// LLM(GPT-4o) HTTP 호출로 마인드맵 무한 트리 마크다운을 생성하는 서비스
using System;
using System.Collections.Generic;
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
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(5);
    private bool _disposed;

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

            var systemPrompt =
                "당신은 회의 녹취를 마인드맵 트리 마크다운으로 변환하는 분석가다. " +
                "출력은 markmap 호환 마크다운: # 루트 → ## L2 → ### L3 → #### L4... " +
                "깊이 상한 없음, 노드 개수 상한 없음. " +
                "의미적 유사 토픽은 묶고, 발언 흐름과 인과를 반영하라. " +
                "묵음/잡음/(음)/(어) 등 노이즈는 제외. " +
                "출력 JSON: {\"markdown_tree\":\"# Root\\n## ...\"}.";

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
                temperature = 0.3,
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
            var markdown = parsed.GetProperty("markdown_tree").GetString();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                _log.Warn("[MMT-실행] LLM 트리 마크다운 비어있음");
                return;
            }

            _lastTreeMarkdown = markdown;
            _log.Info($"[MMT-실행] LLM 트리 생성 완료 — 줄수={markdown.Split('\n').Length}");
            TreeMarkdownGenerated?.Invoke(this, markdown);
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
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        _httpLock.Dispose();
    }
}
