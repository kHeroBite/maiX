using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
using mAIx.Models;
using mAIx.Services.AI;
using mAIx.Services.Storage;

namespace mAIx.Services.Analysis;

/// <summary>
/// 이메일 분석기 - 7단계 AI 분석 파이프라인 실행
/// Channel&lt;T&gt; + SemaphoreSlim으로 비동기 병렬 처리
/// </summary>
public class EmailAnalyzer : IDisposable
{
    private readonly AIService _aiService;
    private readonly PromptService _promptService;
    private readonly PriorityCalculator _priorityCalculator;
    private readonly ContractExtractor _contractExtractor;
    private readonly TodoExtractor _todoExtractor;
    private readonly ILogger _logger;

    // 동시 분석 제한 (최대 5개 동시 처리)
    private readonly SemaphoreSlim _semaphore;
    private const int MaxConcurrency = 5;
    private const int MaxRetryCount = 3;
    private const int RetryDelayMs = 1000;

    // 분석 단계별 프롬프트 키
    private static class PromptKeys
    {
        public const string SummaryOneline = "summary_oneline";
        public const string SummaryDetail = "summary_detail";
        public const string Deadline = "deadline";
        public const string Importance = "importance";
        public const string Urgency = "urgency";
        public const string ContractInfo = "contract_info";
        public const string Todo = "todo";
    }

