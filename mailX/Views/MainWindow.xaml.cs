using System.Windows;
using Wpf.Ui.Controls;
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
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 폴더 목록 초기 로드
        await _viewModel.LoadFoldersCommand.ExecuteAsync(null);
    }
}
