using System;
using System.Collections.Generic;
using System.Linq;
using mAIx.Models;
using mAIx.Services.Sync;
using Serilog;

namespace mAIx.Services.Cache;

/// <summary>
/// 메일함 폴더별 메일 리스트 캐시 서비스 (DI 싱글톤).
///
/// 목적:
///   - 폴더 전환 시 DB 재조회 제거 → 즉시 표시(&lt;100ms)
///   - 백그라운드 동기화 이벤트(EmailsSavedToFolder) 구독으로 캐시 증분 갱신
///   - CRUD 훅(Delete/Move/Update...)으로 UI-DB 정합성 유지
///
/// 캐시 키: (FolderId, ShowSnoozedEmails) — 필터 상태 다르면 별도 저장.
/// 상한: 10개 엔트리 (LRU evict — LastAccessedAt 기준).
/// 스레드: 모든 public 메서드 lock(_lock). Email 객체 속성 변경은 호출자(UI 스레드)가 수행.
/// </summary>
public sealed class MailFolderCacheService : IDisposable
{
    private const int DefaultMaxFolders = 10;
    private const string LogPrefix = "[Cache]";

    private readonly Dictionary<string, CachedFolderState> _cache = new();
    private readonly object _lock = new();
    private readonly int _maxFolders;
    private readonly BackgroundSyncService _syncService;
    private readonly ILogger _logger;
    private bool _disposed;

    public MailFolderCacheService(BackgroundSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _logger = Log.ForContext<MailFolderCacheService>();
        _maxFolders = DefaultMaxFolders;

        _syncService.EmailsSavedToFolder += OnEmailsSavedToFolder;
    }

    // ───────────────────────────── 조회 / 저장 ─────────────────────────────

    /// <summary>캐시 조회. 히트 시 LastAccessedAt 갱신.</summary>
    public bool TryGet(
        string folderId,
        bool showSnoozed,
        out List<Email> emails,
        out int emailSkip,
        out bool hasMore,
        out double scrollOffset)
    {
        emails = new List<Email>();
        emailSkip = 0;
        hasMore = false;
        scrollOffset = 0;

        if (string.IsNullOrEmpty(folderId))
            return false;

        lock (_lock)
        {
            var key = MakeKey(folderId, showSnoozed);
            if (_cache.TryGetValue(key, out var state))
            {
                state.LastAccessedAt = DateTime.UtcNow;
                emails = state.Emails;
                emailSkip = state.EmailSkip;
                hasMore = state.HasMoreEmails;
                scrollOffset = state.ScrollOffset;
                _logger.Debug("{Prefix} hit folder={Folder} snoozed={Snoozed} count={Count}",
                    LogPrefix, folderId, showSnoozed, emails.Count);
                return true;
            }

            _logger.Debug("{Prefix} miss folder={Folder} snoozed={Snoozed}",
                LogPrefix, folderId, showSnoozed);
            return false;
        }
    }

    /// <summary>캐시 저장(또는 덮어쓰기). 상한 초과 시 LRU evict.</summary>
    public void Set(
        string folderId,
        bool showSnoozed,
        List<Email> emails,
        int emailSkip,
        bool hasMore)
    {
        if (string.IsNullOrEmpty(folderId) || emails == null)
            return;

        lock (_lock)
        {
            var key = MakeKey(folderId, showSnoozed);
            var state = new CachedFolderState
            {
                Emails = emails,
                EmailSkip = emailSkip,
                HasMoreEmails = hasMore,
                ShowSnoozedEmails = showSnoozed,
                LoadedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                HighWaterMark = ComputeHighWaterMark(emails),
                ScrollOffset = 0
            };
            _cache[key] = state;
            _logger.Debug("{Prefix} set folder={Folder} snoozed={Snoozed} count={Count} skip={Skip} more={More}",
                LogPrefix, folderId, showSnoozed, emails.Count, emailSkip, hasMore);

            EvictIfNeeded();
        }
    }

    /// <summary>페이지 추가(LoadMore). 중복(EntryId) 자동 제거. HighWaterMark 갱신.</summary>
    public void AppendPage(
        string folderId,
        bool showSnoozed,
        IEnumerable<Email> more,
        int newSkip,
        bool hasMore)
    {
        if (string.IsNullOrEmpty(folderId) || more == null)
            return;

        lock (_lock)
        {
            var key = MakeKey(folderId, showSnoozed);
            if (!_cache.TryGetValue(key, out var state))
                return;

            var existingIds = new HashSet<string>(
                state.Emails.Where(e => e.EntryId != null).Select(e => e.EntryId!),
                StringComparer.Ordinal);

            int added = 0;
            foreach (var email in more)
            {
                if (email.EntryId != null && existingIds.Add(email.EntryId))
                {
                    state.Emails.Add(email);
                    added++;
                }
            }

            state.EmailSkip = newSkip;
            state.HasMoreEmails = hasMore;
            state.LastAccessedAt = DateTime.UtcNow;
            state.HighWaterMark = ComputeHighWaterMark(state.Emails);

            _logger.Debug("{Prefix} append folder={Folder} added={Added} skip={Skip} more={More}",
                LogPrefix, folderId, added, newSkip, hasMore);
        }
    }

