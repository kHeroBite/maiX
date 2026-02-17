using CommunityToolkit.Mvvm.ComponentModel;

namespace MaiX.Models;

public partial class OneNoteAttachment : ObservableObject
{
    // 기본 속성
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string DataUrl { get; set; } = string.Empty;  // Graph API URL 또는 로컬 경로
    public string IconBase64 { get; set; } = string.Empty;  // Windows 시스템 아이콘 Base64 PNG

    [ObservableProperty]
    private string _localPath = string.Empty;

    // AI 분석 상태
    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _analysisResult = string.Empty;

    [ObservableProperty]
    private string _analysisStatus = string.Empty;  // "", "분석 중...", "완료", "실패"

    [ObservableProperty]
    private string _analysisSummary = string.Empty;

    // 계산 속성
    public bool HasAnalysis => !string.IsNullOrEmpty(AnalysisResult);

    public bool IsImage => Extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tiff" => true,
        _ => false
    };

    public bool IsDocument => Extension.ToLowerInvariant() switch
    {
        ".pdf" or ".docx" or ".xlsx" or ".hwp" or ".txt" or ".csv" or ".pptx" => true,
        _ => false
    };

    // ObservableProperty 변경 시 HasAnalysis도 알림
    partial void OnAnalysisResultChanged(string value)
    {
        OnPropertyChanged(nameof(HasAnalysis));
    }
}
