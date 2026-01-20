using System.Windows;
using System.Windows.Input;

namespace mailX.Views.Dialogs;

/// <summary>
/// 메일 작성 창 닫기 확인 대화상자
/// </summary>
public partial class ComposeCloseDialog : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>
    /// 사용자 선택 결과
    /// </summary>
    public ComposeCloseResult Result { get; private set; } = ComposeCloseResult.Cancel;

    public ComposeCloseDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        Result = ComposeCloseResult.Delete;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 보관 버튼 클릭
    /// </summary>
    private void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        Result = ComposeCloseResult.SaveDraft;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 취소 버튼 클릭
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = ComposeCloseResult.Cancel;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 키 입력 처리 (ESC = 취소)
    /// </summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = ComposeCloseResult.Cancel;
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}

/// <summary>
/// 메일 작성 창 닫기 확인 결과
/// </summary>
public enum ComposeCloseResult
{
    /// <summary>
    /// 취소 - 창 닫기 취소
    /// </summary>
    Cancel,

    /// <summary>
    /// 삭제 - 저장 없이 창 닫기
    /// </summary>
    Delete,

    /// <summary>
    /// 보관 - 임시보관함에 저장 후 창 닫기
    /// </summary>
    SaveDraft
}
