// LLM(GPT-4o) HTTP 호출로 마인드맵 무한 트리 마크다운을 생성하며 디스크 영속화를 지원하는 서비스
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Models.Settings;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 마인드맵 트리 생성 서비스 인터페이스
/// </summary>
public interface IMindMapTreeService : IDisposable
{
    /// <summary>
    /// 토픽 변동 시 호출 — 5초 디바운스 후 LLM 호출 실행
    /// </summary>
    void RequestTreeUpdate(IReadOnlyList<TopicSegment> topics, IReadOnlyList<string> minuteSummaries);

    /// <summary>
    /// LLM이 트리 마크다운 생성 완료 시 발화 (UI 스레드로 마샬링은 호출자 책임)
    /// </summary>
    event EventHandler<string>? TreeMarkdownGenerated;

    /// <summary>
    /// 마지막 LLM 응답 (캐시) — 즉시 사용 가능. null이면 아직 생성 전.
    /// </summary>
    string? LastTreeMarkdown { get; }

    /// <summary>
    /// 캐시 및 디바운스 무효화 — 녹음 파일 전환 시 호출하여 할루시네이션 방지
    /// </summary>
    void Reset();

    /// <summary>
    /// 현재 녹음 파일 경로 설정 — 디스크 영속화 대상 지정 (null이면 라이브 녹음 또는 미설정)
    /// </summary>
    void SetCurrentRecording(string? path);

    /// <summary>
    /// 디스크에서 마인드맵 JSON 파일 로드 — 파일 없으면 null 반환
    /// </summary>
    Task<MindMapTreeFile?> LoadFromDiskAsync(string recordingPath, CancellationToken ct = default);

    /// <summary>
    /// 마인드맵 마크다운을 디스크에 저장 — .mindmap.json 파일로 원자적 교체
    /// </summary>
    Task SaveToDiskAsync(string recordingPath, string markdown, bool isUserEdited = false, CancellationToken ct = default);

    /// <summary>
    /// 사용자 편집 마크다운 저장 — isUserEdited=true로 SaveToDiskAsync 호출
    /// </summary>
    Task SaveUserEditedAsync(string recordingPath, string markdown, CancellationToken ct = default);

    /// <summary>
    /// 녹음파일별 스타일 선호 저장 — markdown/isUserEdited 보존, PreferredStyle만 갱신
    /// </summary>
    Task SaveStylePreferenceAsync(string recordingPath, string style, CancellationToken ct = default);

    /// <summary>
    /// 글로벌 default 스타일 로드 — 사용자가 마지막에 선택한 스타일 반환.
    /// 파일 없으면 "radial-tree" 반환.
    /// </summary>
    Task<string> LoadGlobalDefaultStyleAsync(CancellationToken ct = default);

    /// <summary>
    /// 글로벌 default 스타일 저장 — 사용자가 스타일 선택 시 호출
    /// </summary>
    Task SaveGlobalDefaultStyleAsync(string style, CancellationToken ct = default);
}

