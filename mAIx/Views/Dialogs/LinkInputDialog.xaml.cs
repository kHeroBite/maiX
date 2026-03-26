using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace mAIx.Views.Dialogs;

/// <summary>
/// 링크 삽입 다이얼로그
/// </summary>
public partial class LinkInputDialog : FluentWindow
{
    /// <summary>
    /// 입력된 URL
    /// </summary>
    public string? Url { get; private set; }

    /// <summary>
    /// 표시 텍스트
    /// </summary>
    public string? DisplayText { get; private set; }

    public LinkInputDialog()
    {
        InitializeComponent();

        // ESC 키로 창 닫기
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                InsertLink();
            }
        };

        // URL 텍스트박스에 포커스
        Loaded += (s, e) => UrlTextBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        InsertLink();
    }

    private void InsertLink()
    {
        var url = UrlTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            System.Windows.MessageBox.Show("URL을 입력해주세요.", "알림", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            UrlTextBox.Focus();
            return;
        }

        // http(s) 프로토콜 없으면 추가
        if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("mailto:"))
        {
            url = "https://" + url;
        }

        Url = url;
        DisplayText = string.IsNullOrWhiteSpace(DisplayTextBox.Text) ? null : DisplayTextBox.Text.Trim();

        DialogResult = true;
        Close();
    }
}
