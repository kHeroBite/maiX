using System;
using System.Collections.Generic;
using mAIx.Models;

namespace mAIx.Services.Cache;

/// <summary>
/// 폴더 단위 메일 캐시 상태 (MailFolderCacheService 내부 전용).
/// - Emails: 현재 로드된 메일 리스트 (페이지네이션 포함)
/// - EmailSkip/HasMoreEmails: 페이지네이션 상태 (폴더 전환 후 복귀 시 그대로 복원)
/// - ShowSnoozedEmails: 캐시 키에 포함되는 필터 상태 (필터 다르면 별도 캐시)
/// - HighWaterMark: Emails 중 최신 ReceivedDateTime (증분 동기화 기준점)
/// - ScrollOffset: ListBox 세로 스크롤 오프셋 (폴더 복귀 시 복원)
/// - LoadedAt/LastAccessedAt: LRU evict 판단용
/// </summary>
internal sealed class CachedFolderState
{
    public List<Email> Emails { get; set; } = new();
    public int EmailSkip { get; set; }
    public bool HasMoreEmails { get; set; }
    public bool ShowSnoozedEmails { get; set; }
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public DateTimeOffset HighWaterMark { get; set; } = DateTimeOffset.MinValue;
    public double ScrollOffset { get; set; }
}