/// <summary>
/// GPT-4o HTTP 호출로 마인드맵 무한 트리 마크다운을 생성하는 서비스.
/// 5초 디바운스 + 메모리 캐시 + SemaphoreSlim 동시 호출 보호.
/// </summary>
public sealed class MindMapTreeService : IMindMapTreeService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly IHttpClientFactory _httpFactory;
    private readonly AIProvidersSettings _aiProviders;
    private readonly OpenAiRecordingSettings _oaiRecording;
    private readonly SemaphoreSlim _httpLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;
    private string? _lastTreeMarkdown;
    private string? _currentRecordingPath;
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(5);
    private bool _disposed;

    // 글로벌 default 스타일 — 메모리 캐시 + 잠금
    private readonly SemaphoreSlim _globalLock = new(1, 1);
    private string? _globalDefaultStyle;

    // 유효 스타일 5종 (오타/누락 방지)
    private static readonly string[] _validStyles =
        new[] { "radial-tree", "cluster", "force", "mindmap-lr", "sunburst" };

    private static string NormalizeStyle(string? style)
        => string.IsNullOrWhiteSpace(style) || !Array.Exists(_validStyles, s => s == style)
            ? "radial-tree" : style;

    /// <inheritdoc/>
    public event EventHandler<string>? TreeMarkdownGenerated;

    /// <inheritdoc/>
    public string? LastTreeMarkdown => _lastTreeMarkdown;

    /// <summary>
    /// MindMapTreeService 생성자
    /// </summary>
    public MindMapTreeService(
        IHttpClientFactory httpFactory,
        AIProvidersSettings aiProviders,
        OpenAiRecordingSettings oaiRecording)
    {
        _httpFactory = httpFactory;
        _aiProviders = aiProviders;
        _oaiRecording = oaiRecording;
    }

    /// <inheritdoc/>
    public void RequestTreeUpdate(IReadOnlyList<TopicSegment> topics, IReadOnlyList<string> minuteSummaries)
    {
        if (_disposed) return;

        // 디바운스 갱신 — 짧은 시간 안 재호출 시 이전 요청 취소
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounce, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;
                await GenerateTreeAsync(topics, minuteSummaries, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 디바운스 정상 취소 — 무시
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[MMT-실행] RequestTreeUpdate 내부 오류");
            }
        });
    }

    private async Task GenerateTreeAsync(
        IReadOnlyList<TopicSegment> topics,
        IReadOnlyList<string> minuteSummaries,
        CancellationToken ct)
    {
        // 디스크 확인 — isUserEdited=true면 LLM skip (사용자 편집 보존)
        if (!string.IsNullOrWhiteSpace(_currentRecordingPath))
        {
            var existing = await LoadFromDiskAsync(_currentRecordingPath, ct).ConfigureAwait(false);
            if (existing?.IsUserEdited == true)
            {
                _log.Info("[MMRD-skip] LLM 갱신 skip — isUserEdited=true (사용자 편집 보존)");
                // 사용자 편집 마크다운 그대로 발화 (UI 동기화)
                TreeMarkdownGenerated?.Invoke(this, existing.Markdown);
                return;
            }
        }

        // 빈입력 skip — LLM 호출 비용 절감 + 할루시네이션 방지
        var realTopicCount = topics.Count(t => !t.IsSilence && !string.IsNullOrWhiteSpace(t.Title));
        var realSummaryCount = minuteSummaries.Count(s => !string.IsNullOrWhiteSpace(s));
        if (realTopicCount == 0 && realSummaryCount == 0)
        {
            _log.Info("[MMF-실행] GenerateTreeAsync — 빈입력 LLM 호출 skip");
            return;
        }

        // 동시 LLM 호출 방지 — 이미 진행 중이면 skip
        if (!await _httpLock.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _log.Debug("[MMT-실행] LLM 호출 중 — 디바운스 충돌 skip");
            return;
        }

        try
        {
            _log.Info($"[MMT-실행] LLM 트리 생성 요청 — topics={topics.Count} summaries={minuteSummaries.Count}");

            var apiKey = _aiProviders.OpenAI?.ApiKey ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _log.Warn("[MMT-실행] OpenAI ApiKey 미설정 — 트리 생성 건너뜀");
                return;
            }

            var baseUrl = (_aiProviders.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
            var model = _oaiRecording.MinuteSummaryModel ?? "gpt-4o-mini";

            var systemPrompt = """
당신은 회의 녹취를 마인드맵 트리 마크다운으로 변환하는 분석가다.

# 절대 규칙 — 할루시네이션 금지
- 입력에 명시되지 않은 내용을 절대 생성 금지.
- 추론·창작·예시·외부 지식·일반화 금지.
- 입력 텍스트의 직접 인용된 토픽 단어와 키워드만 사용.
- 입력이 짧으면 짧은 트리. 빈 부분 채우지 마라.

# 출력 형식
- markmap 호환 마크다운: # 루트 → - L1 → -- L2 → --- L3 ...
- 깊이 상한 없음, 노드 개수 상한 없음.
- 묵음·잡음·(음)·(어) 등 노이즈 제외.

# 노드 통폐합 가이드라인
- 의미적으로 유사한 토픽 2개 이상은 한 부모 노드 + 자식 분기로 묶어라.
- 한 레벨에 형제 노드가 7개를 넘으면 더 큰 상위 카테고리로 그룹화하라 (Miller 7±2).
- 동의어·유사 표현은 하나로 통합. 동일 단어 반복 금지.
- 깊이 우선(L4, L5 이상) 가로 폭은 좁게 유지.

# 출력 JSON
{"markdown":"# Root\n- ..."}
""";

            var userPrompt = BuildUserPrompt(topics, minuteSummaries);

            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                response_format = new { type = "json_object" },
                temperature = 0,  // [MMF-실행] 할루시네이션 억제 — 0.3→0
                max_completion_tokens = 1024
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
            msg.Headers.Add("Authorization", $"Bearer {apiKey}");
            msg.Content = JsonContent.Create(requestBody);

            using var resp = await client.SendAsync(msg, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                .ConfigureAwait(false);

            var content = body
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                _log.Warn("[MMT-실행] LLM 빈 응답");
                return;
            }

            var parsed = JsonSerializer.Deserialize<JsonElement>(content);
            var markdown = parsed.GetProperty("markdown").GetString();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                _log.Warn("[MMT-실행] LLM 트리 마크다운 비어있음");
                return;
            }

            _lastTreeMarkdown = markdown;
            _log.Info($"[MMRD-실행] LLM 트리 생성 완료 — 줄수={markdown.Split('\n').Length}");
            TreeMarkdownGenerated?.Invoke(this, markdown);

            // 자동 디스크 저장 (라이브 녹음은 _currentRecordingPath null이면 skip)
            if (!string.IsNullOrWhiteSpace(_currentRecordingPath))
            {
                await SaveToDiskAsync(_currentRecordingPath, markdown, isUserEdited: false, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 취소
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[MMT-실행] LLM 트리 생성 실패");
        }
        finally
        {
            _httpLock.Release();
        }
    }

    private static string BuildUserPrompt(
        IReadOnlyList<TopicSegment> topics,
        IReadOnlyList<string> minuteSummaries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("회의 토픽 (시간순):");

        for (int i = 0; i < topics.Count; i++)
        {
            var ts = topics[i];
            if (ts.IsSilence) continue;

            var title = !string.IsNullOrWhiteSpace(ts.Title) ? ts.Title : ts.BodyDisplay;
            if (string.IsNullOrWhiteSpace(title)) continue;

            sb.Append($"[{i + 1}] {title}");

            if (ts.Keywords is { Count: > 0 })
                sb.Append(" — 키워드: " + string.Join(", ", ts.Keywords));

            if (!string.IsNullOrWhiteSpace(ts.Context))
            {
                var ctx = ts.Context.Trim();
                if (ctx.Length > 200) ctx = ctx.Substring(0, 200) + "...";
                sb.Append($" — 맥락: {ctx}");
            }

            sb.AppendLine();
        }

        if (minuteSummaries.Count > 0)
        {
            sb.AppendLine("\n분당 요약:");
            for (int i = 0; i < minuteSummaries.Count; i++)
            {
                sb.AppendLine($"({i + 1}분) {minuteSummaries[i]}");
            }
        }

        sb.AppendLine("\n위 내용을 깊이/노드 수 제한 없는 마인드맵 트리 마크다운으로 작성하라.");
        return sb.ToString();
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _debounceCts?.Cancel();
        _lastTreeMarkdown = null;
        _currentRecordingPath = null;
        _log.Info("[MMF-실행] MindMapTreeService.Reset — 캐시+디바운스+녹음경로 무효화");
    }

    /// <inheritdoc/>
    public void SetCurrentRecording(string? path)
    {
        // 현재 녹음 파일 경로 설정 — 디스크 영속화 대상 지정
        _currentRecordingPath = path;
        _log.Info($"[MMRD-실행] SetCurrentRecording — '{path ?? "<null/live>"}'");
    }

    /// <inheritdoc/>
    public async Task<MindMapTreeFile?> LoadFromDiskAsync(string recordingPath, CancellationToken ct = default)
    {
        // 디스크에서 마인드맵 JSON 파일 로드
        if (string.IsNullOrWhiteSpace(recordingPath)) return null;
        var jsonPath = recordingPath + ".mindmap.json";
        if (!File.Exists(jsonPath)) return null;
        try
        {
            using var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var file = await JsonSerializer.DeserializeAsync<MindMapTreeFile>(fs, cancellationToken: ct).ConfigureAwait(false);
            _log.Info($"[MMRD-실행] LoadFromDisk — '{jsonPath}' isUserEdited={file?.IsUserEdited}");
            if (file != null && !string.IsNullOrWhiteSpace(file.Markdown))
            {
                _lastTreeMarkdown = file.Markdown;
            }
            return file;
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"[MMRD-실행] LoadFromDisk 실패 — {jsonPath}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveToDiskAsync(string recordingPath, string markdown, bool isUserEdited = false, CancellationToken ct = default)
    {
        // 마인드맵 마크다운을 디스크에 원자적 교체로 저장
        if (string.IsNullOrWhiteSpace(recordingPath)) return;
        var jsonPath = recordingPath + ".mindmap.json";
        try
        {
            var file = new MindMapTreeFile { Markdown = markdown, IsUserEdited = isUserEdited, UpdatedAt = DateTime.UtcNow };
            var tmpPath = jsonPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, file, new JsonSerializerOptions { WriteIndented = false }, ct).ConfigureAwait(false);
            }
            // 원자적 교체
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            File.Move(tmpPath, jsonPath);
            _log.Info($"[MMRD-실행] SaveToDisk — '{jsonPath}' isUserEdited={isUserEdited} markdown={markdown.Length}자");
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"[MMRD-실행] SaveToDisk 실패 — {jsonPath}");
        }
    }

    /// <inheritdoc/>
    public Task SaveUserEditedAsync(string recordingPath, string markdown, CancellationToken ct = default)
        // 사용자 편집 마크다운 저장 — isUserEdited=true
        => SaveToDiskAsync(recordingPath, markdown, isUserEdited: true, ct);

    private static string GetGlobalStylePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MaiX");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "mindmap_default_style.json");
    }

    /// <inheritdoc/>
    public async Task<string> LoadGlobalDefaultStyleAsync(CancellationToken ct = default)
    {
        // [MMS-실행] 글로벌 default 스타일 로드 — 메모리 캐시 우선
        if (!string.IsNullOrWhiteSpace(_globalDefaultStyle))
            return _globalDefaultStyle!;

        await _globalLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_globalDefaultStyle)) return _globalDefaultStyle!;
            var path = GetGlobalStylePath();
            if (!File.Exists(path))
            {
                _globalDefaultStyle = "radial-tree";
                _log.Info("[MMS-실행] LoadGlobalDefaultStyle — 파일 없음, default 'radial-tree' 반환");
                return _globalDefaultStyle;
            }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var obj = await JsonSerializer.DeserializeAsync<JsonElement>(fs, cancellationToken: ct).ConfigureAwait(false);
            var style = obj.TryGetProperty("defaultStyle", out var p) ? p.GetString() : null;
            _globalDefaultStyle = NormalizeStyle(style);
            _log.Info($"[MMS-실행] LoadGlobalDefaultStyle — '{_globalDefaultStyle}'");
            return _globalDefaultStyle;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[MMS-실행] LoadGlobalDefaultStyle 실패");
            _globalDefaultStyle = "radial-tree";
            return _globalDefaultStyle;
        }
        finally
        {
            _globalLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SaveGlobalDefaultStyleAsync(string style, CancellationToken ct = default)
    {
        // [MMS-실행] 글로벌 default 스타일 저장 — 원자적 교체
        var normalized = NormalizeStyle(style);
        await _globalLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = GetGlobalStylePath();
            var tmpPath = path + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, new { defaultStyle = normalized }, cancellationToken: ct).ConfigureAwait(false);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmpPath, path);
            _globalDefaultStyle = normalized;
            _log.Info($"[MMS-실행] SaveGlobalDefaultStyle — '{normalized}'");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[MMS-실행] SaveGlobalDefaultStyle 실패");
        }
        finally
        {
            _globalLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SaveStylePreferenceAsync(string recordingPath, string style, CancellationToken ct = default)
    {
        // [MMS-실행] 녹음파일별 스타일 선호 저장 — markdown/isUserEdited 보존, PreferredStyle만 갱신
        if (string.IsNullOrWhiteSpace(recordingPath)) return;
        var normalized = NormalizeStyle(style);
        var existing = await LoadFromDiskAsync(recordingPath, ct).ConfigureAwait(false) ?? new MindMapTreeFile();
        existing.PreferredStyle = normalized;
        existing.UpdatedAt = DateTime.UtcNow;
        var jsonPath = recordingPath + ".mindmap.json";
        try
        {
            var tmpPath = jsonPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, existing, cancellationToken: ct).ConfigureAwait(false);
            }
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            File.Move(tmpPath, jsonPath);
            _log.Info($"[MMS-실행] SaveStylePreference — '{jsonPath}' style='{normalized}'");
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"[MMS-실행] SaveStylePreference 실패 — {jsonPath}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        _httpLock.Dispose();
        _globalLock.Dispose();
    }
}