    public EmailAnalyzer(
        AIService aiService,
        PromptService promptService,
        PriorityCalculator priorityCalculator,
        ContractExtractor contractExtractor,
        TodoExtractor todoExtractor)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        _priorityCalculator = priorityCalculator ?? throw new ArgumentNullException(nameof(priorityCalculator));
        _contractExtractor = contractExtractor ?? throw new ArgumentNullException(nameof(contractExtractor));
        _todoExtractor = todoExtractor ?? throw new ArgumentNullException(nameof(todoExtractor));
        _logger = Log.ForContext<EmailAnalyzer>();
        _semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    }

    /// <summary>
    /// 단일 이메일 분석
    /// </summary>
    /// <param name="email">분석할 이메일</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>분석 결과</returns>
    public async Task<EmailAnalysisResult> AnalyzeEmailAsync(Email email, CancellationToken ct = default)
    {
        var result = new EmailAnalysisResult
        {
            EmailId = email.Id,
            AnalysisStartedAt = DateTime.UtcNow,
            Provider = _aiService.CurrentProviderName
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);

            _logger.Information("이메일 분석 시작: {Id} - {Subject}", email.Id, email.Subject);

            // 이메일 데이터 준비
            var emailData = PrepareEmailData(email);

            // 7단계 분석 파이프라인 실행
            await ExecutePipelineAsync(emailData, result, ct).ConfigureAwait(false);

            // 최종 우선순위 계산
            CalculateFinalPriority(email, result);

            result.IsSuccess = true;
            _logger.Information("이메일 분석 완료: {Id} - 우선순위 {Score}점 ({Level})",
                email.Id, result.PriorityScore, result.PriorityLevel);
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "분석이 취소되었습니다.";
            _logger.Warning("이메일 분석 취소: {Id}", email.Id);
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            _logger.Error(ex, "이메일 분석 실패: {Id}", email.Id);
        }
        finally
        {
            _semaphore.Release();
            stopwatch.Stop();
            result.AnalysisCompletedAt = DateTime.UtcNow;
            result.AnalysisDurationMs = stopwatch.ElapsedMilliseconds;
        }

        return result;
    }

    /// <summary>
    /// 여러 이메일 일괄 분석 (Channel 기반 비동기 처리)
    /// </summary>
    /// <param name="emails">분석할 이메일 목록</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>분석 결과 비동기 스트림</returns>
    public async IAsyncEnumerable<EmailAnalysisResult> AnalyzeEmailsAsync(
        IEnumerable<Email> emails,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Channel 생성 (무제한 버퍼)
        var channel = Channel.CreateUnbounded<EmailAnalysisResult>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });

        var writer = channel.Writer;
        var reader = channel.Reader;

        // Producer: 이메일 분석 작업 시작
        var producerTask = Task.Run(async () =>
        {
            var tasks = new List<Task>();

            foreach (var email in emails)
            {
                if (ct.IsCancellationRequested)
                    break;

                var task = Task.Run(async () =>
                {
                    var result = await AnalyzeEmailAsync(email, ct).ConfigureAwait(false);
                    await writer.WriteAsync(result, ct).ConfigureAwait(false);
                }, ct);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            writer.Complete();
        }, ct);

        // Consumer: 분석 결과 반환
        await foreach (var result in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return result;
        }

        await producerTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 이메일 데이터를 분석용 딕셔너리로 변환
    /// </summary>
    private Dictionary<string, string> PrepareEmailData(Email email)
    {
        var data = new Dictionary<string, string>
        {
            ["subject"] = email.Subject ?? string.Empty,
            ["body"] = email.Body ?? string.Empty,
            ["from"] = email.From ?? string.Empty,
            ["to"] = email.To ?? string.Empty,
            ["cc"] = email.Cc ?? string.Empty,
            ["received_date"] = email.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
            ["has_attachments"] = email.HasAttachments.ToString().ToLower(),
            ["importance"] = email.Importance ?? "normal"
        };

        // 첨부파일 MarkdownContent 결합 (최대 5000자)
        if (email.Attachments != null && email.Attachments.Count > 0)
        {
            var attachmentTexts = new System.Text.StringBuilder();
            foreach (var att in email.Attachments)
            {
                if (!string.IsNullOrWhiteSpace(att.MarkdownContent))
                {
                    attachmentTexts.Append($"[{att.Name}]\n");
                    attachmentTexts.Append(att.MarkdownContent);
                    attachmentTexts.Append("\n\n");
                }
            }
            var combined = attachmentTexts.ToString().Trim();
            if (!string.IsNullOrEmpty(combined))
            {
                data["attachments_text"] = combined.Length > 5000
                    ? combined[..5000]
                    : combined;
            }
        }

        return data;
    }

    /// <summary>
    /// 7단계 분석 파이프라인 실행
    /// </summary>
    private async Task ExecutePipelineAsync(
        Dictionary<string, string> emailData,
        EmailAnalysisResult result,
        CancellationToken ct)
    {
        // 1단계: 한줄 요약
        await ExecuteStepWithRetryAsync(
            "1_summary_oneline",
            async () =>
            {
                var response = await ExecutePromptAsync(PromptKeys.SummaryOneline, emailData, ct).ConfigureAwait(false);
                result.SummaryOneline = response?.Trim();
            },
            result,
            ct).ConfigureAwait(false);

        // 2단계: 상세 요약
        await ExecuteStepWithRetryAsync(
            "2_summary_detail",
            async () =>
            {
                var response = await ExecutePromptAsync(PromptKeys.SummaryDetail, emailData, ct).ConfigureAwait(false);
                result.SummaryDetail = response?.Trim();
            },
            result,
            ct).ConfigureAwait(false);

        // 3단계: 마감일 추출
        await ExecuteStepWithRetryAsync(
            "3_deadline",
            async () =>
            {
                var response = await ExecutePromptAsync(PromptKeys.Deadline, emailData, ct).ConfigureAwait(false);
                ParseDeadline(response, result);
            },
            result,
            ct).ConfigureAwait(false);

        // 4단계: 중요도 판단
        await ExecuteStepWithRetryAsync(
            "4_importance",
            async () =>
            {
                var response = await ExecutePromptAsync(PromptKeys.Importance, emailData, ct).ConfigureAwait(false);
                ParseImportance(response, result);
            },
            result,
            ct).ConfigureAwait(false);

        // 5단계: 긴급도 판단
        await ExecuteStepWithRetryAsync(
            "5_urgency",
            async () =>
            {
                var response = await ExecutePromptAsync(PromptKeys.Urgency, emailData, ct).ConfigureAwait(false);
                ParseUrgency(response, result);
            },
            result,
            ct).ConfigureAwait(false);

        // 6단계: 계약정보 추출
        await ExecuteStepWithRetryAsync(
            "6_contract_info",
            async () =>
            {
                var response = await ExecutePromptAsync(PromptKeys.ContractInfo, emailData, ct).ConfigureAwait(false);
                result.ContractInfo = _contractExtractor.Parse(response);
                result.HasContractInfo = result.ContractInfo != null;
            },
            result,
            ct).ConfigureAwait(false);

        // 7단계: 할일 추출
        await ExecuteStepWithRetryAsync(
            "7_todo",
            async () =>
            {
                var response = await ExecutePromptAsync(PromptKeys.Todo, emailData, ct).ConfigureAwait(false);
                result.Todos = _todoExtractor.Parse(response);
            },
            result,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 재시도 로직이 포함된 단계 실행
    /// </summary>
    private async Task ExecuteStepWithRetryAsync(
        string stepName,
        Func<Task> action,
        EmailAnalysisResult result,
        CancellationToken ct)
    {
        var stepwatch = Stopwatch.StartNew();
        var retryCount = 0;

        while (retryCount < MaxRetryCount)
        {
            try
            {
                await action().ConfigureAwait(false);
                stepwatch.Stop();
                result.StepDurations[stepName] = stepwatch.ElapsedMilliseconds;
                _logger.Debug("분석 단계 완료: {Step} ({Time}ms)", stepName, stepwatch.ElapsedMilliseconds);
                return;
            }
            catch (Exception ex) when (retryCount < MaxRetryCount - 1)
            {
                retryCount++;
                _logger.Warning(ex, "분석 단계 재시도: {Step} ({Retry}/{Max})",
                    stepName, retryCount, MaxRetryCount);
                await Task.Delay(RetryDelayMs * retryCount, ct).ConfigureAwait(false);
            }
        }

        // 최대 재시도 초과
        _logger.Error("분석 단계 최대 재시도 초과: {Step}", stepName);
        stepwatch.Stop();
        result.StepDurations[stepName] = stepwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// 프롬프트 실행
    /// </summary>
    private async Task<string?> ExecutePromptAsync(
        string promptKey,
        Dictionary<string, string> variables,
        CancellationToken ct)
    {
        var renderedPrompt = await _promptService.RenderPromptAsync(promptKey, variables).ConfigureAwait(false);
        if (string.IsNullOrEmpty(renderedPrompt))
        {
            _logger.Warning("프롬프트를 찾을 수 없음: {Key}", promptKey);
            return null;
        }

        return await _aiService.CompleteAsync(renderedPrompt, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 마감일 파싱
    /// </summary>
    private void ParseDeadline(string? response, EmailAnalysisResult result)
    {
        if (string.IsNullOrWhiteSpace(response))
            return;

        try
        {
            var json = JsonDocument.Parse(response);
            var root = json.RootElement;

            if (root.TryGetProperty("deadline", out var deadlineEl) &&
                deadlineEl.ValueKind != JsonValueKind.Null)
            {
                if (DateTime.TryParse(deadlineEl.GetString(), out var deadline))
                {
                    result.Deadline = deadline;
                }
            }

            if (root.TryGetProperty("deadline_text", out var textEl))
            {
                result.DeadlineText = textEl.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "마감일 JSON 파싱 실패: {Response}", response);

            // JSON 파싱 실패 시 날짜 직접 추출 시도
            if (DateTime.TryParse(response?.Trim(), out var deadline))
            {
                result.Deadline = deadline;
                result.DeadlineText = response?.Trim();
            }
        }
    }

    /// <summary>
    /// 중요도 파싱
    /// </summary>
    private void ParseImportance(string? response, EmailAnalysisResult result)
    {
        if (string.IsNullOrWhiteSpace(response))
            return;

        try
        {
            var json = JsonDocument.Parse(response);
            var root = json.RootElement;

            if (root.TryGetProperty("level", out var levelEl))
            {
                result.ImportanceLevel = levelEl.GetString();
            }

            if (root.TryGetProperty("score", out var scoreEl))
            {
                result.ImportanceScore = scoreEl.GetInt32();
            }

            if (root.TryGetProperty("reason", out var reasonEl))
            {
                result.ImportanceReason = reasonEl.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "중요도 JSON 파싱 실패: {Response}", response);
            result.ImportanceLevel = "medium";
            result.ImportanceScore = 50;
        }
    }

    /// <summary>
    /// 긴급도 파싱
    /// </summary>
    private void ParseUrgency(string? response, EmailAnalysisResult result)
    {
        if (string.IsNullOrWhiteSpace(response))
            return;

        try
        {
            var json = JsonDocument.Parse(response);
            var root = json.RootElement;

            if (root.TryGetProperty("level", out var levelEl))
            {
                result.UrgencyLevel = levelEl.GetString();
            }

            if (root.TryGetProperty("score", out var scoreEl))
            {
                result.UrgencyScore = scoreEl.GetInt32();
            }

            if (root.TryGetProperty("reason", out var reasonEl))
            {
                result.UrgencyReason = reasonEl.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "긴급도 JSON 파싱 실패: {Response}", response);
            result.UrgencyLevel = "normal";
            result.UrgencyScore = 50;
        }
    }

    /// <summary>
    /// 최종 우선순위 계산
    /// </summary>
    private void CalculateFinalPriority(Email email, EmailAnalysisResult result)
    {
        var priorityResult = _priorityCalculator.Calculate(
            importanceScore: result.ImportanceScore,
            urgencyScore: result.UrgencyScore,
            senderEmail: email.From,
            keywords: result.Keywords,
            hasDeadline: result.Deadline.HasValue,
            deadlineDays: result.Deadline.HasValue
                ? (result.Deadline.Value - DateTime.UtcNow).Days
                : (int?)null);

        result.PriorityScore = priorityResult.Score;
        result.PriorityLevel = priorityResult.Level;
    }

    /// <summary>
    /// IDisposable 구현 — SemaphoreSlim 자원 해제 (L-376)
    /// </summary>
    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}
