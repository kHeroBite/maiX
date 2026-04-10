using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NLog;
using mAIx.ViewModels.Teams;

namespace mAIx.Controls.Teams;

/// <summary>
/// 채널 파일 탭 UserControl — 파일 목록 + 업로드 + 다운로드 + 드래그앤드롭 + 버전 기록
/// </summary>
public partial class ChannelFilesControl : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public ChannelFilesControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChannelFilesViewModel oldVm)
            oldVm.FilePickRequested -= PickFileAsync;

        if (e.NewValue is ChannelFilesViewModel newVm)
            newVm.FilePickRequested += PickFileAsync;
    }

    /// <summary>파일 선택 다이얼로그 표시</summary>
    private System.Threading.Tasks.Task<string?> PickFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "업로드할 파일 선택",
            Filter = "모든 파일 (*.*)|*.*",
            Multiselect = false
        };
        var result = dlg.ShowDialog() == true ? dlg.FileName : null;
        return System.Threading.Tasks.Task.FromResult(result);
    }

    /// <summary>파일 업로드 버튼 클릭</summary>
    private void UploadFileButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("파일 업로드 버튼 클릭");
        ExecuteCommand("UploadFileCommand", null);
    }

    /// <summary>다운로드 버튼 클릭 — 선택된 파일</summary>
    private void DownloadFileButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("파일 다운로드 버튼 클릭");
        if (DataContext is ChannelFilesViewModel vm)
            vm.DownloadFileCommand.Execute(vm.SelectedFile);
    }

    /// <summary>새로고침 버튼 클릭</summary>
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("파일 새로고침 버튼 클릭");
        ExecuteCommand("RefreshCommand", null);
    }

    /// <summary>버전 기록 버튼 클릭</summary>
    private void VersionHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("버전 기록 버튼 클릭");
        if (DataContext is ChannelFilesViewModel vm)
            vm.ShowVersionsCommand.Execute(vm.SelectedFile);
    }

    /// <summary>파일 더블클릭 — 브라우저로 열기</summary>
    private void FilesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ChannelFilesViewModel vm && vm.SelectedFile != null)
        {
            _log.Debug("파일 더블클릭: {Name}", vm.SelectedFile.Name);
            vm.OpenFileCommand.Execute(vm.SelectedFile);
        }
    }

    /// <summary>드래그 오버 — 오버레이 표시</summary>
    private void FilesGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DragDropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>드래그 떠남 — 오버레이 숨김</summary>
    private void FilesGrid_DragLeave(object sender, DragEventArgs e)
    {
        DragDropOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>드롭 — 파일 업로드 실행</summary>
    private void FilesGrid_Drop(object sender, DragEventArgs e)
    {
        DragDropOverlay.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (var filePath in files)
        {
            _log.Info("드래그앤드롭 업로드: {Path}", filePath);
            ExecuteCommand("UploadFileFromPathCommand", filePath);
        }
    }

    /// <summary>DataContext의 커맨드를 이름으로 실행</summary>
    private void ExecuteCommand(string commandName, object? parameter)
    {
        if (DataContext is not { } vm) return;
        var cmdProp = vm.GetType().GetProperty(commandName);
        if (cmdProp?.GetValue(vm) is ICommand cmd && cmd.CanExecute(parameter))
            cmd.Execute(parameter);
    }
}
