using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using mailX.Utils;
using mailX.ViewModels;

namespace mailX.Views;

/// <summary>
/// 로그인 윈도우 - Microsoft 365 계정 인증
/// MARS 스타일 스플릿 레이아웃
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        Log4.Debug("LoginWindow 생성자 시작");
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
        Log4.Debug("InitializeComponent 완료");

        Loaded += LoginWindow_Loaded;

        // 로그인 성공 시 DialogResult 설정
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        Log4.Debug("LoginWindow 생성자 완료");
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Log4.Debug("LoginWindow_Loaded 시작");
        // 저장된 계정 목록 로드
        await _viewModel.LoadSavedAccountsAsync();
        Log4.Debug("LoginWindow_Loaded 완료");
    }

    /// <summary>
    /// 로그인 성공 시 호출
    /// </summary>
    private void OnLoginSucceeded()
    {
        Log4.Debug("OnLoginSucceeded 호출됨");
        Log4.Debug("DialogResult = true 설정 직전");
        DialogResult = true;
        Log4.Debug("DialogResult = true 설정 완료");
        Log4.Debug("Close() 호출 직전");
        Close();
        Log4.Debug("Close() 호출 완료");
    }

    /// <summary>
    /// 창 드래그 이동 (타이틀바 없는 창에서)
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// 닫기 버튼 클릭
    /// </summary>
    private void ButtonClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 닫기 버튼 마우스 진입
    /// </summary>
    private void ButtonClose_MouseEnter(object sender, MouseEventArgs e)
    {
        buttonClose.Foreground = new SolidColorBrush(Color.FromRgb(40, 80, 50)); // #285032
    }

    /// <summary>
    /// 닫기 버튼 마우스 이탈
    /// </summary>
    private void ButtonClose_MouseLeave(object sender, MouseEventArgs e)
    {
        buttonClose.Foreground = new SolidColorBrush(Colors.DarkGray);
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
