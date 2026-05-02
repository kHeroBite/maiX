using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.AI;

namespace mAIx.Services.Storage;

/// <summary>
/// 프롬프트 서비스 - 파일 우선 + DB Fallback 패턴 (P4-02)
/// 외부 파일(Resources/Prompts/*.txt) 수정이 DB보다 우선 적용됨
/// </summary>
public class PromptService
{
    private readonly mAIxDbContext _dbContext;
    private readonly AIService _aiService;
    private readonly PromptCacheService _promptCache;
    private readonly ILogger _logger;

    /// <summary>
    /// 변수 패턴 정규식 ({{variable}} 형식)
    /// </summary>
    private static readonly Regex VariablePattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public PromptService(mAIxDbContext dbContext, AIService aiService, PromptCacheService promptCache)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _promptCache = promptCache ?? throw new ArgumentNullException(nameof(promptCache));
        _logger = Log.ForContext<PromptService>();
    }

    /// <summary>
    /// 프롬프트 키로 조회
    /// </summary>
    /// <param name="promptKey">프롬프트 고유 키</param>
    /// <returns>프롬프트 또는 null</returns>
    public async Task<Prompt?> GetPromptAsync(string promptKey)
    {
        if (string.IsNullOrWhiteSpace(promptKey))
        {
            _logger.Warning("프롬프트 키가 비어있음");
            return null;
        }

        // P4-02: 파일 우선 조회 — 외부 파일 수정이 DB보다 우선 적용됨 (Resources/Prompts/{key}.txt)
        var fileTemplate = await _promptCache.GetTemplateAsync(promptKey).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(fileTemplate))
        {
            _logger.Debug("파일에서 프롬프트 로드: {Key}", promptKey);
            return new Prompt
            {
                PromptKey = promptKey,
                Name = promptKey,
                Template = fileTemplate,
                IsSystem = true
            };
        }

        // Fallback: DB에서 조회
        var prompt = await _dbContext.Prompts
            .FirstOrDefaultAsync(p => p.PromptKey == promptKey).ConfigureAwait(false);

        if (prompt == null)
        {
            _logger.Debug("프롬프트를 찾을 수 없음 (파일+DB 모두 없음): {Key}", promptKey);
        }

        return prompt;
    }

    /// <summary>
    /// 전체 프롬프트 목록 조회
    /// </summary>
    /// <returns>프롬프트 목록</returns>
    public async Task<List<Prompt>> GetAllPromptsAsync()
    {
        return await _dbContext.Prompts
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 카테고리별 프롬프트 조회
    /// </summary>
    /// <param name="category">카테고리 (analysis, extraction, generation 등)</param>
    /// <returns>프롬프트 목록</returns>
    public async Task<List<Prompt>> GetPromptsByCategoryAsync(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return await GetAllPromptsAsync().ConfigureAwait(false);
        }

        return await _dbContext.Prompts
            .Where(p => p.Category == category)
            .OrderBy(p => p.Name)
            .ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 프롬프트 저장 또는 업데이트
    /// </summary>
    /// <param name="prompt">프롬프트 객체</param>
    /// <returns>저장된 프롬프트</returns>
    public async Task<Prompt> SavePromptAsync(Prompt prompt)
    {
        if (prompt == null)
            throw new ArgumentNullException(nameof(prompt));

        if (string.IsNullOrWhiteSpace(prompt.PromptKey))
            throw new ArgumentException("프롬프트 키는 필수입니다.", nameof(prompt));

        if (string.IsNullOrWhiteSpace(prompt.Template))
            throw new ArgumentException("프롬프트 템플릿은 필수입니다.", nameof(prompt));

        var existing = await _dbContext.Prompts
            .FirstOrDefaultAsync(p => p.PromptKey == prompt.PromptKey).ConfigureAwait(false);

        if (existing != null)
        {
            // 시스템 프롬프트는 수정 불가
            if (existing.IsSystem && !prompt.IsSystem)
            {
                throw new InvalidOperationException($"시스템 프롬프트는 수정할 수 없습니다: {prompt.PromptKey}");
            }

            // 업데이트
            existing.Name = prompt.Name;
            existing.Template = prompt.Template;
            existing.Variables = prompt.Variables;
            existing.Category = prompt.Category;
            existing.IsEnabled = prompt.IsEnabled;

            _dbContext.Prompts.Update(existing);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);

            _logger.Information("프롬프트 업데이트 완료: {Key}", prompt.PromptKey);
            return existing;
        }
        else
        {
            // 신규 추가
            await _dbContext.Prompts.AddAsync(prompt).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);

            _logger.Information("프롬프트 생성 완료: {Key}", prompt.PromptKey);
            return prompt;
        }
    }

    /// <summary>
    /// 프롬프트 삭제
    /// </summary>
    /// <param name="promptKey">프롬프트 고유 키</param>
    /// <returns>삭제 성공 여부</returns>
    public async Task<bool> DeletePromptAsync(string promptKey)
    {
        if (string.IsNullOrWhiteSpace(promptKey))
        {
            _logger.Warning("삭제할 프롬프트 키가 비어있음");
            return false;
        }

        var prompt = await _dbContext.Prompts
            .FirstOrDefaultAsync(p => p.PromptKey == promptKey).ConfigureAwait(false);

        if (prompt == null)
        {
            _logger.Warning("삭제할 프롬프트를 찾을 수 없음: {Key}", promptKey);
            return false;
        }

        // 시스템 프롬프트는 삭제 불가
        if (prompt.IsSystem)
        {
            _logger.Warning("시스템 프롬프트는 삭제할 수 없음: {Key}", promptKey);
            throw new InvalidOperationException($"시스템 프롬프트는 삭제할 수 없습니다: {promptKey}");
        }

        _dbContext.Prompts.Remove(prompt);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _logger.Information("프롬프트 삭제 완료: {Key}", promptKey);
        return true;
    }

    /// <summary>
    /// 프롬프트 렌더링 - 변수 치환
    /// </summary>
    /// <param name="promptKey">프롬프트 고유 키</param>
    /// <param name="variables">치환 변수 딕셔너리</param>
    /// <returns>렌더링된 프롬프트 문자열</returns>
    public async Task<string?> RenderPromptAsync(string promptKey, Dictionary<string, string> variables)
    {
        var prompt = await GetPromptAsync(promptKey).ConfigureAwait(false);
        if (prompt == null)
        {
            _logger.Warning("렌더링할 프롬프트를 찾을 수 없음: {Key}", promptKey);
            return null;
        }

        if (!prompt.IsEnabled)
        {
            _logger.Warning("비활성화된 프롬프트: {Key}", promptKey);
            return null;
        }

        return RenderTemplate(prompt.Template, variables);
    }

    /// <summary>
    /// 템플릿 렌더링 (내부 메서드)
    /// </summary>
    /// <param name="template">템플릿 문자열</param>
    /// <param name="variables">치환 변수 딕셔너리</param>
    /// <returns>렌더링된 문자열</returns>
    private string RenderTemplate(string template, Dictionary<string, string>? variables)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        if (variables == null || variables.Count == 0)
            return template;

        return VariablePattern.Replace(template, match =>
        {
            var variableName = match.Groups[1].Value;
            return variables.TryGetValue(variableName, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// 프롬프트 테스트 실행
    /// </summary>
    /// <param name="promptKey">프롬프트 고유 키</param>
    /// <param name="inputData">입력 데이터 (JSON 형식)</param>
    /// <param name="provider">AI Provider 이름 (null이면 현재 Provider 사용)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>테스트 이력 객체</returns>
    public async Task<PromptTestHistory?> TestPromptAsync(
        string promptKey,
        Dictionary<string, string> inputData,
        string? provider = null,
        CancellationToken ct = default)
    {
        var prompt = await GetPromptAsync(promptKey).ConfigureAwait(false);
        if (prompt == null)
        {
            _logger.Warning("테스트할 프롬프트를 찾을 수 없음: {Key}", promptKey);
            return null;
        }

        // 프롬프트 렌더링
        var renderedPrompt = RenderTemplate(prompt.Template, inputData);

        // 실행 시간 측정
        var stopwatch = Stopwatch.StartNew();

        string result;
        string usedProvider;

        try
        {
            if (!string.IsNullOrEmpty(provider))
            {
                result = await _aiService.CompleteWithProviderAsync(provider, renderedPrompt, ct).ConfigureAwait(false);
                usedProvider = provider;
            }
            else
            {
                result = await _aiService.CompleteAsync(renderedPrompt, ct).ConfigureAwait(false);
                usedProvider = _aiService.CurrentProviderName;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "프롬프트 테스트 실행 실패: {Key}", promptKey);
            result = $"ERROR: {ex.Message}";
            usedProvider = provider ?? _aiService.CurrentProviderName;
        }

        stopwatch.Stop();

        // 입력 데이터 JSON 직렬화
        var inputJson = System.Text.Json.JsonSerializer.Serialize(inputData);

        // 테스트 이력 저장
        var history = new PromptTestHistory
        {
            PromptId = prompt.Id,
            InputData = inputJson,
            OutputResult = result,
            Provider = usedProvider,
            ExecutionTime = stopwatch.ElapsedMilliseconds,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.PromptTestHistories.AddAsync(history, ct).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.Information(
            "프롬프트 테스트 완료: {Key}, Provider: {Provider}, 실행시간: {Time}ms",
            promptKey, usedProvider, stopwatch.ElapsedMilliseconds);

        return history;
    }

    /// <summary>
    /// 테스트 이력 조회
    /// </summary>
    /// <param name="promptId">프롬프트 ID</param>
    /// <param name="limit">최대 조회 개수</param>
    /// <returns>테스트 이력 목록</returns>
    public async Task<List<PromptTestHistory>> GetTestHistoryAsync(int promptId, int limit = 10)
    {
        return await _dbContext.PromptTestHistories
            .Where(h => h.PromptId == promptId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(limit)
            .ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 프롬프트 키로 테스트 이력 조회
    /// </summary>
    /// <param name="promptKey">프롬프트 고유 키</param>
    /// <param name="limit">최대 조회 개수</param>
    /// <returns>테스트 이력 목록</returns>
    public async Task<List<PromptTestHistory>> GetTestHistoryByKeyAsync(string promptKey, int limit = 10)
    {
        var prompt = await GetPromptAsync(promptKey).ConfigureAwait(false);
        if (prompt == null)
        {
            return new List<PromptTestHistory>();
        }

        return await GetTestHistoryAsync(prompt.Id, limit).ConfigureAwait(false);
    }

    /// <summary>
    /// 활성화된 프롬프트만 조회
    /// </summary>
    /// <returns>활성화된 프롬프트 목록</returns>
    public async Task<List<Prompt>> GetEnabledPromptsAsync()
    {
        return await _dbContext.Prompts
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 프롬프트 템플릿에서 사용된 변수 목록 추출
    /// </summary>
    /// <param name="template">템플릿 문자열</param>
    /// <returns>변수 이름 목록</returns>
    public static List<string> ExtractVariables(string template)
    {
        if (string.IsNullOrEmpty(template))
            return new List<string>();

        return VariablePattern.Matches(template)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }
}
