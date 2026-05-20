// 60초 PeriodicTimer 기반 1분 요약 서비스 (SemaphoreSlim 동시 호출 보호 + 디스크 JSON 저장)
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Models.Settings;
using mAIx.Services.AI.Testing;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 1분 요약 서비스 인터페이스
/// </summary>
public interface IMinuteSummaryService : IDisposable
{
    /// <summary>
    /// 1분 요약 엔트리 생성 이벤트
    /// </summary>
    event Action<MinuteSummaryEntry>? MinuteSummaryCreated;

    /// <summary>
    /// 서비스 시작
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 서비스 중지
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// STT 텍스트 추가
    /// </summary>
    Task AddTranscriptAsync(string text);

    /// <summary>
    /// 전체 1분 요약 목록 반환
    /// </summary>
    Task<IReadOnlyList<MinuteSummaryEntry>> GetAllMinuteSummariesAsync();
}

/// <summary>
/// 60초마다 LLM 호출하여 1분 요약을 생성하고 디스크에 저장하는 서비스
/// </summary>
public sealed class MinuteSummaryService : IMinuteSummaryService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;
    private readonly HttpClient _httpClient = new();

    // 동시 호출 보호 (클래스 필드 → Dispose에서 해제)
    private readonly SemaphoreSlim _summarySemaphore = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _timerTask;
    private bool _running;
    private bool _disposed;

    private readonly List<MinuteSummaryEntry> _entries = new();
    private readonly List<string> _currentMinuteBuffer = new();
    private readonly object _bufferLock = new();

    private int _entryIndex = 0;
    private TimeSpan _minuteStartTime = TimeSpan.Zero;
    private TimeSpan _recordingElapsed = TimeSpan.Zero;

    // 저장 디렉토리
    private static readonly string SaveDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mAIx", "Recordings", "MinuteSummaries");

    /// <inheritdoc/>
    public event Action<MinuteSummaryEntry>? MinuteSummaryCreated;

    public MinuteSummaryService(AppSettingsManager settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ConfigureHttpClient();
        Directory.CreateDirectory(SaveDirectory);
    }

    private void ConfigureHttpClient()
    {
        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            _log.Warn("[MinuteSummary] 이미 실행 중");
            return Task.CompletedTask;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = true;
        _minuteStartTime = TimeSpan.Zero;
        _recordingElapsed = TimeSpan.Zero;
        _timerTask = RunTimerLoopAsync(_cts.Token);
        _log.Info("[MinuteSummary] 시작 (model={Model})", _settings.OaiRecording.MinuteSummaryModel);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        _log.Info("[MinuteSummary] 중지");
        _running = false;
        _cts?.Cancel();
        if (_timerTask != null)
        {
            try { await _timerTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
        }
    }

    /// <inheritdoc/>
    public Task AddTranscriptAsync(string text)
    {
        _log.Debug("[MinuteSummary] AddTranscriptAsync 진입 — text length={Len}, _running={Running}", text?.Length ?? 0, _running);
        if (_disposed) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        lock (_bufferLock)
        {
            _currentMinuteBuffer.Add(text);
            _recordingElapsed = TimeSpan.FromSeconds(_entryIndex * 60 + (DateTime.Now - _lastStartedAt).TotalSeconds);
        }
        return Task.CompletedTask;
    }

    private DateTime _lastStartedAt = DateTime.Now;

    /// <inheritdoc/>
    public Task<IReadOnlyList<MinuteSummaryEntry>> GetAllMinuteSummariesAsync()
    {
        lock (_bufferLock)
        {
            return Task.FromResult<IReadOnlyList<MinuteSummaryEntry>>(_entries.AsReadOnly());
        }
    }

    private async Task RunTimerLoopAsync(CancellationToken ct)
    {
        _lastStartedAt = DateTime.Now;
        // 옵션 패널 ProcessingIntervalSeconds (기본 60초) × DebugTimerScale
        var envScale = Environment.GetEnvironmentVariable("MAIX_DEBUG_TIMER_SCALE");
        var timerScale = double.TryParse(envScale, out var parsed) ? parsed : _settings.OaiRecording.DebugTimerScale;
        var baseSeconds = Math.Max(5, _settings.OaiRecording.ProcessingIntervalSeconds);
        var timerInterval = TimeSpan.FromSeconds(Math.Max(1.0, baseSeconds * timerScale));
        _log.Info("[MinuteSummary] PeriodicTimer 주기={Interval}s (옵션={Base}s, scale={Scale})", timerInterval.TotalSeconds, baseSeconds, timerScale);
        using var timer = new PeriodicTimer(timerInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                List<string> snapshot;
                TimeSpan startTime, endTime;

                lock (_bufferLock)
                {
                    _log.Debug("[MinuteSummary] PeriodicTimer 틱 — buffer={Count}개", _currentMinuteBuffer.Count);
                    if (_currentMinuteBuffer.Count == 0)
                    {
                        _log.Info("[MinuteSummary] PeriodicTimer 스킵 — STT 버퍼 없음 (아직 텍스트 미수신)");
                        continue;
                    }
                    snapshot = new List<string>(_currentMinuteBuffer);
                    _currentMinuteBuffer.Clear();
                    startTime = _minuteStartTime;
                    endTime = startTime + TimeSpan.FromSeconds(60);
                    _minuteStartTime = endTime;
                    _lastStartedAt = DateTime.Now;
                }

                _log.Info("[MinuteSummary] SummarizeMinuteAsync 시작 — segments={Count}", snapshot.Count);
                await SummarizeMinuteAsync(snapshot, startTime, endTime, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[MinuteSummary] 타이머 루프 오류");
        }
    }

    private async Task SummarizeMinuteAsync(List<string> texts, TimeSpan startTime, TimeSpan endTime, CancellationToken ct)
    {
        await _summarySemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Mock 분기 — EnableMock=true 시 실호출 없이 mock 요약 반환
            if (MockOpenAiResponseInjector.TryHandleMinuteSummary(out var mockSummary))
            {
                var mockEntry = new MinuteSummaryEntry
                {
                    Index = _entryIndex++,
                    StartTime = startTime,
                    EndTime = endTime,
                    SummaryText = mockSummary,
                    CreatedAt = DateTime.Now
                };
                lock (_bufferLock) { _entries.Add(mockEntry); }
                MinuteSummaryCreated?.Invoke(mockEntry);
                _log.Info("[MinuteSummary] Mock 1분 요약 완료: {Text}", mockSummary);
                return;
            }

            // 전체 묵음 판정 — LLM 호출 스킵, "묵음" 엔트리 생성
            if (IsAllSilence(texts))
            {
                _log.Info("[MinuteSummary] 전체 묵음 구간 감지 — LLM 스킵");
                var silentEntry = new MinuteSummaryEntry
                {
                    Index = _entryIndex++,
                    StartTime = startTime,
                    EndTime = endTime,
                    SummaryText = "묵음",
                    Topic = "묵음",
                    Keywords = new List<string>(),
                    IsSilence = true,
                    CreatedAt = DateTime.Now
                };
                lock (_bufferLock) { _entries.Add(silentEntry); }
                MinuteSummaryCreated?.Invoke(silentEntry);
                return;
            }

            var combinedText = string.Join(" ", texts);
            var model = _settings.OaiRecording.MinuteSummaryModel;
            var baseUrl = (_settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
            var url = baseUrl + "/chat/completions";

            const string systemPrompt = """
                반드시 다음 JSON 형식으로만 응답하라. 다른 텍스트 절대 포함 금지.
                {
                  "title": "20자 이내 카드 제목 (예: '하네스엔지니어링 자기소개')",
                  "topic": "5~10자 핵심 주제어 (예: '자기소개')",
                  "context": "주제어의 배경/이유 한 줄 (30~80자)",
                  "summary": "30~150자 요약 텍스트",
                  "keywords": ["고유명사·전문용어·핵심명사만 3~5개"]
                }

                엄격한 규칙:
                1. topic은 다음 일반어를 절대 사용하지 마라: 회의내용, 회의 준비, 회의, 내용, 준비, 녹음, 대화
                   (대화의 진짜 핵심 키워드를 뽑아라. 예: '예산 협의', 'API 설계 토론')
                2. keywords는 명사/고유명사만. 조사/지시대명사/일반어 제외.
                3. summary는 회의 진행 사실이 아닌 실제 내용 요약.
                """;

            var userPrompt = $"""
                다음은 녹음 전사 텍스트 (구간: {startTime.ToString(@"mm\:ss")} ~ {endTime.ToString(@"mm\:ss")}) 입니다.

                [전사 텍스트]
                {combinedText}
                """;

            var requestBody = new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_completion_tokens = 320,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warn("[MinuteSummary] API 오류 {Status}", response.StatusCode);
                return;
            }

            var respJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var llmContent = ExtractSummaryText(respJson);
            var (summaryText, topic, title, context, keywords) = ExtractAll(llmContent);

            if (string.IsNullOrWhiteSpace(summaryText)) return;

            _log.Info("[MinuteSummary] 5필드 파싱 — title={Title}, topic={Topic}, context={ContextLen}자, keywords={KwCount}개",
                title, topic, context.Length, keywords.Count);

            var entry = new MinuteSummaryEntry
            {
                Index = _entryIndex++,
                StartTime = startTime,
                EndTime = endTime,
                SummaryText = summaryText,
                Topic = topic,
                Title = title,
                Context = context,
                Keywords = keywords,
                CreatedAt = DateTime.Now
            };

            lock (_bufferLock)
            {
                _entries.Add(entry);
            }

            await SaveEntryToDiskAsync(entry).ConfigureAwait(false);

            _log.Info("[MinuteSummary] 1분 요약 생성 #{Idx}: {Preview}", entry.Index,
                entry.SummaryText.Length > 80 ? entry.SummaryText[..80] : entry.SummaryText);

            MinuteSummaryCreated?.Invoke(entry);
        }
        catch (OperationCanceledException)
        {
            // 정상 취소
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[MinuteSummary] 요약 생성 실패");
        }
        finally
        {
            _summarySemaphore.Release();
        }
    }

    private static string ExtractSummaryText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0];
                if (msg.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    return contentProp.GetString() ?? string.Empty;
                }
            }
        }
        catch { /* 무시 */ }
        return string.Empty;
    }

    // ─── AC-003 topic 블랙리스트 ────────────────────────────────────────
    private static readonly HashSet<string> _topicBlacklist = new()
    {
        "회의내용", "회의 준비", "회의", "내용", "준비", "녹음", "대화", "이야기"
    };

    // ─── AC-007 keywords 불용어 사전 (odev-1 AllTopicKeywords와 동일 사본) ──
    private static readonly HashSet<string> _stopWords = new()
    {
        "이것", "그것", "저것", "때문", "정도", "관련", "내용", "준비", "회의",
        "문제", "경우", "방법", "시간", "오늘", "어제", "내일", "여기", "거기",
        "우리", "저희", "이거", "그거"
    };

    /// <summary>
    /// LLM 응답 content 문자열에서 5필드(summary/topic/title/context/keywords)를 추출한다 (AC-011).
    /// 옛 2필드(summary/topic) 응답도 graceful 처리 — title/context는 string.Empty 반환.
    /// JSON 파싱 실패 시 summary=원문(150자), topic=summary앞10자, title/context=Empty, keywords=빈목록.
    /// </summary>
    private static (string summary, string topic, string title, string context, List<string> keywords) ExtractAll(string llmContent)
    {
        if (string.IsNullOrWhiteSpace(llmContent))
            return (string.Empty, string.Empty, string.Empty, string.Empty, new List<string>());

        try
        {
            using var doc = JsonDocument.Parse(llmContent);
            var root = doc.RootElement;

            var summary = root.TryGetProperty("summary", out var sumProp)
                ? (sumProp.GetString() ?? string.Empty)
                : string.Empty;
            var topic = root.TryGetProperty("topic", out var topicProp)
                ? (topicProp.GetString() ?? string.Empty)
                : string.Empty;
            // title/context — 옛 응답 부재 시 string.Empty (graceful 호환)
            var title = root.TryGetProperty("title", out var titleProp)
                ? (titleProp.GetString() ?? string.Empty)
                : string.Empty;
            var context = root.TryGetProperty("context", out var ctxProp)
                ? (ctxProp.GetString() ?? string.Empty)
                : string.Empty;

            // keywords 추출 (배열 아니면 빈 목록 — 기존 응답 호환 graceful)
            var keywords = new List<string>();
            if (root.TryGetProperty("keywords", out var kwProp) &&
                kwProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var kw in kwProp.EnumerateArray())
                {
                    if (kw.ValueKind != JsonValueKind.String) continue;
                    var s = kw.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(s) && s.Length >= 2 && s.Length <= 30 && !keywords.Contains(s))
                        keywords.Add(s);
                }
            }

            // AC-007 keywords 불용어 필터
            var beforeCount = keywords.Count;
            keywords = keywords
                .Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 2 && !_stopWords.Contains(k.Trim()))
                .ToList();
            if (beforeCount != keywords.Count)
                _log.Info("[MinuteSummary] AC-007 stopwords 필터 — {Before}개 → {After}개", beforeCount, keywords.Count);

            // topic 길이 보정: 5자 미만이면 summary 앞 10자
            topic = topic.Trim();
            if (topic.Length < 5)
                topic = summary.Trim().Length > 10 ? summary.Trim()[..10] : summary.Trim();
            else if (topic.Length > 20)
                topic = topic[..20];

            // AC-003 topic 블랙리스트 — 일반어 매치 시 summary 앞 10자 fallback
            if (_topicBlacklist.Contains(topic.Trim()))
            {
                var fallback = summary.Trim();
                var oldTopic = topic;
                topic = fallback.Length > 10 ? fallback[..10] : fallback;
                _log.Info("[MinuteSummary] AC-003 topic blacklist match ({Old}) → fallback: {New}", oldTopic, topic);
            }

            return (summary, topic, title, context, keywords);
        }
        catch
        {
            // JSON 파싱 실패 — 원문 그대로 fallback
            var fallbackSummary = llmContent.Length > 150 ? llmContent[..150] : llmContent;
            var fallbackTopic = fallbackSummary.Trim();
            fallbackTopic = fallbackTopic.Length > 10 ? fallbackTopic[..10] : fallbackTopic;
            return (fallbackSummary, fallbackTopic, string.Empty, string.Empty, new List<string>());
        }
    }

    /// <summary>
    /// [호환 wrapper] ExtractAll에서 (summary, topic) 튜플만 반환.
    /// 옛 코드 경로 호환 — 내부적으로 ExtractAll 호출.
    /// </summary>
    private static (string summary, string topic, List<string> keywords) ExtractSummaryAndTopic(string llmContent)
    {
        var (summary, topic, _, _, keywords) = ExtractAll(llmContent);
        return (summary, topic, keywords);
    }

    /// <summary>
    /// texts 목록 전체가 "[묵음 N초]" / "[묵음 N.N초]" 마커만으로 구성된 경우 true.
    /// 실발화 텍스트가 단 하나라도 있으면 false.
    /// </summary>
    private static bool IsAllSilence(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0) return false;
        foreach (var t in texts)
        {
            var trimmed = t?.Trim() ?? string.Empty;
            // 묵음 마커 형식: "[묵음 N초]" 또는 "[묵음 N.N초]"
            if (!Regex.IsMatch(
                    trimmed, @"^\[묵음 [\d.]+초\]$"))
                return false;
        }
        return true;
    }

    private async Task SaveEntryToDiskAsync(MinuteSummaryEntry entry)
    {
        try
        {
            var fileName = $"minute_{entry.CreatedAt:yyyyMMdd_HHmmss}_{entry.Index:D4}.json";
            var filePath = Path.Combine(SaveDirectory, fileName);
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8).ConfigureAwait(false);
            _log.Debug("[MinuteSummary] 디스크 저장: {Path}", filePath);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[MinuteSummary] 디스크 저장 실패");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _summarySemaphore.Dispose();
        _httpClient.Dispose();
    }
}
