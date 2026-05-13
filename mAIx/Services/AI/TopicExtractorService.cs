// PeriodicTimer 기반 주제어 추출 서비스 (ProcessingIntervalSeconds마다 LLM 호출, Jaccard 세그먼트 분기)
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
    /// 세그먼트 통폐합(그루핑) 완료 이벤트 — 새 전체 목록 전달
    /// </summary>
    event Action<IReadOnlyList<TopicSegment>>? TopicSegmentsConsolidated;

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

    // 추출 주기: 설정값 기반 동적 주기 (StartAsync에서 결정)

    /// <inheritdoc/>
    public event Action<TopicSegment>? TopicSegmentAdded;

    /// <inheritdoc/>
    public event Action<TopicSegment>? TopicSegmentUpdated;

    /// <inheritdoc/>
    public event Action<IReadOnlyList<TopicSegment>>? TopicSegmentsConsolidated;

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
        var intervalSec = Math.Max(5.0, _settings.OaiRecording.ProcessingIntervalSeconds);
        _log.Info($"[TopicExtractor] 시작 (interval={intervalSec}s, model={_settings.OaiRecording.KeywordExtractModel})");
        _timerTask = RunTimerLoopAsync(_cts.Token, TimeSpan.FromSeconds(intervalSec));
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

    private async Task RunTimerLoopAsync(CancellationToken ct, TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
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
                await ConsolidateTopicsIfNeededAsync(ct).ConfigureAwait(false);
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
        var prevSummary = _segments.Count > 0 ? (_segments[^1].SummaryPreview ?? string.Empty) : string.Empty;
        var prevSummaryStr = string.IsNullOrWhiteSpace(prevSummary) ? "(없음)" : prevSummary;

        var prompt = $$"""
            다음은 녹음 전사 텍스트 일부입니다.

            [새로 추가된 전사 텍스트]
            {{combinedText}}

            [직전 핵심요약 — 같은 안건이면 이를 발전·확장한 통합 요약을 생성]
            요약문: {{prevSummaryStr}}
            키워드: {{prevKeywordsStr}}

            지시사항 (병합·분리 균형 판단):
            1. **같은 안건이면 병합** (topic_changed=false):
               - 같은 회의 안건의 깊이 있는 논의, 같은 작업의 세부 단계, 동일 주제의 연속 발화
               - summary_preview = 직전 요약 + 새 정보를 통합·확장한 한 줄 (이전 내용 유지하며 발전, 10~30자)
               - keywords = 직전 키워드 + 새 키워드 union 중 가장 대표적인 1~3개
            2. **다른 안건으로 전환되면 분리** (topic_changed=true):
               - 회의에서 다음 안건으로 명확히 이동 ("이제 다음 건은…", "그럼 ~로 넘어가서")
               - 대화 중 화제가 명확히 바뀜 (예: "프로젝트 일정" → "점심 메뉴", "코드 리뷰" → "휴가 계획")
               - 새 인물/장소/상황으로 전환
               - 같은 큰 주제 안이라도 **명확히 구분되는 하위 안건**이면 분리 OK
            3. 판단 가이드:
               - 직전 핵심요약과 새 텍스트의 **주제어/맥락이 50% 이상 겹치면 false** (병합)
               - **30% 이하만 겹치면 true** (분리)
               - 30~50% 사이면 더 구체적인 새 정보 비중으로 판단

            예시:
              직전: "Q3 매출 보고" → 새: "Q3 영업이익 분석" → false (같은 Q3 실적)
              직전: "Q3 매출 보고" → 새: "신입 채용 일정" → true (완전 다른 안건)
              직전: "코드 리뷰" → 새: "리뷰 후 배포 일정" → false (코드 리뷰 연장)
              직전: "코드 리뷰" → 새: "점심 뭐 먹지" → true (잡담 전환)

            반드시 다음 JSON 형식으로만 응답하세요 (마크다운 코드블록 없이):
            {"keywords": ["핵심1", "핵심2"], "topic_changed": false, "summary_preview": "통합·확장된 한 줄 핵심요약"}
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

            // topic_changed 기본 false (LLM이 명시 true 반환 시에만 신뢰) — 보수적 그룹화
            var topicChanged = innerRoot.TryGetProperty("topic_changed", out var tc) && tc.GetBoolean();
            var summaryPreview = innerRoot.TryGetProperty("summary_preview", out var sp) ? sp.GetString() ?? string.Empty : string.Empty;

            // 핵심요약 카드 분리 — 100% LLM 위임 (사용자 요청):
            // - LLM이 topic_changed=true 반환 시 즉시 신규 세그먼트
            // - 보조 휴리스틱: 직전 카드 5분+ AND Jaccard ≤ 0.2 → 시간 강제 분리 (LLM이 모두 false만 반환할 때 안전망)
            // - 단 안전망: 새 요약문이 직전 요약문과 완전히 동일하면 분리 거부 (LLM 헷갈림 방어)
            var prevSummaryText = _segments.Count > 0 ? (_segments[^1].SummaryPreview ?? string.Empty) : string.Empty;
            var summaryDuplicate = !string.IsNullOrWhiteSpace(summaryPreview) &&
                                    !string.IsNullOrWhiteSpace(prevSummaryText) &&
                                    summaryPreview.Trim().Equals(prevSummaryText.Trim(), StringComparison.OrdinalIgnoreCase);

            var prevKeywordList = _segments.Count > 0 ? _segments[^1].Keywords : new List<string>();
            var jaccardSimilarity = CalcJaccard(new HashSet<string>(prevKeywordList), new HashSet<string>(keywords));

            const double TimeForceSplitMinSec = 300.0; // 5분
            var prevDurationSec = _segments.Count > 0
                ? (_segments[^1].EndTime - _segments[^1].StartTime).TotalSeconds
                : 0.0;
            var timeForceSplit = prevDurationSec >= TimeForceSplitMinSec && jaccardSimilarity <= 0.2;

            var llmSplit = topicChanged && !summaryDuplicate;
            var shouldSplit = _segments.Count == 0 || llmSplit || timeForceSplit;

            _log.Info("[TopicExtractor] 판정: LLM_changed={C} dup={Dup} jaccard={J:F2} prevDur={D:F0}s timeForce={T} → 분리={Split}",
                topicChanged, summaryDuplicate, jaccardSimilarity, prevDurationSec, timeForceSplit, shouldSplit);

            if (shouldSplit)
            {
                // 인접 충돌 회피: 직전 세그먼트와 다른 팔레트 인덱스 선택 (라이트/다크 자동 선택)
                var palette = TopicSegment.GetPaletteForCurrentTheme();
                int paletteIdx = _nextSegmentId % palette.Length;
                if (_segments.Count > 0)
                {
                    var prevColorIdx = (_nextSegmentId - 1) % palette.Length;
                    if (paletteIdx == prevColorIdx)
                        paletteIdx = (paletteIdx + 1) % palette.Length;
                }

                // 시간 연속성 — 새 세그먼트 StartTime = 직전 세그먼트 EndTime (1~2초 미세 세그먼트 방지)
                // 첫 세그먼트는 0초부터 시작 (이전 세그먼트 없음)
                var continuousStart = _segments.Count > 0 ? _segments[^1].EndTime : TimeSpan.Zero;
                // EndTime은 buffer 마지막 발화 시각 — 단 너무 짧으면 최소 PeriodicTimer interval만큼 보장
                var minEnd = continuousStart + TimeSpan.FromSeconds(Math.Max(5, _settings.OaiRecording.ProcessingIntervalSeconds));
                var effectiveEnd = endTime > minEnd ? endTime : minEnd;

                var newSegment = new TopicSegment
                {
                    Id = _nextSegmentId++,
                    StartTime = continuousStart,
                    EndTime = effectiveEnd,
                    Keywords = keywords,
                    DisplayTitle = keywords.Count > 0 ? string.Join(" · ", keywords.Take(2)) : "(주제 없음)",
                    SummaryPreview = summaryPreview,
                    BackgroundColorHex = palette[paletteIdx]
                };

                _segments.Add(newSegment);
                _log.Info("[TopicExtractor] 새 세그먼트 #{Id}: {Title}", newSegment.Id, newSegment.DisplayTitle);
                TopicSegmentAdded?.Invoke(newSegment);
            }
            else
            {
                // 기존 마지막 세그먼트 갱신 — EndTime 연장 + 최신 핵심요약/키워드/타이틀로 덮어쓰기
                var last = _segments[^1];
                last.EndTime = endTime;
                if (!string.IsNullOrWhiteSpace(summaryPreview))
                    last.SummaryPreview = summaryPreview;
                if (keywords.Count > 0)
                {
                    last.Keywords = keywords;
                    last.DisplayTitle = string.Join(" · ", keywords.Take(2));
                }
                _log.Info("[TopicExtractor] 세그먼트 #{Id} 갱신 — title={Title}, summary={Summary}", last.Id, last.DisplayTitle, last.SummaryPreview);
                TopicSegmentUpdated?.Invoke(last);
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[TopicExtractor] 결과 파싱 실패");
        }
    }

    private async Task ConsolidateTopicsIfNeededAsync(CancellationToken ct)
    {
        var count = _segments.Count;

        if (count <= 10)
        {
            _log.Debug("[TopicExtractor] 그루핑 skip — 세그먼트 {Count}개 (10개 이하)", count);
            return;
        }

        _log.Info("[TopicExtractor] 그루핑 평가 시작 — 현재 세그먼트 {Count}개", count);

        // 세그먼트 목록을 JSON 직렬화하여 LLM에 전달
        var segInfos = _segments.Select(s => new
        {
            id = s.Id,
            title = s.DisplayTitle ?? string.Empty,
            summary = s.SummaryPreview ?? string.Empty,
            keywords = s.Keywords,
            startTime = s.StartTime.ToString(@"hh\:mm\:ss"),
            endTime = s.EndTime.ToString(@"hh\:mm\:ss")
        });
        var segJson = JsonSerializer.Serialize(segInfos);

        var prompt = $$"""
            다음은 현재 녹음 세션의 핵심주제 세그먼트 목록입니다.
            유사한 것끼리 병합하여 8~12개로 줄여주세요. 시간순 및 주제 연관성을 고려하세요.

            [세그먼트 목록]
            {{segJson}}

            지시사항:
            - 비슷한 주제를 가진 세그먼트들을 하나로 병합
            - 시간 순서를 유지 (연속된 세그먼트 우선 병합)
            - 목표: 8~12개의 대표 세그먼트

            반드시 다음 JSON 배열 형식으로만 응답하세요 (마크다운 코드블록 없이):
            [{"merged_ids":[0,2],"title":"병합된 제목","summary":"통합 핵심요약","keywords":["키워드1","키워드2"]}]
            """;

        try
        {
            var model = _settings.OaiRecording.KeywordExtractModel;
            var baseUrl = (_settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
            var url = baseUrl + "/chat/completions";

            // response_format json_object는 배열 직접 반환 불가 → wrapper 응답 처리
            // json_object 미사용 시 배열로 바로 오므로 배열/객체 양쪽 파싱 시도
            var reqJson = JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                max_completion_tokens = 1024
            });
            using var content = new StringContent(reqJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warn("[TopicExtractor] 그루핑 API 오류 {Status}", response.StatusCode);
                return;
            }

            var respJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // choices[0].message.content 추출
            string? contentStr = null;
            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0];
                if (msg.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    contentStr = contentProp.GetString();
                }
            }

            if (string.IsNullOrEmpty(contentStr))
            {
                _log.Warn("[TopicExtractor] 그루핑 응답 content 비어있음");
                return;
            }

            // JSON 배열 파싱 (직접 배열 또는 wrapper 객체 양쪽 시도)
            JsonElement mergeArray;
            using var innerDoc = JsonDocument.Parse(contentStr);
            var innerRoot = innerDoc.RootElement;

            if (innerRoot.ValueKind == JsonValueKind.Array)
            {
                mergeArray = innerRoot;
            }
            else if (innerRoot.ValueKind == JsonValueKind.Object)
            {
                // {"result": [...]} 또는 첫 번째 배열 값 사용
                JsonElement found = default;
                foreach (var prop in innerRoot.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        found = prop.Value;
                        break;
                    }
                }
                if (found.ValueKind != JsonValueKind.Array)
                {
                    _log.Warn("[TopicExtractor] 그루핑 응답 배열 없음, 기존 세그먼트 유지");
                    return;
                }
                mergeArray = found;
            }
            else
            {
                _log.Warn("[TopicExtractor] 그루핑 응답 형식 불명, 기존 세그먼트 유지");
                return;
            }

            // 병합 그룹으로 새 세그먼트 목록 구성
            var palette = TopicSegment.GetPaletteForCurrentTheme();
            var newSegments = new List<TopicSegment>();
            int newIdx = 0;

            foreach (var group in mergeArray.EnumerateArray())
            {
                var mergedIds = new List<int>();
                if (group.TryGetProperty("merged_ids", out var ids))
                {
                    foreach (var idEl in ids.EnumerateArray())
                        mergedIds.Add(idEl.GetInt32());
                }

                var sources = _segments.Where(s => mergedIds.Contains(s.Id)).ToList();
                if (sources.Count == 0) continue;

                var title = group.TryGetProperty("title", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
                var summary = group.TryGetProperty("summary", out var sm) ? (sm.GetString() ?? string.Empty) : string.Empty;
                var kws = new List<string>();
                if (group.TryGetProperty("keywords", out var kwArr))
                {
                    foreach (var kw in kwArr.EnumerateArray())
                    {
                        var s = kw.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) kws.Add(s);
                    }
                }

                newSegments.Add(new TopicSegment
                {
                    Id = newIdx,
                    StartTime = sources.Min(s => s.StartTime),
                    EndTime = sources.Max(s => s.EndTime),
                    DisplayTitle = string.IsNullOrWhiteSpace(title) ? (kws.Count > 0 ? string.Join(" · ", kws.Take(2)) : "(주제 없음)") : title,
                    SummaryPreview = summary,
                    Keywords = kws,
                    BackgroundColorHex = palette[newIdx % palette.Length]
                });
                newIdx++;
            }

            if (newSegments.Count == 0)
            {
                _log.Warn("[TopicExtractor] 그루핑 결과 세그먼트 0개 — 기존 유지");
                return;
            }

            var beforeCount = _segments.Count;
            _segments.Clear();
            _segments.AddRange(newSegments);
            _nextSegmentId = newSegments.Max(s => s.Id) + 1;

            _log.Info("[TopicExtractor] 그루핑 완료: {Before}개 → {After}개", beforeCount, _segments.Count);
            TopicSegmentsConsolidated?.Invoke(_segments.ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[TopicExtractor] 그루핑 처리 실패 — 기존 세그먼트 유지");
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