    /// <summary>현재 캐시된 리스트 스냅샷(없으면 null).</summary>
    public List<Email>? GetSnapshot(string folderId, bool showSnoozed)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(MakeKey(folderId, showSnoozed), out var state)
                ? state.Emails
                : null;
        }
    }

    /// <summary>캐시된 최신 ReceivedDateTime — 증분 동기화 기준점.</summary>
    public DateTimeOffset? GetHighWaterMark(string folderId, bool showSnoozed)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(MakeKey(folderId, showSnoozed), out var state)
                ? state.HighWaterMark
                : (DateTimeOffset?)null;
        }
    }

    // ───────────────────────────── CRUD 훅 ─────────────────────────────

    /// <summary>메일 1건 추가(복원/수동 추가). 해당 폴더 모든 필터 변형 캐시에 prepend.</summary>
    public void OnEmailAdded(string folderId, Email email)
    {
        if (string.IsNullOrEmpty(folderId) || email == null)
            return;

        lock (_lock)
        {
            foreach (var state in GetFolderEntries(folderId))
            {
                if (email.EntryId != null &&
                    state.Emails.Any(e => e.EntryId == email.EntryId))
                    continue;

                state.Emails.Insert(0, email);
                state.HighWaterMark = ComputeHighWaterMark(state.Emails);
                state.LastAccessedAt = DateTime.UtcNow;
            }
            _logger.Debug("{Prefix} added folder={Folder}", LogPrefix, folderId);
        }
    }

    /// <summary>삭제: 해당 폴더 캐시에서 EntryId 일치 항목 제거.</summary>
    public void OnEmailsDeleted(string folderId, IEnumerable<string> entryIds)
    {
        if (string.IsNullOrEmpty(folderId) || entryIds == null)
            return;

        var ids = new HashSet<string>(entryIds, StringComparer.Ordinal);
        if (ids.Count == 0)
            return;

        lock (_lock)
        {
            int totalRemoved = 0;
            foreach (var state in GetFolderEntries(folderId))
            {
                int removed = state.Emails.RemoveAll(e => e.EntryId != null && ids.Contains(e.EntryId));
                if (removed > 0)
                {
                    state.LastAccessedAt = DateTime.UtcNow;
                    state.HighWaterMark = ComputeHighWaterMark(state.Emails);
                    totalRemoved += removed;
                }
            }
            _logger.Debug("{Prefix} removed folder={Folder} count={Count}",
                LogPrefix, folderId, totalRemoved);
        }
    }

    /// <summary>이동: source에서 제거 + target 무효화(다음 방문 시 DB 재조회).</summary>
    public void OnEmailsMoved(string sourceFolderId, string targetFolderId, IEnumerable<string> entryIds)
    {
        if (string.IsNullOrEmpty(sourceFolderId) || entryIds == null)
            return;

        var ids = new HashSet<string>(entryIds, StringComparer.Ordinal);
        if (ids.Count == 0)
            return;

        lock (_lock)
        {
            int totalRemoved = 0;
            foreach (var state in GetFolderEntries(sourceFolderId))
            {
                int removed = state.Emails.RemoveAll(e => e.EntryId != null && ids.Contains(e.EntryId));
                if (removed > 0)
                {
                    state.LastAccessedAt = DateTime.UtcNow;
                    state.HighWaterMark = ComputeHighWaterMark(state.Emails);
                    totalRemoved += removed;
                }
            }

            // target은 무효화 — 다음 진입 시 DB에서 새로 로드
            if (!string.IsNullOrEmpty(targetFolderId))
                RemoveFolderEntriesNoLock(targetFolderId);

            _logger.Debug("{Prefix} moved src={Src} tgt={Tgt} count={Count}",
                LogPrefix, sourceFolderId, targetFolderId ?? "(null)", totalRemoved);
        }
    }

    /// <summary>읽음/플래그/핀 등 항목 속성 변경. 리스트 재구성 없이 개별 Email 객체만 mutate.</summary>
    public void OnEmailsUpdated(IEnumerable<string> entryIds, Action<Email> mutate)
    {
        if (entryIds == null || mutate == null)
            return;

        var ids = new HashSet<string>(entryIds, StringComparer.Ordinal);
        if (ids.Count == 0)
            return;

        lock (_lock)
        {
            int touched = 0;
            foreach (var state in _cache.Values)
            {
                foreach (var email in state.Emails)
                {
                    if (email.EntryId != null && ids.Contains(email.EntryId))
                    {
                        mutate(email);
                        touched++;
                    }
                }
            }
            _logger.Debug("{Prefix} updated count={Count}", LogPrefix, touched);
        }
    }

    // ───────────────────────────── 무효화 / 스크롤 ─────────────────────────────

    public void InvalidateFolder(string folderId)
    {
        if (string.IsNullOrEmpty(folderId))
            return;

        lock (_lock)
        {
            RemoveFolderEntriesNoLock(folderId);
            _logger.Debug("{Prefix} invalidate folder={Folder}", LogPrefix, folderId);
        }
    }

    public void InvalidateAll()
    {
        lock (_lock)
        {
            _cache.Clear();
            _logger.Debug("{Prefix} invalidate all", LogPrefix);
        }
    }

    public void SetScrollOffset(string folderId, bool showSnoozed, double offset)
    {
        if (string.IsNullOrEmpty(folderId))
            return;

        lock (_lock)
        {
            if (_cache.TryGetValue(MakeKey(folderId, showSnoozed), out var state))
            {
                state.ScrollOffset = offset;
                state.LastAccessedAt = DateTime.UtcNow;
            }
        }
    }

    // ───────────────────────────── 백그라운드 sync 훅 ─────────────────────────────

    /// <summary>
    /// BackgroundSyncService.EmailsSavedToFolder 구독자 — 동기화로 저장된 메일을 캐시에 증분 추가.
    /// 해당 폴더의 모든 필터 변형 캐시에 prepend(중복 EntryId 제외).
    /// </summary>
    private void OnEmailsSavedToFolder(string folderId, IReadOnlyList<Email> saved)
    {
        if (string.IsNullOrEmpty(folderId) || saved == null || saved.Count == 0)
            return;

        lock (_lock)
        {
            int appended = 0;
            foreach (var state in GetFolderEntries(folderId))
            {
                var existingIds = new HashSet<string>(
                    state.Emails.Where(e => e.EntryId != null).Select(e => e.EntryId!),
                    StringComparer.Ordinal);

                // 최신 메일이 위에 오도록: 전체 리스트를 병합 후 ReceivedDateTime 내림차순 정렬
                bool changed = false;
                foreach (var email in saved)
                {
                    if (email.EntryId != null && existingIds.Add(email.EntryId))
                    {
                        state.Emails.Add(email);
                        changed = true;
                    }
                }

                if (changed)
                {
                    state.Emails.Sort((a, b) =>
                        Nullable.Compare(b.ReceivedDateTime, a.ReceivedDateTime));
                    state.HighWaterMark = ComputeHighWaterMark(state.Emails);
                    state.LastAccessedAt = DateTime.UtcNow;
                    appended++;
                }
            }

            _logger.Debug("{Prefix} append saved folder={Folder} count={Count} variants={Variants}",
                LogPrefix, folderId, saved.Count, appended);
        }
    }

    // ───────────────────────────── 내부 유틸 ─────────────────────────────

    private static string MakeKey(string folderId, bool showSnoozed)
        => folderId + "|" + (showSnoozed ? "1" : "0");

    /// <summary>folderId 접두 일치하는 모든 캐시 엔트리(필터 변형 포함) 반환. 반드시 lock 보유 상태에서 호출.</summary>
    private IEnumerable<CachedFolderState> GetFolderEntries(string folderId)
    {
        var prefix = folderId + "|";
        foreach (var kvp in _cache)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                yield return kvp.Value;
        }
    }

    /// <summary>folderId 관련 모든 엔트리 제거. 반드시 lock 보유 상태에서 호출.</summary>
    private void RemoveFolderEntriesNoLock(string folderId)
    {
        var prefix = folderId + "|";
        var keys = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var k in keys)
            _cache.Remove(k);
    }

    private static DateTimeOffset ComputeHighWaterMark(List<Email> emails)
    {
        DateTimeOffset high = DateTimeOffset.MinValue;
        foreach (var email in emails)
        {
            if (email.ReceivedDateTime.HasValue)
            {
                var dto = new DateTimeOffset(email.ReceivedDateTime.Value,
                    email.ReceivedDateTime.Value.Kind == DateTimeKind.Utc ? TimeSpan.Zero : TimeSpan.Zero);
                if (dto > high) high = dto;
            }
        }
        return high;
    }

    /// <summary>LRU evict — _maxFolders 초과 시 LastAccessedAt 가장 오래된 엔트리 제거.</summary>
    private void EvictIfNeeded()
    {
        while (_cache.Count > _maxFolders)
        {
            string? oldestKey = null;
            DateTime oldestAt = DateTime.MaxValue;
            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAccessedAt < oldestAt)
                {
                    oldestAt = kvp.Value.LastAccessedAt;
                    oldestKey = kvp.Key;
                }
            }
            if (oldestKey == null) break;

            var folder = oldestKey.Split('|')[0];
            _cache.Remove(oldestKey);
            _logger.Debug("{Prefix} evict folder={Folder}", LogPrefix, folder);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _syncService.EmailsSavedToFolder -= OnEmailsSavedToFolder;
        lock (_lock) { _cache.Clear(); }
    }
}
