using System;
using System.IO;
using System.Windows;
using mAIx.Services.Graph;
using mAIx.ViewModels;
using mAIx.Views.Dialogs;
using Microsoft.Win32;
using Serilog;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — OneDrive 강화 헬퍼 (Phase 5)
    /// 공유/버전 다이얼로그, 미리보기, 청크 업로드 헬퍼 메서드
    /// </summary>
    public partial class MainWindow
    {
        private static readonly ILogger _oneDriveLog = Log.ForContext("SourceContext", "MainWindow.OneDrive");

        /// <summary>
        /// 공유 다이얼로그 열기 (ShareDialog)
        /// </summary>
        private void OpenShareDialogFor(DriveItemViewModel? item)
        {
            if (item == null) return;

            try
            {
                var oneDriveService = ((App)Application.Current).GetService<GraphOneDriveService>()!;
                var dialog = new ShareDialog(oneDriveService, item.Id, item.Name)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _oneDriveLog.Error(ex, "공유 다이얼로그 열기 실패");
            }
        }

        /// <summary>
        /// 버전 히스토리 다이얼로그 열기 (VersionHistoryDialog)
        /// </summary>
        private void OpenVersionHistoryDialogFor(DriveItemViewModel? item)
        {
            if (item == null || item.IsFolder) return;

            try
            {
                var oneDriveService = ((App)Application.Current).GetService<GraphOneDriveService>()!;
                var dialog = new VersionHistoryDialog(oneDriveService, item.Id, item.Name)
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _oneDriveLog.Error(ex, "버전 히스토리 다이얼로그 열기 실패");
            }
        }

        /// <summary>
        /// 대용량 파일 업로드 (OpenFileDialog → 청크 업로드)
        /// </summary>
        private async void OneDriveUploadLargeFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "업로드할 파일 선택",
                Filter = "모든 파일 (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                if (_oneDriveViewModel == null) return;

                var filePath = openFileDialog.FileName;
                var fileInfo = new FileInfo(filePath);

                _oneDriveLog.Information("파일 업로드 시작: {FileName} ({Size})",
                    fileInfo.Name, GraphOneDriveService.FormatFileSize(fileInfo.Length));

                await _oneDriveViewModel.UploadLargeFileAsync(filePath);
            }
        }
    }
}
