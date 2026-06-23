// 설정 주기 PeriodicTimer 기반 누적 요약 서비스 (원문 STT 미전송, 압축 갱신 모드 포함)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Helpers;
using mAIx.Models;
using mAIx.Models.Settings;
using mAIx.Services.AI.Testing;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 누적 요약 서비스 인터페이스
/// </summary>
public interface ICumulativeSummaryService : IDisposable
{
    /// <summary>
    /// 누적 요약 갱신 이벤트 (새 누적 요약 텍스트)
    /// </summary>
    event Action<string>? CumulativeSummaryUpdated;

    /// <summary>
    /// 서비스 시작
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 서비스 중지
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 종료 시 최종 요약 1회 생성 (전체 누적요약 + 마지막 1분요약 입력)
    /// </summary>
    Task<string> FinalSummarizeAsync();
}

/// <summary>
/// AppSettingsManager.OaiRecording.CumulativeSummaryIntervalMinutes 주기로
/// 1분 요약 엔트리를 읽어 누적 요약을 갱신하는 서비스 (원문 STT 절대 미사용)
/// </summary>
public sealed class CumulativeSummaryService : ICumulativeSummaryService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;
    private readonly IMinuteSummaryService _minuteSummaryService;
    private readonly HttpClient _httpClient = new();

    private CancellationTokenSource? _cts;
    private Task? _timerTask;
    private bool _running;
    private bool _disposed;

    // 현재 누적 요약 텍스트
    private string _cumulativeSummary = string.Empty;
    private readonly object _summaryLock = new();

    // 한국어 토큰 추정: 1자 ≈ 1.5 토큰
    private const double KoreanCharsPerToken = 1.5;
    private const int MaxCumulativeTokens = 2000;

    /// <inheritdoc/>
    public event Action<string>? CumulativeSummaryUpdated;

    public CumulativeSummaryService(AppSettingsManager settings, IMinuteSummaryService minuteSummaryService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _minuteSummaryService = minuteSummaryService ?? throw new ArgumentNullException(nameof(minuteSummaryService));
        ConfigureHttpClient();
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
            _log.Warn("[CumulativeSummary] 이미 실행 중");
            return Task.CompletedTask;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = true;

        var intervalMinutes = _settings.OaiRecording.CumulativeSummaryIntervalMinutes;
        _timerTask = RunTimerLoopAsync(intervalMinutes, _cts.Token);
        _log.Info("[CumulativeSummary] 시작 (interval={M}분, model={Model})", intervalMinutes, _settings.OaiRecording.CumulativeSummaryModel);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        _log.Info("[CumulativeSummary] 중지");
        _running = false;
        _cts?.Cancel();
        if (_timerTask != null)
        {
            try { await _timerTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
        }
    }

    private async Task RunTimerLoopAsync(int intervalMinutes, CancellationToken ct)
    {
        // DebugTimerScale: 1.0=정상, 0.1=10배 빠름 — 환경변수 MAIX_DEBUG_TIMER_SCALE 우선
        var envScale = Environment.GetEnvironmentVariable("MAIX_DEBUG_TIMER_SCALE");
        var timerScale = double.TryParse(envScale, out var parsed) ? parsed : _settings.OaiRecording.DebugTimerScale;
        var scaledMinutes = Math.Max(1.0 / 60.0, intervalMinutes * timerScale); // 최소 1초
        _log.Debug("[CumulativeSummary] PeriodicTimer 주기={Min:F2}분 (scale={Scale})", scaledMinutes, timerScale);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(scaledMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await UpdateCumulativeSummaryAsync(intervalMinutes, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[CumulativeSummary] 타이머 루프 오류");
        }
    }

    private async Task UpdateCumulativeSummaryAsync(int intervalMinutes, CancellationToken ct)
    {
        try
        {
            // Mock 분기 — EnableMock=true 시 실호출 없이 mock 누적 요약 반환
            if (MockOpenAiResponseInjector.TryHandleCumulativeSummary(out var mockSummary))
            {
                lock (_summaryLock) { _cumulativeSummary = mockSummary; }
                CumulativeSummaryUpdated?.Invoke(mockSummary);
                _log.Info("[CumulativeSummary] Mock 누적 요약 완료: {Text}", mockSummary);
                return;
            }

            // 직전 N분간 1분 요약 엔트리 수집 (원문 STT 미사용)
            var allEntries = await _minuteSummaryService.GetAllMinuteSummariesAsync().ConfigureAwait(false);
            var cutoff = DateTime.Now - TimeSpan.FromMinutes(intervalMinutes);
            var recentEntries = allEntries.Where(e => e.CreatedAt >= cutoff).ToList();

            if (recentEntries.Count == 0 && string.IsNullOrEmpty(_cumulativeSummary))
            {
                _log.Debug("[CumulativeSummary] 새 1분 요약 없음 — 건너뜀");
                return;
            }

            string prevCumulative;
            lock (_summaryLock)
            {
                prevCumulative = _cumulativeSummary;
            }

            // 토큰 추정: 한국어 1자 ≈ 1.5 토큰
            var estimatedTokens = (int)(prevCumulative.Length * KoreanCharsPerToken);
            _log.Debug("[CumulativeSummary] 누적요약 추정 토큰={Tokens}", estimatedTokens);

            var model = _settings.OaiRecording.CumulativeSummaryModel;
            var newSummary = await CallCumulativeSummaryApiAsync(prevCumulative, recentEntries, estimatedTokens, model, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(newSummary)) return;

            lock (_summaryLock)
            {
                _cumulativeSummary = newSummary;
            }

            _log.Info("[CumulativeSummary] 누적 요약 갱신: {Preview}",
                newSummary.Length > 100 ? newSummary[..100] : newSummary);

            CumulativeSummaryUpdated?.Invoke(newSummary);
        }
        catch (OperationCanceledException)
        {
            // 정상 취소
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[CumulativeSummary] 갱신 실패");
        }
    }

    private async Task<string> CallCumulativeSummaryApiAsync(
        string prevCumulative,
        List<MinuteSummaryEntry> recentEntries,
        int estimatedTokens,
        string model,
        CancellationToken ct)
    {
        var baseUrl = (_settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        var url = baseUrl + "/chat/completions";

        string prompt;

        if (estimatedTokens > MaxCumulativeTokens && !string.IsNullOrEmpty(prevCumulative))
        {
            // 압축 갱신 모드: 이전 누적요약 먼저 압축 후 새 N분 통합
            var recentSummaries = string.Join("\n\n", recentEntries.Select(e =>
                $"[{TimeSpanFormatter.FormatTimeSpan(e.StartTime)}~{TimeSpanFormatter.FormatTimeSpan(e.EndTime)}] {e.SummaryText}"));

            prompt = $"""
                다음은 이전까지의 누적 요약과 최근 추가된 1분 요약들입니다.

                [이전 누적 요약]
                {prevCumulative}

                [최근 1분 요약들]
                {recentSummaries}

                위 내용을 바탕으로 전체 내용을 압축하여 핵심만 남긴 통합 누적 요약을 작성해 주세요.
                요약은 명확하고 간결하게 작성하고, 원문 STT 텍스트가 아닌 요약 내용만 사용하세요.
                """;

            _log.Debug("[CumulativeSummary] 압축 갱신 모드 (토큰 추정 {T} > {Max})", estimatedTokens, MaxCumulativeTokens);
        }
        else
        {
            var recentSummaries = string.Join("\n\n", recentEntries.Select(e =>
                $"[{TimeSpanFormatter.FormatTimeSpan(e.StartTime)}~{TimeSpanFormatter.FormatTimeSpan(e.EndTime)}] {e.SummaryText}"));

            prompt = $"""
                다음은 현재까지의 누적 요약과 최근 추가된 1분 요약들입니다.

                [현재 누적 요약]
                {(string.IsNullOrEmpty(prevCumulative) ? "(없음)" : prevCumulative)}

                [최근 1분 요약들]
                {(recentEntries.Count == 0 ? "(없음)" : recentSummaries)}

                위 내용을 통합하여 전체 회의/강의/대화의 누적 요약을 업데이트해 주세요.
                원문 STT 텍스트가 아닌 요약 내용만 사용하세요.
                """;
        }

        var requestBody = new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            max_completion_tokens = 512
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _log.Warn("[CumulativeSummary] API 오류 {Status}", response.StatusCode);
            return string.Empty;
        }

        var respJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ExtractContentText(respJson);
    }

    /// <inheritdoc/>
    public async Task<string> FinalSummarizeAsync()
    {
        _log.Info("[CumulativeSummary] 최종 요약 생성 시작");

        // Mock 분기 — EnableMock=true 시 실호출 없이 mock 최종 요약 반환
        if (MockOpenAiResponseInjector.TryHandleFinalSummary(out var mockFinal))
        {
            _log.Info("[CumulativeSummary] Mock 최종 요약 완료: {Text}", mockFinal);
            return mockFinal;
        }

        try
        {
            var allEntries = await _minuteSummaryService.GetAllMinuteSummariesAsync().ConfigureAwait(false);
            var lastEntry = allEntries.Count > 0 ? allEntries[^1] : null;

            string prevCumulative;
            lock (_summaryLock)
            {
                prevCumulative = _cumulativeSummary;
            }

            var lastMinuteSummary = lastEntry != null
                ? $"[{TimeSpanFormatter.FormatTimeSpan(lastEntry.StartTime)}~{TimeSpanFormatter.FormatTimeSpan(lastEntry.EndTime)}] {lastEntry.SummaryText}"
                : "(없음)";

            var prompt = $"""
                다음은 전체 녹음의 누적 요약과 마지막 1분 요약입니다.

                [누적 요약]
                {(string.IsNullOrEmpty(prevCumulative) ? "(없음)" : prevCumulative)}

                [마지막 1분 요약]
                {lastMinuteSummary}

                위 내용을 바탕으로 전체 녹음의 최종 종합 요약을 작성해 주세요.
                핵심 주제, 결론, 주요 논의 사항을 포함하여 명확하게 정리해 주세요.
                """;

            var model = _settings.OaiRecording.FinalSummaryModel;
            var baseUrl = (_settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
            var url = baseUrl + "/chat/completions";

            var requestBody = new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                max_completion_tokens = 1024
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warn("[CumulativeSummary] 최종 요약 API 오류 {Status}", response.StatusCode);
                return string.Empty;
            }

            var respJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var finalText = ExtractContentText(respJson);
            _log.Info("[CumulativeSummary] 최종 요약 완료: {Preview}", finalText.Length > 100 ? finalText[..100] : finalText);
            return finalText;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[CumulativeSummary] 최종 요약 실패");
            return string.Empty;
        }
    }

    private static string ExtractContentText(string responseJson)
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
