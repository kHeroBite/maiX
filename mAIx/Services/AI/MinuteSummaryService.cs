// 60초 PeriodicTimer 기반 1분 요약 서비스 (SemaphoreSlim 동시 호출 보호 + 디스크 JSON 저장)
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

            var combinedText = string.Join(" ", texts);
            var model = _settings.OaiRecording.MinuteSummaryModel;
            var baseUrl = (_settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
            var url = baseUrl + "/chat/completions";

            const string systemPrompt = """
                반드시 다음 JSON 형식으로만 응답하라. 다른 설명/마크다운 금지.
                {
                  "summary": "30~150자 요약 텍스트",
                  "topic": "5~20자 주제어 또는 주제맥락 (예: '하네스엔지니어링 설명', '바이브코딩의 미래에 대한 분석')"
                }
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
                max_completion_tokens = 256,
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
            var (summaryText, topic) = ExtractSummaryAndTopic(llmContent);

            if (string.IsNullOrWhiteSpace(summaryText)) return;

            var entry = new MinuteSummaryEntry
            {
                Index = _entryIndex++,
                StartTime = startTime,
                EndTime = endTime,
                SummaryText = summaryText,
                Topic = topic,
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

    /// <summary>
    /// LLM 응답 content 문자열에서 summary와 topic을 추출한다.
    /// JSON 파싱 실패 시 summary=원문(150자), topic=summary앞20자 fallback.
    /// </summary>
    private static (string summary, string topic) ExtractSummaryAndTopic(string llmContent)
    {
        if (string.IsNullOrWhiteSpace(llmContent))
            return (string.Empty, string.Empty);

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

            // topic 길이 보정: 5자 미만이면 summary 앞 20자, 20자 초과면 앞 20자 truncate
            topic = topic.Trim();
            if (topic.Length < 5)
                topic = summary.Trim().Length > 20 ? summary.Trim()[..20] : summary.Trim();
            else if (topic.Length > 20)
                topic = topic[..20];

            return (summary, topic);
        }
        catch
        {
            // JSON 파싱 실패 — 원문 그대로 fallback
            var fallbackSummary = llmContent.Length > 150 ? llmContent[..150] : llmContent;
            var fallbackTopic = fallbackSummary.Trim();
            fallbackTopic = fallbackTopic.Length > 20 ? fallbackTopic[..20] : fallbackTopic;
            return (fallbackSummary, fallbackTopic);
        }
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
