using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using mAIx.Models;

namespace mAIx.Data;

/// <summary>
/// mAIx 데이터베이스 컨텍스트 - SQLite 기반 로컬 데이터 저장소
/// </summary>
public class mAIxDbContext : DbContext
{
    public mAIxDbContext(DbContextOptions<mAIxDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // PendingModelChangesWarning 경고 무시 (마이그레이션 적용 전 경고)
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    /// <summary>
    /// SQLite WAL 모드 + synchronous=NORMAL 적용 (성능 최적화)
    /// </summary>
    public static void ApplyWalMode(DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) connection.Open();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
        }
        finally
        {
            if (!wasOpen) connection.Close();
        }
    }

    // ===== DbSet 정의 (12개) =====
    // Note: Account는 DB에 저장하지 않고 로컬 XML 파일로 관리 (%APPDATA%\mAIx\conf\autologin.xml)

    /// <summary>
    /// 이메일 테이블
    /// </summary>
    public DbSet<Email> Emails { get; set; } = null!;

    /// <summary>
    /// 첨부파일 테이블
    /// </summary>
    public DbSet<Attachment> Attachments { get; set; } = null!;

    /// <summary>
    /// 폴더 테이블
    /// </summary>
    public DbSet<Folder> Folders { get; set; } = null!;

    /// <summary>
    /// TODO 테이블
    /// </summary>
    public DbSet<Todo> Todos { get; set; } = null!;

    /// <summary>
    /// 계약정보 테이블
    /// </summary>
    public DbSet<ContractInfo> ContractInfos { get; set; } = null!;

    /// <summary>
    /// AI 설정 테이블
    /// </summary>
    public DbSet<AISetting> AISettings { get; set; } = null!;

    /// <summary>
    /// 동기화 상태 테이블
    /// </summary>
    public DbSet<SyncState> SyncStates { get; set; } = null!;

    /// <summary>
    /// 서명 테이블
    /// </summary>
    public DbSet<Signature> Signatures { get; set; } = null!;

    /// <summary>
    /// 프롬프트 테이블
    /// </summary>
    public DbSet<Prompt> Prompts { get; set; } = null!;

    /// <summary>
    /// 프롬프트 테스트 이력 테이블
    /// </summary>
    public DbSet<PromptTestHistory> PromptTestHistories { get; set; } = null!;

    /// <summary>
    /// Teams 메시지 테이블
    /// </summary>
    public DbSet<TeamsMessage> TeamsMessages { get; set; } = null!;

    /// <summary>
    /// OneNote 페이지 테이블
    /// </summary>
    public DbSet<OneNotePage> OneNotePages { get; set; } = null!;

    /// <summary>
    /// 문서 변환기 설정 테이블
    /// </summary>
    public DbSet<ConverterSetting> ConverterSettings { get; set; } = null!;

    /// <summary>
    /// 캘린더 이벤트 테이블
    /// </summary>
    public DbSet<CalendarEvent> CalendarEvents { get; set; } = null!;

    /// <summary>
    /// 캘린더 동기화 상태 테이블
    /// </summary>
    public DbSet<CalendarSyncState> CalendarSyncStates { get; set; } = null!;

    /// <summary>
    /// 채팅 즐겨찾기 테이블 (로컬 전용)
    /// </summary>
    public DbSet<ChatFavorite> ChatFavorites { get; set; } = null!;

    /// <summary>
    /// 메일 규칙 테이블
    /// </summary>
    public DbSet<MailRule> MailRules { get; set; } = null!;

    /// <summary>
    /// Quick Steps 테이블
    /// </summary>
    public DbSet<QuickStep> QuickSteps { get; set; } = null!;

    /// <summary>
    /// Split Inbox 규칙 테이블
    /// </summary>
    public DbSet<SplitInboxRule> SplitInboxRules { get; set; } = null!;

    /// <summary>
    /// 스크리너 항목 테이블 (발신자 차단/허용)
    /// </summary>
    public DbSet<ScreenerEntry> ScreenerEntries { get; set; } = null!;

    /// <summary>
    /// Reply Later 큐 테이블
    /// </summary>
    public DbSet<ReplyLaterItem> ReplyLaterItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ===== Email 인덱스 =====
        modelBuilder.Entity<Email>(entity =>
        {
            // InternetMessageId + ParentFolderId 복합 유니크 인덱스
            // (같은 메일이 보낸편지함/받은편지함에 모두 있을 수 있음)
            entity.HasIndex(e => new { e.InternetMessageId, e.ParentFolderId })
                .IsUnique()
                .HasDatabaseName("IX_Email_InternetMessageId_ParentFolderId");

            // InternetMessageId 인덱스 (검색용, 유니크 아님)
            entity.HasIndex(e => e.InternetMessageId)
                .HasDatabaseName("IX_Email_InternetMessageId");

            // ConversationId 인덱스 (스레드 조회용)
            entity.HasIndex(e => e.ConversationId)
                .HasDatabaseName("IX_Email_ConversationId");

            // AccountEmail 인덱스 (계정별 조회)
            entity.HasIndex(e => e.AccountEmail)
                .HasDatabaseName("IX_Email_AccountEmail");

            // ReceivedDateTime 인덱스 (날짜 정렬)
            entity.HasIndex(e => e.ReceivedDateTime)
                .HasDatabaseName("IX_Email_ReceivedDateTime");

            // ParentFolderId 인덱스 (폴더별 조회)
            entity.HasIndex(e => e.ParentFolderId)
                .HasDatabaseName("IX_Email_ParentFolderId");

            // AnalysisStatus 인덱스 (분석 상태별 조회)
            entity.HasIndex(e => e.AnalysisStatus)
                .HasDatabaseName("IX_Email_AnalysisStatus");

            // SnoozedUntil 인덱스 (스누즈 해제 조회용)
            entity.HasIndex(e => e.SnoozedUntil)
                .HasDatabaseName("IX_Email_SnoozedUntil");
        });

        // ===== Attachment 인덱스 =====
        modelBuilder.Entity<Attachment>(entity =>
        {
            // EmailId 인덱스 (이메일별 첨부파일 조회)
            entity.HasIndex(a => a.EmailId)
                .HasDatabaseName("IX_Attachment_EmailId");

            // ConversionStatus 인덱스 (변환 상태별 조회)
            entity.HasIndex(a => a.ConversionStatus)
                .HasDatabaseName("IX_Attachment_ConversionStatus");
        });

        // ===== Folder 인덱스 =====
        modelBuilder.Entity<Folder>(entity =>
        {
            // AccountEmail 인덱스 (계정별 폴더 조회)
            entity.HasIndex(f => f.AccountEmail)
                .HasDatabaseName("IX_Folder_AccountEmail");
        });

        // ===== SyncState 인덱스 =====
        modelBuilder.Entity<SyncState>(entity =>
        {
            // AccountEmail + FolderId unique 복합 인덱스
            entity.HasIndex(s => new { s.AccountEmail, s.FolderId })
                .IsUnique()
                .HasDatabaseName("IX_SyncState_AccountEmail_FolderId");
        });

        // ===== Prompt 인덱스 =====
        modelBuilder.Entity<Prompt>(entity =>
        {
            // PromptKey unique 인덱스
            entity.HasIndex(p => p.PromptKey)
                .IsUnique()
                .HasDatabaseName("IX_Prompt_PromptKey");
        });

        // ===== ContractInfo 인덱스 (1:1 관계) =====
        modelBuilder.Entity<ContractInfo>(entity =>
        {
            // EmailId unique 인덱스 (1:1 관계 보장)
            entity.HasIndex(c => c.EmailId)
                .IsUnique()
                .HasDatabaseName("IX_ContractInfo_EmailId");

            // Email과 1:1 관계 설정
            entity.HasOne(c => c.Email)
                .WithOne(e => e.ContractInfo)
                .HasForeignKey<ContractInfo>(c => c.EmailId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ===== ConverterSetting 인덱스 =====
        modelBuilder.Entity<ConverterSetting>(entity =>
        {
            // Extension unique 인덱스
            entity.HasIndex(c => c.Extension)
                .IsUnique()
                .HasDatabaseName("IX_ConverterSetting_Extension");
        });

        // ===== CalendarEvent 인덱스 =====
        modelBuilder.Entity<CalendarEvent>(entity =>
        {
            // GraphId 인덱스 (Graph API ID로 빠른 조회)
            entity.HasIndex(e => e.GraphId)
                .HasDatabaseName("IX_CalendarEvent_GraphId");

            // AccountEmail + CalendarId 복합 인덱스
            entity.HasIndex(e => new { e.AccountEmail, e.CalendarId })
                .HasDatabaseName("IX_CalendarEvent_AccountEmail_CalendarId");

            // 시작일시 인덱스 (날짜 범위 조회)
            entity.HasIndex(e => e.StartDateTime)
                .HasDatabaseName("IX_CalendarEvent_StartDateTime");

            // AccountEmail + StartDateTime 복합 인덱스 (계정별 날짜 조회)
            entity.HasIndex(e => new { e.AccountEmail, e.StartDateTime })
                .HasDatabaseName("IX_CalendarEvent_AccountEmail_StartDateTime");

            // ICalUId 인덱스 (반복 일정 조회)
            entity.HasIndex(e => e.ICalUId)
                .HasDatabaseName("IX_CalendarEvent_ICalUId");

            // SeriesMasterId 인덱스 (반복 일정 인스턴스 조회)
            entity.HasIndex(e => e.SeriesMasterId)
                .HasDatabaseName("IX_CalendarEvent_SeriesMasterId");
        });

        // ===== CalendarSyncState 인덱스 =====
        modelBuilder.Entity<CalendarSyncState>(entity =>
        {
            // AccountEmail + CalendarId unique 복합 인덱스
            entity.HasIndex(s => new { s.AccountEmail, s.CalendarId })
                .IsUnique()
                .HasDatabaseName("IX_CalendarSyncState_AccountEmail_CalendarId");
        });

        // ===== MailRule 인덱스 =====
        modelBuilder.Entity<MailRule>(entity =>
        {
            // IsEnabled 인덱스 (활성 규칙 조회)
            entity.HasIndex(r => r.IsEnabled)
                .HasDatabaseName("IX_MailRule_IsEnabled");

            // Priority 인덱스 (우선순위 정렬)
            entity.HasIndex(r => r.Priority)
                .HasDatabaseName("IX_MailRule_Priority");

            // AccountEmail 인덱스 (계정별 규칙 조회)
            entity.HasIndex(r => r.AccountEmail)
                .HasDatabaseName("IX_MailRule_AccountEmail");
        });

        // ===== QuickStep 인덱스 =====
        modelBuilder.Entity<QuickStep>(entity =>
        {
            // IsEnabled 인덱스 (활성 QuickStep 조회)
            entity.HasIndex(q => q.IsEnabled)
                .HasDatabaseName("IX_QuickStep_IsEnabled");

            // SortOrder 인덱스 (정렬 조회)
            entity.HasIndex(q => q.SortOrder)
                .HasDatabaseName("IX_QuickStep_SortOrder");
        });

        // ===== SplitInboxRule 인덱스 =====
        modelBuilder.Entity<SplitInboxRule>(entity =>
        {
            // IsEnabled 인덱스 (활성 규칙 조회)
            entity.HasIndex(r => r.IsEnabled)
                .HasDatabaseName("IX_SplitInboxRule_IsEnabled");

            // SortOrder 인덱스 (정렬 조회)
            entity.HasIndex(r => r.SortOrder)
                .HasDatabaseName("IX_SplitInboxRule_SortOrder");
        });

        // ===== ScreenerEntry 인덱스 =====
        modelBuilder.Entity<ScreenerEntry>(entity =>
        {
            // SenderEmail 인덱스 (발신자 조회)
            entity.HasIndex(s => s.SenderEmail)
                .HasDatabaseName("IX_ScreenerEntry_SenderEmail");

            // Action 인덱스 (차단/허용 필터)
            entity.HasIndex(s => s.Action)
                .HasDatabaseName("IX_ScreenerEntry_Action");
        });

        // ===== ReplyLaterItem 인덱스 =====
        modelBuilder.Entity<ReplyLaterItem>(entity =>
        {
            // IsCompleted 인덱스 (미완료 조회)
            entity.HasIndex(r => r.IsCompleted)
                .HasDatabaseName("IX_ReplyLaterItem_IsCompleted");

            // RemindAt 인덱스 (알림 시각 조회)
            entity.HasIndex(r => r.RemindAt)
                .HasDatabaseName("IX_ReplyLaterItem_RemindAt");
        });
    }
}
