// 모드 A 전용 - 1분 요약 텍스트의 감성을 0~100 점수로 분석
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 1분 요약 텍스트에서 감성 점수를 분석하는 서비스 인터페이스.
/// </summary>
public interface ISentimentAnalysisService : IDisposable
{
    /// <summary>
    /// 텍스트의 감성을 분석하여 SentimentResult를 반환.
    /// 분석 실패 시 null 반환 (entry.Sentiment=null → UI 회색).
    /// </summary>
    Task<SentimentResult?> AnalyzeAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// gpt-4o-mini를 사용하여 한국어 텍스트의 감성을 0~100 점수로 분석하는 서비스.
/// 저비용(gpt-4o-mini) API 호출로 분당 추가 비용 최소화.
/// </summary>
public sealed class SentimentAnalysisService : ISentimentAnalysisService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;
    private readonly HttpClient _httpClient;

    // 동시 호출 보호 — 클래스 필드로 보유 후 Dispose에서 해제 (L-376)
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private bool _disposed;

    private const string SentimentModel = "gpt-4o-mini";
    private const string ApiEndpoint = "https://api.openai.com/v1/chat/completions";

    private static readonly string SystemPrompt =
        "당신은 한국어 텍스트 감성 분석 전문가입니다. 다음 한국어 텍스트의 감성을 0~100 점수로 평가하라. " +
        "0=매우 부정, 50=중립, 100=매우 긍정. 긍정적 표현(성공, 달성, 좋음, 기쁨 등)은 높은 점수, " +
        "부정적 표현(실패, 문제, 어려움, 걱정 등)은 낮은 점수를 부여하라. " +
        "반드시 다음 JSON 형식만 응답하라: {\"score\": int}";

    public SentimentAnalysisService(AppSettingsManager settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <inheritdoc/>
    public async Task<SentimentResult?> AnalyzeAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _log.Debug("감성 분석 생략 — 빈 텍스트");
            return null;
        }

        // 외부 try-catch — async Task 전체 보호 (L-377)
        try
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await AnalyzeInternalAsync(text, ct).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _log.Debug("감성 분석 취소됨");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "감성 분석 실패 — 중립 결과로 폴백");
            return null;
        }
    }

    private async Task<SentimentResult?> AnalyzeInternalAsync(string text, CancellationToken ct)
    {
        var requestBody = new
        {
            model = SentimentModel,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"다음 텍스트의 감성을 분석하라:\n\n{text}" },
            },
            response_format = new { type = "json_object" },
            max_tokens = 20,
            temperature = 0.1,
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiEndpoint, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(responseJson);

        var messageContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        using var resultDoc = JsonDocument.Parse(messageContent);
        if (!resultDoc.RootElement.TryGetProperty("score", out var scoreElem))
        {
            _log.Warn("감성 API 응답에 score 필드 없음: {0}", messageContent);
            return null;
        }

        var score = scoreElem.GetInt32();
        // 범위 클램핑 (0~100)
        score = Math.Clamp(score, 0, 100);

        var label = score >= 70 ? "긍정" : score >= 30 ? "중립" : "부정";

        _log.Debug("감성 분석 완료 — Score={0}, Label={1}", score, label);

        return new SentimentResult
        {
            Score = score,
            Label = label,
            AnalyzedAt = DateTime.Now,
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
        _httpClient.Dispose();
    }
}
