using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using log4net;

namespace MaiX.Services.AI;

/// <summary>
/// AI 분석 결과를 JSON 파일로 로컬 캐시하는 서비스
/// 캐시 경로: %APPDATA%/MaiX/analysis_cache/{PageId}.json
/// </summary>
public class FileAnalysisCacheService
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(FileAnalysisCacheService));
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _memoryCache = new();

    public FileAnalysisCacheService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _cacheDir = Path.Combine(appData, "MaiX", "analysis_cache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// 분석 결과 저장 (메모리 + 파일)
    /// </summary>
    public async Task SaveAnalysisResultAsync(string pageId, string fileName, string result)
    {
        if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(result))
            return;

        try
        {
            var cache = await LoadOrCreateCacheAsync(pageId);
            cache[fileName] = result;
            _memoryCache[pageId] = cache;

            var filePath = GetCacheFilePath(pageId);
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);

            _log.Debug($"[AnalysisCache] 저장: PageId={ShortenId(pageId)}, File={fileName}, Length={result.Length}");
        }
        catch (Exception ex)
        {
            _log.Error($"[AnalysisCache] 저장 실패: {fileName}", ex);
        }
    }

    /// <summary>
    /// 단일 파일 분석 결과 로드
    /// </summary>
    public async Task<string?> LoadAnalysisResultAsync(string pageId, string fileName)
    {
        if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(fileName))
            return null;

        var cache = await LoadOrCreateCacheAsync(pageId);
        return cache.TryGetValue(fileName, out var result) ? result : null;
    }

    /// <summary>
    /// 페이지 전체 분석 결과 로드
    /// </summary>
    public async Task<Dictionary<string, string>> LoadAllAnalysisResultsAsync(string pageId)
    {
        if (string.IsNullOrEmpty(pageId))
            return new Dictionary<string, string>();

        return await LoadOrCreateCacheAsync(pageId);
    }

    private async Task<Dictionary<string, string>> LoadOrCreateCacheAsync(string pageId)
    {
        if (_memoryCache.TryGetValue(pageId, out var cached))
            return cached;

        var filePath = GetCacheFilePath(pageId);
        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    _memoryCache[pageId] = dict;
                    return dict;
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"[AnalysisCache] 캐시 파일 읽기 실패, 새로 생성: {filePath}", ex);
            }
        }

        var newCache = new Dictionary<string, string>();
        _memoryCache[pageId] = newCache;
        return newCache;
    }

    private string GetCacheFilePath(string pageId)
    {
        // PageId에 특수문자 포함 가능 → 파일명 안전 변환
        var safeId = string.Join("_", pageId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDir, $"{safeId}.json");
    }

    private static string ShortenId(string id) =>
        id.Length > 20 ? id[..10] + "..." + id[^8..] : id;
}
