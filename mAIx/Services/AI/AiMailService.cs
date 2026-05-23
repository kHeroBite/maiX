using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using mAIx.Models;
using mAIx.Services.AI;
using mAIx.Utils;

namespace mAIx.Services.AI;

/// <summary>
/// AI 메일 생성 서비스 — 답장 초안, 스레드 요약, 일일 브리핑, 회의 브리핑 생성
/// EmailAnalyzer가 분석(분류/우선순위)을 담당하고, 이 서비스는 생성(텍스트 작성)을 담당
/// </summary>
public class AiMailService
{
    private readonly AIService _aiService;
    private readonly ILogger _logger;

    public AiMailService(AIService aiService)
    {
        _aiService = aiService;
        _logger = Log.ForContext<AiMailService>();
    }

    /// <summary>
    /// AI 답장 초안 생성
    /// </summary>
    /// <param name="email">원본 메일</param>
    /// <param name="tone">답장 톤: "정중" (기본) / "캐주얼" / "비즈니스"</param>
    /// <returns>생성된 답장 초안 텍스트</returns>
    public async Task<string> GenerateReplyDraftAsync(Email email, string tone = "정중", CancellationToken ct = default)
    {
        Log4.Debug($"[AiMailService] 답장 초안 생성 시작 - EmailId={email.Id}, Tone={tone}");

        var toneInstruction = tone switch
        {
            "캐주얼" => "친근하고 편안한 어조로 작성하세요.",
            "비즈니스" => "격식 있는 비즈니스 어조로 작성하세요.",
            _ => "정중하고 프로페셔널한 어조로 작성하세요."
        };

        var prompt = new StringBuilder();
        prompt.AppendLine($"[시스템] 당신은 이메일 답장 초안을 작성하는 AI 비서입니다. {toneInstruction}");
        prompt.AppendLine();
        prompt.AppendLine("[원본 메일 정보]");
        prompt.AppendLine($"발신자: {email.From}");
        prompt.AppendLine($"제목: {email.Subject}");
        prompt.AppendLine($"수신 일시: {email.ReceivedDateTime:yyyy-MM-dd HH:mm}");
        prompt.AppendLine();
        prompt.AppendLine("[원본 메일 본문]");
        prompt.AppendLine(TruncateBody(email.Body, 2000));
        prompt.AppendLine();
        prompt.AppendLine("[지시사항]");
        prompt.AppendLine("위 메일에 대한 답장 초안을 작성하세요.");
        prompt.AppendLine("- 인사말로 시작하고, 핵심 답변을 제공하며, 적절한 마무리로 끝내세요.");
        prompt.AppendLine("- HTML 태그 없이 순수 텍스트로 작성하세요.");
        prompt.AppendLine("- 답장 본문만 출력하세요 (제목, 메타데이터 제외).");

        try
        {
            var result = await _aiService.CompleteAsync(prompt.ToString(), ct).ConfigureAwait(false);
            Log4.Debug($"[AiMailService] 답장 초안 생성 완료 - EmailId={email.Id}, 길이={result.Length}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "답장 초안 생성 실패: EmailId={Id}", email.Id);
            Log4.Error($"[AiMailService] 답장 초안 생성 실패: EmailId={email.Id}, {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 메일 스레드(대화) AI 요약 생성
    /// </summary>
    /// <param name="threadEmails">같은 ConversationId의 메일 목록 (시간순 정렬 권장)</param>
    /// <returns>스레드 요약 텍스트</returns>
    public async Task<string> GenerateThreadSummaryAsync(List<Email> threadEmails, CancellationToken ct = default)
    {
        Log4.Debug($"[AiMailService] 스레드 요약 생성 시작 - 메일 수={threadEmails.Count}");

        var prompt = new StringBuilder();
        prompt.AppendLine("[시스템] 당신은 이메일 대화 스레드를 요약하는 AI 비서입니다.");
        prompt.AppendLine();
        prompt.AppendLine("[대화 스레드]");

        foreach (var email in threadEmails.Take(10))  // 최대 10개
        {
            prompt.AppendLine($"--- {email.ReceivedDateTime:MM-dd HH:mm} | {email.From} ---");
            prompt.AppendLine(TruncateBody(email.Body, 500));
            prompt.AppendLine();
        }

        prompt.AppendLine("[지시사항]");
        prompt.AppendLine("위 이메일 대화 스레드를 3~5줄로 요약하세요.");
        prompt.AppendLine("- 주요 논점, 결정 사항, 다음 액션을 포함하세요.");
        prompt.AppendLine("- 한국어로 작성하세요.");

        try
        {
            var result = await _aiService.CompleteAsync(prompt.ToString(), ct).ConfigureAwait(false);
            Log4.Debug($"[AiMailService] 스레드 요약 완료 - 메일 수={threadEmails.Count}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "스레드 요약 실패: Count={Count}", threadEmails.Count);
            Log4.Error($"[AiMailService] 스레드 요약 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 오늘의 일일 메일 브리핑 생성
    /// </summary>
    /// <param name="todayEmails">오늘 수신된 메일 목록</param>
    /// <returns>브리핑 텍스트</returns>
    public async Task<string> GenerateDailyBriefingAsync(List<Email> todayEmails, CancellationToken ct = default)
    {
        Log4.Debug($"[AiMailService] 일일 브리핑 생성 시작 - 메일 수={todayEmails.Count}");

        var prompt = new StringBuilder();
        prompt.AppendLine("[시스템] 당신은 비서 AI입니다. 오늘의 이메일을 브리핑해드립니다.");
        prompt.AppendLine();
        prompt.AppendLine($"[오늘 수신 메일 목록] (총 {todayEmails.Count}건)");

        foreach (var email in todayEmails.Take(20))  // 최대 20개
        {
            var priority = email.AiPriority > 0 ? $"[우선순위:{email.AiPriority}]" : "";
            var category = !string.IsNullOrEmpty(email.AiCategory) ? $"[{email.AiCategory}]" : "";
            prompt.AppendLine($"- {priority}{category} {email.From}: {email.Subject}");
            if (!string.IsNullOrEmpty(email.AiSummaryBrief))
                prompt.AppendLine($"  → {email.AiSummaryBrief}");
        }

        prompt.AppendLine();
        prompt.AppendLine("[지시사항]");
        prompt.AppendLine("위 메일들을 바탕으로 오늘의 이메일 브리핑을 작성하세요.");
        prompt.AppendLine("- 긴급/중요 메일을 먼저 언급하세요.");
        prompt.AppendLine("- 오늘 처리해야 할 주요 액션 아이템을 정리하세요.");
        prompt.AppendLine("- 전체 분량은 10줄 이내로 간결하게 작성하세요.");
        prompt.AppendLine("- 한국어로 작성하세요.");

        try
        {
            var result = await _aiService.CompleteAsync(prompt.ToString(), ct).ConfigureAwait(false);
            Log4.Debug($"[AiMailService] 일일 브리핑 생성 완료");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일일 브리핑 생성 실패: Count={Count}", todayEmails.Count);
            Log4.Error($"[AiMailService] 일일 브리핑 생성 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 회의 브리핑 생성 (관련 메일 기반)
    /// </summary>
    /// <param name="meetingSubject">회의 주제 또는 제목</param>
    /// <param name="relatedEmails">회의 관련 메일 목록</param>
    /// <returns>회의 브리핑 텍스트</returns>
    public async Task<string> GenerateMeetingBriefingAsync(string meetingSubject, List<Email> relatedEmails, CancellationToken ct = default)
    {
        Log4.Debug($"[AiMailService] 회의 브리핑 생성 시작 - Subject={meetingSubject}, 관련메일={relatedEmails.Count}건");

        var prompt = new StringBuilder();
        prompt.AppendLine("[시스템] 당신은 회의 준비를 도와주는 AI 비서입니다.");
        prompt.AppendLine();
        prompt.AppendLine($"[회의 주제] {meetingSubject}");
        prompt.AppendLine();

        if (relatedEmails.Any())
        {
            prompt.AppendLine("[관련 이메일 내용]");
            foreach (var email in relatedEmails.Take(5))  // 최대 5개
            {
                prompt.AppendLine($"--- {email.ReceivedDateTime:MM-dd} | {email.From}: {email.Subject} ---");
                prompt.AppendLine(TruncateBody(email.Body, 800));
                prompt.AppendLine();
            }
        }

        prompt.AppendLine("[지시사항]");
        prompt.AppendLine("위 정보를 바탕으로 회의 브리핑을 작성하세요. 다음 항목을 포함하세요:");
        prompt.AppendLine("1. 회의 배경 및 목적 (2~3줄)");
        prompt.AppendLine("2. 주요 논의 사항 (불릿 포인트)");
        prompt.AppendLine("3. 사전 검토 필요 사항");
        prompt.AppendLine("4. 예상 결정 사항");
        prompt.AppendLine("- 한국어로 작성하세요.");

        try
        {
            var result = await _aiService.CompleteAsync(prompt.ToString(), ct).ConfigureAwait(false);
            Log4.Debug($"[AiMailService] 회의 브리핑 생성 완료 - Subject={meetingSubject}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "회의 브리핑 생성 실패: Subject={Subject}", meetingSubject);
            Log4.Error($"[AiMailService] 회의 브리핑 생성 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 메일 본문에서 HTML 태그 제거 후 지정 길이로 자르기
    /// </summary>
    private static string TruncateBody(string? body, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(본문 없음)";

        // 간단한 HTML 태그 제거
        var text = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        return text.Length > maxLength ? text[..maxLength] + "..." : text;
    }
}
