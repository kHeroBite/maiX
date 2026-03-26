using System.Windows;
using System.Windows.Input;

namespace mAIx.Views.Dialogs;

/// <summary>
/// 메일 작성 창 닫기 확인 대화상자
/// "확인" 클릭 시 초안 삭제, "취소" 클릭 시 창 닫기 취소
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

        // 창 로드 시 확인 버튼에 포커스
        Loaded += (s, e) => ConfirmButton.Focus();
    }

    /// <summary>
    /// 확인 버튼 클릭 (삭제 확인)
    /// </summary>
    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Result = ComposeCloseResult.Delete;
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
    /// 보관 버튼 클릭 (임시보관함에 저장)
    /// </summary>
    private void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        Result = ComposeCloseResult.SaveDraft;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 키 입력 처리
    /// ESC = 취소, Enter/Space = 확인
    /// </summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // ESC = 취소
                Result = ComposeCloseResult.Cancel;
                DialogResult = false;
                Close();
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Space:
                // Enter/Space = 확인 (삭제)
                Result = ComposeCloseResult.Delete;
                DialogResult = true;
                Close();
                e.Handled = true;
                break;
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
    /// 보관 - 임시보관함에 저장 후 창 닫기 (현재 미사용)
    /// </summary>
    SaveDraft
}
