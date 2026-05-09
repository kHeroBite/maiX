// PeriodicTimer 기반 주제어 추출 서비스 (10~15초마다 LLM 호출, Jaccard 세그먼트 분기)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Models.Settings;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 주제어 추출 서비스 인터페이스
/// </summary>
public interface ITopicExtractorService : IDisposable
{
    /// <summary>
    /// 새 주제어 세그먼트 추가 이벤트
    /// </summary>
    event Action<TopicSegment>? TopicSegmentAdded;

    /// <summary>
    /// 기존 주제어 세그먼트 갱신 이벤트
    /// </summary>
    event Action<TopicSegment>? TopicSegmentUpdated;

    /// <summary>
    /// 서비스 시작
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 서비스 중지
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// STT 텍스트 추가 (수신 즉시 버퍼링)
    /// </summary>
    Task AddTranscriptAsync(string text, TimeSpan time);
}

/// <summary>
/// 10~15초 PeriodicTimer로 LLM 호출하여 주제어 세그먼트를 추출/갱신하는 서비스
/// </summary>
public sealed class TopicExtractorService : ITopicExtractorService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;
    private readonly HttpClient _httpClient = new();

    private CancellationTokenSource? _cts;
    private Task? _timerTask;
    private bool _running;
    private bool _disposed;

    // STT 텍스트 버퍼 (마지막 추출 이후 누적)
    private readonly List<(string text, TimeSpan time)> _buffer = new();
    private readonly object _bufferLock = new();

    // 현재 세그먼트 목록
    private readonly List<TopicSegment> _segments = new();
    private int _nextSegmentId = 0;

    // 추출 주기: 12초 (10~15초 사이)
    private static readonly TimeSpan ExtractInterval = TimeSpan.FromSeconds(12);

    /// <inheritdoc/>
    public event Action<TopicSegment>? TopicSegmentAdded;

    /// <inheritdoc/>
    public event Action<TopicSegment>? TopicSegmentUpdated;

    public TopicExtractorService(AppSettingsManager settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            _log.Warn("[TopicExtractor] 이미 실행 중");
            return Task.CompletedTask;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = true;
        _timerTask = RunTimerLoopAsync(_cts.Token);
        _log.Info("[TopicExtractor] 시작 (interval={Interval}s, model={Model})", ExtractInterval.TotalSeconds, _settings.OaiRecording.KeywordExtractModel);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        _log.Info("[TopicExtractor] 중지");
        _running = false;
        _cts?.Cancel();
        if (_timerTask != null)
        {
            try { await _timerTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
        }
    }

    /// <inheritdoc/>
    public Task AddTranscriptAsync(string text, TimeSpan time)
    {
        if (!_running || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        lock (_bufferLock)
        {
            _buffer.Add((text, time));
        }
        return Task.CompletedTask;
    }

    private async Task RunTimerLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ExtractInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                List<(string text, TimeSpan time)> snapshot;
                lock (_bufferLock)
                {
                    if (_buffer.Count == 0) continue;
                    snapshot = new List<(string text, TimeSpan time)>(_buffer);
                    _buffer.Clear();
                }

                await ExtractTopicAsync(snapshot, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[TopicExtractor] 타이머 루프 오류");
        }
    }

    private async Task ExtractTopicAsync(List<(string text, TimeSpan time)> snapshot, CancellationToken ct)
    {
        var combinedText = string.Join(" ", snapshot.Select(s => s.text));
        var earliestTime = snapshot.Min(s => s.time);
        var latestTime = snapshot.Max(s => s.time);

        var prevKeywords = _segments.Count > 0 ? _segments[^1].Keywords : new List<string>();
        var prevKeywordsStr = prevKeywords.Count > 0 ? string.Join(", ", prevKeywords) : "(없음)";

        var prompt = $$"""
            다음은 녹음 전사 텍스트 일부입니다.

            [전사 텍스트]
            {{combinedText}}

            [이전 주제 키워드]
            {{prevKeywordsStr}}

            위 전사 텍스트에서 주제어(키워드)를 3~5개 추출하고, 이전 주제와 동일한지 판단하세요.
            반드시 다음 JSON 형식으로만 응답하세요 (마크다운 코드블록 없이):
            {"keywords": ["키워드1", "키워드2", "키워드3"], "topic_changed": true, "summary_preview": "한 줄 요약"}
            """;

        try
        {
            var model = _settings.OaiRecording.KeywordExtractModel;
            var baseUrl = (_settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
            var url = baseUrl + "/chat/completions";

            var requestBody = new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                max_completion_tokens = 256,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warn("[TopicExtractor] API 오류 {Status}", response.StatusCode);
                return;
            }

            var respJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            ParseAndApplyResult(respJson, earliestTime, latestTime);
        }
        catch (OperationCanceledException)
        {
            // 정상 취소
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[TopicExtractor] 주제어 추출 실패");
        }
    }

    private void ParseAndApplyResult(string responseJson, TimeSpan startTime, TimeSpan endTime)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // choices[0].message.content 추출
            string? contentStr = null;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0];
                if (msg.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    contentStr = contentProp.GetString();
                }
            }

            if (string.IsNullOrEmpty(contentStr)) return;

            using var innerDoc = JsonDocument.Parse(contentStr);
            var innerRoot = innerDoc.RootElement;

            var keywords = new List<string>();
            if (innerRoot.TryGetProperty("keywords", out var kws))
            {
                foreach (var kw in kws.EnumerateArray())
                {
                    var s = kw.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) keywords.Add(s);
                }
            }

            var topicChanged = !innerRoot.TryGetProperty("topic_changed", out var tc) || tc.GetBoolean();
            var summaryPreview = innerRoot.TryGetProperty("summary_preview", out var sp) ? sp.GetString() ?? string.Empty : string.Empty;

            // Jaccard ≤ 0.5 → 새 세그먼트, 아니면 기존 갱신
            var prevKeywords = _segments.Count > 0 ? _segments[^1].Keywords : new List<string>();
            var jaccard = CalcJaccard(new HashSet<string>(prevKeywords), new HashSet<string>(keywords));
            _log.Debug("[TopicExtractor] Jaccard={J:F2}, topicChanged={C}", jaccard, topicChanged);

            if (_segments.Count == 0 || jaccard <= 0.5 || topicChanged)
            {
                // 인접 충돌 회피: 직전 세그먼트와 다른 팔레트 인덱스 선택
                int paletteIdx = _nextSegmentId % TopicSegment.PastelPalette.Length;
                if (_segments.Count > 0)
                {
                    var prevColorIdx = (_nextSegmentId - 1) % TopicSegment.PastelPalette.Length;
                    if (paletteIdx == prevColorIdx)
                        paletteIdx = (paletteIdx + 1) % TopicSegment.PastelPalette.Length;
                }

                var newSegment = new TopicSegment
                {
                    Id = _nextSegmentId++,
                    StartTime = startTime,
                    EndTime = endTime,
                    Keywords = keywords,
                    DisplayTitle = keywords.Count > 0 ? string.Join(" · ", keywords.Take(2)) : "(주제 없음)",
                    SummaryPreview = summaryPreview,
                    BackgroundColorHex = TopicSegment.PastelPalette[paletteIdx]
                };

                _segments.Add(newSegment);
                _log.Info("[TopicExtractor] 새 세그먼트 #{Id}: {Title}", newSegment.Id, newSegment.DisplayTitle);
                TopicSegmentAdded?.Invoke(newSegment);
            }
            else
            {
                // 기존 마지막 세그먼트 갱신
                var last = _segments[^1];
                last.EndTime = endTime;
                last.SummaryPreview = summaryPreview;
                if (keywords.Count > 0) last.Keywords = keywords;
                _log.Debug("[TopicExtractor] 세그먼트 #{Id} 갱신", last.Id);
                TopicSegmentUpdated?.Invoke(last);
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[TopicExtractor] 결과 파싱 실패");
        }
    }

    private static double CalcJaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var intersection = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
        var union = a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _httpClient.Dispose();
    }
}
