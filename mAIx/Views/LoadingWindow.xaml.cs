using System.Windows;

namespace mAIx.Views;

/// <summary>
/// 로딩 화면 - 투명 배경 스피너만 표시
/// </summary>
public partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 상태 메시지 업데이트 (더 이상 사용하지 않음)
    /// </summary>
    public void UpdateStatus(string message)
    {
        // 스피너만 표시하므로 메시지 업데이트 불필요
    }

    /// <summary>
    /// 상세 메시지 업데이트 (더 이상 사용하지 않음)
    /// </summary>
    public void UpdateDetail(string detail)
    {
        // 스피너만 표시하므로 메시지 업데이트 불필요
    }
}
