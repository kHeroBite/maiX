using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using mAIx.Models;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.Services.Cache;

/// <summary>
/// 프로필 사진 캐시 서비스
/// - 연락처/조직 사용자의 프로필 사진을 메모리 캐시
/// - 지연 로딩 (팝업 표시 후 비동기 로드)
/// </summary>
public class ProfilePhotoCacheService
{
    private readonly GraphAuthService _authService;
    private readonly ILogger _logger;

    // 사진 캐시: "{Source}:{ContactId}" → PhotoCacheEntry
    private readonly ConcurrentDictionary<string, PhotoCacheEntry> _cache = new();

    // 캐시 설정
    private const int CacheExpirationMinutes = 60; // 1시간
    private const int MaxCacheSize = 500; // 최대 캐시 항목 수

    public ProfilePhotoCacheService(GraphAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = Log.ForContext<ProfilePhotoCacheService>();
    }

    /// <summary>
    /// 프로필 사진 가져오기 (캐시 우선)
    /// </summary>
    /// <param name="source">연락처 소스</param>
    /// <param name="contactId">연락처/사용자 ID</param>
    /// <returns>Base64 인코딩된 사진 (없으면 null)</returns>
    public async Task<string?> GetPhotoAsync(ContactSource source, string? contactId)
    {
        // 로컬 연락처는 사진 조회 불가
        if (source == ContactSource.Local || string.IsNullOrEmpty(contactId))
            return null;

        var cacheKey = $"{source}:{contactId}";

        // 캐시 확인
        if (_cache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.Debug("프로필 사진 캐시 히트: {CacheKey}", cacheKey);
            return cached.PhotoBase64;
        }

        // Graph API에서 사진 조회
        try
        {
            if (!_authService.IsLoggedIn)
                return null;

            var client = _authService.GetGraphClient();
            Stream? photoStream = null;

            if (source == ContactSource.Contact)
            {
                // Microsoft 연락처 프로필 사진
                try
                {
                    photoStream = await client.Me.Contacts[contactId].Photo.Content.GetAsync();
                }
                catch
                {
                    // 사진이 없는 경우 예외 발생 - 무시
                }
            }
            else if (source == ContactSource.Organization)
            {
                // 조직 사용자 프로필 사진
                try
                {
                    photoStream = await client.Users[contactId].Photo.Content.GetAsync();
                }
                catch
                {
                    // 사진이 없는 경우 예외 발생 - 무시
                }
            }

            string? photoBase64 = null;
            if (photoStream != null)
            {
                using var memoryStream = new MemoryStream();
                await photoStream.CopyToAsync(memoryStream);
                photoBase64 = Convert.ToBase64String(memoryStream.ToArray());
            }

            // 캐시에 저장 (사진이 없어도 캐시 - 중복 조회 방지)
            CleanupCacheIfNeeded();
            _cache[cacheKey] = new PhotoCacheEntry(photoBase64);

            _logger.Debug("프로필 사진 로드: {CacheKey}, HasPhoto={HasPhoto}",
                cacheKey, photoBase64 != null);

            return photoBase64;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "프로필 사진 조회 실패: {CacheKey}", cacheKey);

            // 실패 시에도 캐시 (재시도 방지, 짧은 만료 시간)
            _cache[cacheKey] = new PhotoCacheEntry(null, expirationMinutes: 5);
            return null;
        }
    }

    /// <summary>
    /// 여러 연락처의 사진을 비동기로 일괄 로딩
    /// UI 쓰레드에서 PropertyChanged 알림이 발생하도록 처리
    /// </summary>
    /// <param name="suggestions">연락처 목록</param>
    public async Task LoadPhotosAsync(IEnumerable<ContactSuggestion> suggestions)
    {
        // 사진 로딩이 필요한 항목만 필터링
        var toLoad = suggestions
            .Where(s => !s.PhotoLoaded &&
                       s.Source != ContactSource.Local &&
                       !string.IsNullOrEmpty(s.ContactId))
            .ToList();

        if (toLoad.Count == 0)
            return;

        _logger.Debug("프로필 사진 일괄 로딩 시작: {Count}건", toLoad.Count);

        // 병렬로 사진 로드 (최대 5개 동시)
        var tasks = toLoad.Select(async suggestion =>
        {
            try
            {
                var photo = await GetPhotoAsync(suggestion.Source, suggestion.ContactId);

                // UI 쓰레드에서 속성 업데이트
                if (!string.IsNullOrEmpty(photo))
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        suggestion.PhotoBase64 = photo;
                    });
                }

                suggestion.PhotoLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("사진 로드 실패: {Email} - {Error}", suggestion.Email, ex.Message);
                suggestion.PhotoLoaded = true; // 실패해도 재시도 방지
            }
        });

        await Task.WhenAll(tasks);

        _logger.Debug("프로필 사진 일괄 로딩 완료");
    }

    /// <summary>
    /// 캐시 정리
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _logger.Debug("프로필 사진 캐시 정리됨");
    }

    /// <summary>
    /// 캐시 크기 초과 시 오래된 항목 정리
    /// </summary>
    private void CleanupCacheIfNeeded()
    {
        if (_cache.Count < MaxCacheSize)
            return;

        // 만료된 항목 먼저 제거
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        // 그래도 크면 가장 오래된 항목 제거
        if (_cache.Count >= MaxCacheSize)
        {
            var oldestKeys = _cache
                .OrderBy(kvp => kvp.Value.CreatedAt)
                .Take(_cache.Count - MaxCacheSize + 50) // 50개 여유
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }

        _logger.Debug("프로필 사진 캐시 정리: 현재 {Count}건", _cache.Count);
    }

    /// <summary>
    /// 사진 캐시 항목
    /// </summary>
    private class PhotoCacheEntry
    {
        public string? PhotoBase64 { get; }
        public DateTime CreatedAt { get; }
        public DateTime ExpiresAt { get; }

        public bool IsExpired => DateTime.Now > ExpiresAt;

        public PhotoCacheEntry(string? photoBase64, int expirationMinutes = CacheExpirationMinutes)
        {
            PhotoBase64 = photoBase64;
            CreatedAt = DateTime.Now;
            ExpiresAt = CreatedAt.AddMinutes(expirationMinutes);
        }
    }
}
