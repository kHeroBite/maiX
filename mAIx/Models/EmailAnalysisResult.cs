using System;
using System.Collections.Generic;

namespace mAIx.Models;

/// <summary>
/// 이메일 분석 결과 데이터 클래스
/// 7단계 분석 파이프라인의 결과를 담는 객체
/// </summary>
public class EmailAnalysisResult
{
    /// <summary>
    /// 분석 대상 이메일 ID
    /// </summary>
    public int EmailId { get; set; }

    /// <summary>
    /// 분석 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 분석 실패 시 오류 메시지
    /// </summary>
    public string? ErrorMessage { get; set; }

    // ===== 1단계: 한줄 요약 =====

    /// <summary>
    /// AI 생성 한줄 요약
    /// </summary>
    public string? SummaryOneline { get; set; }

    // ===== 2단계: 상세 요약 =====

    /// <summary>
    /// AI 생성 상세 요약
    /// </summary>
    public string? SummaryDetail { get; set; }

    // ===== 3단계: 마감일 추출 =====

    /// <summary>
    /// 추출된 마감일
    /// </summary>
    public DateTime? Deadline { get; set; }

    /// <summary>
    /// 마감일 원본 텍스트 (예: "다음주 금요일까지")
    /// </summary>
    public string? DeadlineText { get; set; }

    // ===== 4단계: 중요도 판단 =====

    /// <summary>
    /// 중요도 레벨 (critical, high, medium, low)
    /// </summary>
    public string? ImportanceLevel { get; set; }

    /// <summary>
    /// 중요도 점수 (0-100)
    /// </summary>
    public int ImportanceScore { get; set; }

    /// <summary>
    /// 중요도 판단 근거
    /// </summary>
    public string? ImportanceReason { get; set; }

    // ===== 5단계: 긴급도 판단 =====

    /// <summary>
    /// 긴급도 레벨 (urgent, soon, normal, later)
    /// </summary>
    public string? UrgencyLevel { get; set; }

    /// <summary>
    /// 긴급도 점수 (0-100)
    /// </summary>
    public int UrgencyScore { get; set; }

    /// <summary>
    /// 긴급도 판단 근거
    /// </summary>
    public string? UrgencyReason { get; set; }

    // ===== 6단계: 계약정보 추출 =====

    /// <summary>
    /// 계약 정보 존재 여부
    /// </summary>
    public bool HasContractInfo { get; set; }

    /// <summary>
    /// 추출된 계약 정보
    /// </summary>
    public ContractInfoResult? ContractInfo { get; set; }

    // ===== 7단계: 할일 추출 =====

    /// <summary>
    /// 추출된 할일 목록
    /// </summary>
    public List<TodoResult> Todos { get; set; } = new();

    // ===== 최종 우선순위 =====

    /// <summary>
    /// 최종 우선순위 점수 (0-100)
    /// </summary>
    public int PriorityScore { get; set; }

    /// <summary>
    /// 우선순위 레벨 (critical, high, medium, low)
    /// </summary>
    public string? PriorityLevel { get; set; }

    // ===== 메타 정보 =====

    /// <summary>
    /// 분석 시작 시간
    /// </summary>
    public DateTime AnalysisStartedAt { get; set; }

    /// <summary>
    /// 분석 완료 시간
    /// </summary>
    public DateTime AnalysisCompletedAt { get; set; }

    /// <summary>
    /// 총 분석 소요 시간 (밀리초)
    /// </summary>
    public long AnalysisDurationMs { get; set; }

    /// <summary>
    /// 각 단계별 소요 시간 (밀리초)
    /// </summary>
    public Dictionary<string, long> StepDurations { get; set; } = new();

    /// <summary>
    /// 사용된 AI Provider
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 비업무 메일 여부 (광고, 뉴스레터 등)
    /// </summary>
    public bool IsNonBusiness { get; set; }

    /// <summary>
    /// 추출된 키워드 목록
    /// </summary>
    public List<string> Keywords { get; set; } = new();
}

/// <summary>
/// 계약 정보 추출 결과
/// </summary>
public class ContractInfoResult
{
    /// <summary>
    /// 계약명
    /// </summary>
    public string? ContractName { get; set; }

    /// <summary>
    /// 계약 금액 (원 단위)
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// 계약 금액 원본 텍스트
    /// </summary>
    public string? AmountText { get; set; }

    /// <summary>
    /// 계약 기간
    /// </summary>
    public string? Period { get; set; }

    /// <summary>
    /// 계약 상대방 (회사명 또는 담당자)
    /// </summary>
    public string? CounterParty { get; set; }

    /// <summary>
    /// 담당자 정보
    /// </summary>
    public string? ContactPerson { get; set; }

    /// <summary>
    /// 마감일
    /// </summary>
    public DateTime? Deadline { get; set; }

    /// <summary>
    /// 특이사항
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// 투입 공수 (Man-Month)
    /// </summary>
    public decimal? ManMonth { get; set; }

    /// <summary>
    /// 근무 위치
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// 원격근무 가능 여부
    /// </summary>
    public bool? IsRemote { get; set; }

    /// <summary>
    /// 업무 범위
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// 사업 유형
    /// </summary>
    public string? BusinessType { get; set; }
}

/// <summary>
/// 할일 추출 결과
/// </summary>
public class TodoResult
{
    /// <summary>
    /// 할일 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 마감일
    /// </summary>
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// 마감일 원본 텍스트
    /// </summary>
    public string? DueDateText { get; set; }

    /// <summary>
    /// 우선순위 (1-5, 1이 가장 높음)
    /// </summary>
    public int Priority { get; set; } = 3;

    /// <summary>
    /// 담당자 (추정)
    /// </summary>
    public string? Assignee { get; set; }

    /// <summary>
    /// 카테고리 (회신, 검토, 작성, 확인, 보고, 기타)
    /// </summary>
    public string? Category { get; set; }
}
