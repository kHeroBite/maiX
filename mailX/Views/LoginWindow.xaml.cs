using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Wpf.Ui.Controls;
using mailX.ViewModels;

namespace mailX.Views;

/// <summary>
/// 로그인 윈도우 - Microsoft 365 계정 인증
/// </summary>
public partial class LoginWindow : FluentWindow
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        Loaded += LoginWindow_Loaded;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 저장된 계정 목록 로드
        await _viewModel.LoadSavedAccountsAsync();
    }

    /// <summary>
    /// 하이퍼링크 클릭 시 브라우저에서 열기
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
