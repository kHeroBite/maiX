using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NLog;

namespace mAIx.Controls.Teams;

/// <summary>
/// 채널 파일 탭 UserControl — 파일 목록 + 업로드 + 드래그앤드롭
/// </summary>
public partial class ChannelFilesControl : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public ChannelFilesControl()
    {
        InitializeComponent();
    }

    /// <summary>파일 업로드 버튼 클릭</summary>
    private void UploadFileButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("파일 업로드 버튼 클릭");
        ExecuteCommand("UploadFileCommand", null);
    }

    /// <summary>새 폴더 버튼 클릭</summary>
    private void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("새 폴더 버튼 클릭");
        ExecuteCommand("CreateFolderCommand", null);
    }

    /// <summary>파일 항목 클릭 — 브라우저로 열기</summary>
    private void ChannelFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: { } fileItem })
        {
            _log.Debug("파일 클릭: {File}", fileItem);
            ExecuteCommand("OpenFileCommand", fileItem);
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

    /// <summary>DataContext의 커맨드를 이름으로 실행 (ChannelFilesViewModel 타입 의존성 제거)</summary>
    private void ExecuteCommand(string commandName, object? parameter)
    {
        if (DataContext is not { } vm) return;
        var cmdProp = vm.GetType().GetProperty(commandName);
        if (cmdProp?.GetValue(vm) is ICommand cmd && cmd.CanExecute(parameter))
            cmd.Execute(parameter);
    }
}
