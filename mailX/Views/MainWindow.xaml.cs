using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;
using mailX.Utils;
using mailX.ViewModels;

namespace mailX.Views;

/// <summary>
/// 메인 윈도우 - 3단 레이아웃 (폴더트리 | 메일리스트 | 본문+AI)
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        Log4.Debug("MainWindow 생성자 시작");
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // 타이틀바 설정
        TitleBar.CloseClicked += (_, _) =>
        {
            Log4.Debug("MainWindow 닫기 버튼 클릭됨");
            Close();
        };

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Log4.Debug("MainWindow 생성자 완료");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Log4.Debug("MainWindow_Loaded 시작");
        // 폴더 목록 초기 로드
        await _viewModel.LoadFoldersCommand.ExecuteAsync(null);
        Log4.Debug("MainWindow_Loaded 완료");
    }

    private void MainWindow_Closed(object? sender, System.EventArgs e)
    {
        Log4.Debug("MainWindow_Closed - 애플리케이션 종료");
        // OnExplicitShutdown 모드에서는 명시적으로 종료 호출 필요
        Application.Current.Shutdown();
    }
}
