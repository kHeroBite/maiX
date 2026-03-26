using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MaiX.Services.AI;

/// <summary>
/// 프롬프트 파일 캐시 서비스 — Singleton
/// Resources/Prompts/*.txt 파일을 메모리에 캐싱하여 반복 디스크 I/O 제거
/// </summary>
public class PromptCacheService
{
    private readonly ConcurrentDictionary<string, string> _캐시 = new();
    private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(PromptCacheService));

    /// <summary>
    /// 프롬프트 디렉토리 경로
    /// </summary>
    private static string 프롬프트경로 =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Prompts");

    /// <summary>
    /// 앱 시작 시 모든 프롬프트 .txt 파일을 메모리에 로드
    /// </summary>
    public async Task InitializeAsync()
    {
        var 디렉토리 = 프롬프트경로;
        if (!Directory.Exists(디렉토리))
        {
            _log.Warn($"프롬프트 디렉토리 없음: {디렉토리}");
            return;
        }

        var 파일목록 = Directory.GetFiles(디렉토리, "*.txt");
        _log.Info($"프롬프트 캐시 초기화 시작: {파일목록.Length}개 파일");

        foreach (var 파일 in 파일목록)
        {
            var 파일명 = Path.GetFileName(파일);
            var 내용 = await File.ReadAllTextAsync(파일);
            _캐시[파일명] = 내용;
        }

        _log.Info($"프롬프트 캐시 초기화 완료: {_캐시.Count}개 로드됨");
    }

    /// <summary>
    /// 캐시에서 프롬프트 템플릿 반환. 캐시 미스 시 디스크에서 읽어 캐시에 저장.
    /// </summary>
    public async Task<string> GetTemplateAsync(string 파일명)
    {
        if (_캐시.TryGetValue(파일명, out var 캐시값))
            return 캐시값;

        // 캐시 미스 — 디스크 fallback
        var 파일경로 = Path.Combine(프롬프트경로, 파일명);
        if (!File.Exists(파일경로))
            throw new FileNotFoundException($"프롬프트 템플릿 파일을 찾을 수 없습니다: {파일경로}");

        var 내용 = await File.ReadAllTextAsync(파일경로);
        _캐시[파일명] = 내용;
        _log.Debug($"프롬프트 캐시 미스 → 디스크 로드: {파일명}");
        return 내용;
    }

    /// <summary>
    /// 전체 프롬프트 리로드 — 캐시 클리어 후 디스크에서 다시 읽기
    /// </summary>
    public async Task<int> ReloadAllAsync()
    {
        _캐시.Clear();
        await InitializeAsync();
        _log.Info($"프롬프트 전체 리로드 완료: {_캐시.Count}개");
        return _캐시.Count;
    }

    /// <summary>
    /// 개별 프롬프트 리로드 — 특정 파일만 디스크에서 다시 읽기
    /// </summary>
    public async Task ReloadAsync(string 파일명)
    {
        var 파일경로 = Path.Combine(프롬프트경로, 파일명);
        if (!File.Exists(파일경로))
        {
            _log.Warn($"리로드 대상 파일 없음: {파일명}");
            return;
        }

        var 내용 = await File.ReadAllTextAsync(파일경로);
        _캐시[파일명] = 내용;
        _log.Debug($"프롬프트 개별 리로드: {파일명}");
    }

    /// <summary>
    /// 캐시된 프롬프트 파일명 목록 반환
    /// </summary>
    public IReadOnlyList<string> GetCachedFileNames() =>
        _캐시.Keys.ToList().AsReadOnly();
}
