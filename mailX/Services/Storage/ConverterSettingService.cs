using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using mailX.Data;
using mailX.Models;
using mailX.Services.Converter;

namespace mailX.Services.Storage;

/// <summary>
/// 문서 변환기 설정 서비스 - DB에서 변환기 설정 관리
/// </summary>
public class ConverterSettingService
{
    private readonly MailXDbContext _dbContext;
    private readonly AttachmentProcessor _attachmentProcessor;
    private readonly ILogger _logger;

    public ConverterSettingService(MailXDbContext dbContext, AttachmentProcessor attachmentProcessor)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _attachmentProcessor = attachmentProcessor ?? throw new ArgumentNullException(nameof(attachmentProcessor));
        _logger = Log.ForContext<ConverterSettingService>();
    }

    /// <summary>
    /// 모든 변환기 설정 조회
    /// </summary>
    public async Task<List<ConverterSetting>> GetAllSettingsAsync()
    {
        return await _dbContext.ConverterSettings
            .OrderBy(s => s.Extension)
            .ToListAsync();
    }

    /// <summary>
    /// 특정 확장자의 설정 조회
    /// </summary>
    public async Task<ConverterSetting?> GetSettingAsync(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        var ext = extension.StartsWith(".") ? extension : $".{extension}";
        return await _dbContext.ConverterSettings
            .FirstOrDefaultAsync(s => s.Extension == ext);
    }

    /// <summary>
    /// 변환기 설정 저장 또는 업데이트
    /// </summary>
    public async Task<ConverterSetting> SaveSettingAsync(string extension, string converterName)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("확장자는 필수입니다.", nameof(extension));

        if (string.IsNullOrWhiteSpace(converterName))
            throw new ArgumentException("변환기 이름은 필수입니다.", nameof(converterName));

        var ext = extension.StartsWith(".") ? extension : $".{extension}";

        var existing = await _dbContext.ConverterSettings
            .FirstOrDefaultAsync(s => s.Extension == ext);

        if (existing != null)
        {
            existing.SelectedConverter = converterName;
            existing.UpdatedAt = DateTime.UtcNow;
            _dbContext.ConverterSettings.Update(existing);
        }
        else
        {
            existing = new ConverterSetting
            {
                Extension = ext,
                SelectedConverter = converterName,
                UpdatedAt = DateTime.UtcNow,
                IsEnabled = true
            };
            await _dbContext.ConverterSettings.AddAsync(existing);
        }

        await _dbContext.SaveChangesAsync();

        // AttachmentProcessor에도 적용
        _attachmentProcessor.SelectConverter(ext, converterName);

        _logger.Information("변환기 설정 저장: {Extension} → {Converter}", ext, converterName);
        return existing;
    }

    /// <summary>
    /// 변환기 설정 삭제 (기본값 사용으로 복원)
    /// </summary>
    public async Task<bool> DeleteSettingAsync(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var ext = extension.StartsWith(".") ? extension : $".{extension}";
        var setting = await _dbContext.ConverterSettings
            .FirstOrDefaultAsync(s => s.Extension == ext);

        if (setting == null)
            return false;

        _dbContext.ConverterSettings.Remove(setting);
        await _dbContext.SaveChangesAsync();

        _logger.Information("변환기 설정 삭제: {Extension}", ext);
        return true;
    }

    /// <summary>
    /// DB에서 설정을 불러와 AttachmentProcessor에 적용
    /// </summary>
    public async Task LoadSettingsToProcessorAsync()
    {
        var settings = await _dbContext.ConverterSettings
            .Where(s => s.IsEnabled)
            .ToListAsync();

        var settingsDict = settings.ToDictionary(
            s => s.Extension,
            s => s.SelectedConverter,
            StringComparer.OrdinalIgnoreCase);

        _attachmentProcessor.LoadSelectedConverters(settingsDict);

        _logger.Information("변환기 설정 로드 완료: {Count}개", settings.Count);
    }

    /// <summary>
    /// AttachmentProcessor의 현재 설정을 DB에 저장
    /// </summary>
    public async Task SaveSettingsFromProcessorAsync()
    {
        var currentSettings = _attachmentProcessor.ExportSelectedConverters();

        foreach (var kvp in currentSettings)
        {
            await SaveSettingAsync(kvp.Key, kvp.Value);
        }

        _logger.Information("변환기 설정 일괄 저장 완료: {Count}개", currentSettings.Count);
    }

    /// <summary>
    /// 확장자별 사용 가능한 변환기 목록과 현재 선택 정보 조회
    /// </summary>
    public async Task<Dictionary<string, ConverterSelectionInfo>> GetConverterSelectionInfoAsync()
    {
        var result = new Dictionary<string, ConverterSelectionInfo>(StringComparer.OrdinalIgnoreCase);
        var allInfo = _attachmentProcessor.GetAllConverterInfo();
        var dbSettings = await GetAllSettingsAsync();
        var dbSettingsDict = dbSettings.ToDictionary(s => s.Extension, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in allInfo)
        {
            var selectedConverter = dbSettingsDict.TryGetValue(kvp.Key, out var dbSetting)
                ? dbSetting.SelectedConverter
                : null;

            result[kvp.Key] = new ConverterSelectionInfo
            {
                Extension = kvp.Key,
                AvailableConverters = kvp.Value.Select(c => new ConverterOption
                {
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    Priority = c.Priority,
                    IsAvailable = c.IsAvailable,
                    IsSelected = !string.IsNullOrEmpty(selectedConverter)
                        ? c.Name == selectedConverter
                        : c.IsSelected
                }).ToList(),
                SelectedConverterName = selectedConverter
            };
        }

        return result;
    }

    /// <summary>
    /// 모든 설정 초기화 (기본값으로 복원)
    /// </summary>
    public async Task ResetAllSettingsAsync()
    {
        var allSettings = await _dbContext.ConverterSettings.ToListAsync();
        _dbContext.ConverterSettings.RemoveRange(allSettings);
        await _dbContext.SaveChangesAsync();

        _logger.Information("모든 변환기 설정 초기화 완료");
    }
}

/// <summary>
/// 확장자별 변환기 선택 정보
/// </summary>
public class ConverterSelectionInfo
{
    /// <summary>
    /// 파일 확장자
    /// </summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// 사용 가능한 변환기 목록
    /// </summary>
    public List<ConverterOption> AvailableConverters { get; set; } = new();

    /// <summary>
    /// 현재 선택된 변환기 이름 (null이면 기본값)
    /// </summary>
    public string? SelectedConverterName { get; set; }
}

/// <summary>
/// 변환기 옵션
/// </summary>
public class ConverterOption
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsSelected { get; set; }
}
