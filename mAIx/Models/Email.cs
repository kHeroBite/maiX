using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mAIx.Models;

/// <summary>
/// 이메일 모델 - Graph API에서 가져온 이메일과 AI 분석 결과를 저장
/// DB 엔티티 + INotifyPropertyChanged 구현 (EF Core 직접 바인딩 지원)
/// </summary>
public class Email : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Graph API의 고유 메시지 ID (RFC 2822 형식)
    /// </summary>
    [MaxLength(500)]
    public string? InternetMessageId { get; set; }

    /// <summary>
    /// Exchange 고유 Entry ID
    /// </summary>
    [MaxLength(500)]
    public string? EntryId { get; set; }

    /// <summary>
    /// 대화 스레드 ID
    /// </summary>
    [MaxLength(500)]
    public string? ConversationId { get; set; }

    /// <summary>
    /// 이메일 제목
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// 이메일 본문 (HTML 또는 텍스트)
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// HTML 본문 여부
    /// </summary>
    public bool IsHtml { get; set; }

    /// <summary>
    /// 발신자 이메일 주소
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// 수신자 이메일 주소 (JSON 배열)
    /// </summary>
    public string? To { get; set; }

    /// <summary>
    /// 참조 수신자 (JSON 배열)
    /// </summary>
    public string? Cc { get; set; }

    /// <summary>
    /// 숨은 참조 수신자 (JSON 배열)
    /// </summary>
    public string? Bcc { get; set; }

    /// <summary>
    /// 수신 일시
    /// </summary>
    public DateTime? ReceivedDateTime { get; set; }

    /// <summary>
    /// 읽음 여부
    /// </summary>
    private bool _isRead;
    public bool IsRead
    {
        get => _isRead;
        set { if (_isRead != value) { _isRead = value; OnPropertyChanged(nameof(IsRead)); } }
    }

    /// <summary>
    /// 중요도 (low, normal, high)
    /// </summary>
    [MaxLength(20)]
    public string? Importance { get; set; }

    /// <summary>
    /// 플래그 상태 (flagged, complete, notFlagged)
    /// </summary>
    [MaxLength(20)]
    private string? _flagStatus;
    public string? FlagStatus
    {
        get => _flagStatus;
        set { if (_flagStatus != value) { _flagStatus = value; OnPropertyChanged(nameof(FlagStatus)); } }
    }

    /// <summary>
    /// 카테고리 목록 (JSON 배열)
    /// </summary>
    public string? Categories { get; set; }

    /// <summary>
    /// 사용자 별표 (즐겨찾기)
    /// </summary>
    public bool IsStarred { get; set; }

    /// <summary>
    /// 메일 고정 (Pin) 여부
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// 메일 고정 시간
    /// </summary>
    public DateTime? PinnedAt { get; set; }

    /// <summary>
    /// 첨부파일 존재 여부
    /// </summary>
    public bool HasAttachments { get; set; }

    /// <summary>
    /// 상위 폴더 ID
    /// </summary>
    [MaxLength(500)]
    public string? ParentFolderId { get; set; }

    /// <summary>
    /// 임시보관함 메일 여부 (계산된 속성)
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsDraft { get; set; }

    /// <summary>
    /// 미리보기 텍스트 (DB 비저장, 런타임 할당)
    /// Graph API BodyPreview 또는 Body에서 첫 100자 추출
    /// </summary>
    [NotMapped]
    public string? PreviewText { get; set; }

    /// <summary>
    /// AI 요약 또는 미리보기 텍스트 중 우선 표시 값 (DB 비저장)
    /// </summary>
    [NotMapped]
    public string? PreviewOrSummary => AiSummaryBrief ?? SummaryOneline ?? PreviewText;

    // ===== AI 분석 결과 필드 =====

    /// <summary>
    /// AI 생성 한줄 요약
    /// </summary>
    [MaxLength(500)]
    public string? SummaryOneline { get; set; }

    /// <summary>
    /// AI 생성 상세 요약
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// 우선순위 점수 (0-100)
    /// </summary>
    public int? PriorityScore { get; set; }

    /// <summary>
    /// 우선순위 레벨 (critical, high, medium, low)
    /// </summary>
    [MaxLength(20)]
    public string? PriorityLevel { get; set; }

    /// <summary>
    /// 긴급도 레벨 (urgent, soon, normal, later)
    /// </summary>
    [MaxLength(20)]
    public string? UrgencyLevel { get; set; }

    /// <summary>
    /// AI가 추출한 마감일
    /// </summary>
    public DateTime? Deadline { get; set; }

    /// <summary>
    /// 비업무 메일 여부 (광고, 뉴스레터 등)
    /// </summary>
    public bool IsNonBusiness { get; set; }

    /// <summary>
    /// 내 역할 위치 (to, cc, bcc)
    /// </summary>
    [MaxLength(10)]
    public string? MyPosition { get; set; }

    /// <summary>
    /// AI 추출 키워드 (JSON 배열)
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// 예약 발송 시간 (null이면 즉시 발송 또는 예약 없음)
    /// </summary>
    public DateTime? ScheduledSendTime { get; set; }

    /// <summary>
    /// 스누즈 해제 시각. null = 스누즈 안 됨, 값 = 해당 시각까지 목록에서 숨김
    /// </summary>
    private DateTime? _snoozedUntil;
    public DateTime? SnoozedUntil
    {
        get => _snoozedUntil;
        set { if (_snoozedUntil != value) { _snoozedUntil = value; OnPropertyChanged(nameof(SnoozedUntil)); } }
    }

    // ===== AI 분류 필드 =====

    /// <summary>
    /// AI 분류 카테고리 (긴급/액션필요/FYI/대기/기타). 기본값 null (미분류)
    /// </summary>
    [MaxLength(50)]
    public string? AiCategory { get; set; }

    /// <summary>
    /// AI 우선순위 점수 (0~100). 기본값 0
    /// </summary>
    public int AiPriority { get; set; } = 0;

    /// <summary>
    /// AI 판정: 액션 필요 여부. 기본값 false
    /// </summary>
    public bool AiActionRequired { get; set; } = false;

    /// <summary>
    /// AI 한줄 요약. 기본값 null
    /// </summary>
    [MaxLength(500)]
    public string? AiSummaryBrief { get; set; }

    /// <summary>
    /// AI 분석 상태 (pending, completed, failed)
    /// </summary>
    [MaxLength(20)]
    public string AnalysisStatus { get; set; } = "pending";

    /// <summary>
    /// 소속 계정 이메일
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string AccountEmail { get; set; } = string.Empty;

    // ===== 네비게이션 프로퍼티 =====

    /// <summary>
    /// 첨부파일 목록
    /// </summary>
    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    /// <summary>
    /// TODO 목록
    /// </summary>
    public virtual ICollection<Todo> Todos { get; set; } = new List<Todo>();

    /// <summary>
    /// 계약 정보 (1:1 관계)
    /// </summary>
    public virtual ContractInfo? ContractInfo { get; set; }
}
