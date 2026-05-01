using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using mAIx.Services.Graph;
using mAIx.ViewModels;
using Serilog;

namespace mAIx.Controls
{
    /// <summary>
    /// 파일 미리보기 패널 — 이미지, PDF, Office 문서 미리보기 + 파일 정보
    /// </summary>
    public partial class FilePreviewPanel : UserControl
    {
        private static readonly ILogger _log = Log.ForContext<FilePreviewPanel>();

        private DriveItemViewModel? _currentItem;
        private GraphOneDriveService? _oneDriveService;

        /// <summary>
        /// 닫기 버튼 클릭 이벤트
        /// </summary>
        public event EventHandler? CloseRequested;

        /// <summary>
        /// 다운로드 요청 이벤트
        /// </summary>
        public event EventHandler<DriveItemViewModel>? DownloadRequested;

        /// <summary>
        /// 공유 요청 이벤트
        /// </summary>
        public event EventHandler<DriveItemViewModel>? ShareRequested;

        /// <summary>
        /// 버전 히스토리 요청 이벤트
        /// </summary>
        public event EventHandler<DriveItemViewModel>? VersionHistoryRequested;

        public FilePreviewPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// OneDrive 서비스 설정
        /// </summary>
        public void SetService(GraphOneDriveService service)
        {
            _oneDriveService = service;
        }

        /// <summary>
        /// 파일 미리보기 로드
        /// </summary>
        public async Task LoadPreview(DriveItemViewModel item)
        {
            if (item == null) return;

            _currentItem = item;
            FileNameText.Text = item.Name;
            FileSizeText.Text = item.SizeDisplay;
            FileModifiedText.Text = item.LastModifiedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "-";
            FileOwnerText.Text = item.OwnerDisplayName;

            // 모든 컨테이너 숨기기
            ImagePreviewContainer.Visibility = Visibility.Collapsed;
            WebPreviewContainer.Visibility = Visibility.Collapsed;
            NoPreviewContainer.Visibility = Visibility.Collapsed;
            LoadingContainer.Visibility = Visibility.Visible;

            try
            {
                var extension = Path.GetExtension(item.Name)?.ToLowerInvariant();

                switch (extension)
                {
                    case ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp":
                        await LoadImagePreviewAsync(item);
                        break;
                    case ".pdf":
                        await LoadWebPreviewAsync(item);
                        break;
                    case ".docx" or ".doc" or ".xlsx" or ".xls" or ".pptx" or ".ppt":
                        await LoadOfficePreviewAsync(item);
                        break;
                    default:
                        ShowNoPreview();
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "미리보기 로드 실패: {FileName}", item.Name);
                ShowNoPreview();
            }
        }

        private async System.Threading.Tasks.Task LoadImagePreviewAsync(DriveItemViewModel item)
        {
            try
            {
                if (_oneDriveService == null) { ShowNoPreview(); return; }

                var thumbnailUrl = await _oneDriveService.GetThumbnailUrlAsync(item.Id, "large");
                if (!string.IsNullOrEmpty(thumbnailUrl))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(thumbnailUrl);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    PreviewImage.Source = bitmap;
                    ImagePreviewContainer.Visibility = Visibility.Visible;
                }
                else
                {
                    ShowNoPreview();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("이미지 미리보기 실패: {Error}", ex.Message);
                ShowNoPreview();
            }
            finally
            {
                LoadingContainer.Visibility = Visibility.Collapsed;
            }
        }

        private async System.Threading.Tasks.Task LoadWebPreviewAsync(DriveItemViewModel item)
        {
            try
            {
                if (_oneDriveService == null) { ShowNoPreview(); return; }

                var previewUrl = await _oneDriveService.GetPreviewUrlAsync(item.Id);
                if (!string.IsNullOrEmpty(previewUrl))
                {
                    PreviewWebBrowser.Navigate(new Uri(previewUrl));
                    WebPreviewContainer.Visibility = Visibility.Visible;
                }
                else
                {
                    ShowNoPreview();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("PDF 미리보기 실패: {Error}", ex.Message);
                ShowNoPreview();
            }
            finally
            {
                LoadingContainer.Visibility = Visibility.Collapsed;
            }
        }

        private async System.Threading.Tasks.Task LoadOfficePreviewAsync(DriveItemViewModel item)
        {
            try
            {
                if (_oneDriveService == null) { ShowNoPreview(); return; }

                // OneDrive 임베드 URL 사용
                var previewUrl = await _oneDriveService.GetPreviewUrlAsync(item.Id);
                if (!string.IsNullOrEmpty(previewUrl))
                {
                    PreviewWebBrowser.Navigate(new Uri(previewUrl));
                    WebPreviewContainer.Visibility = Visibility.Visible;
                }
                else if (!string.IsNullOrEmpty(item.WebUrl))
                {
                    // WebUrl fallback
                    PreviewWebBrowser.Navigate(new Uri(item.WebUrl));
                    WebPreviewContainer.Visibility = Visibility.Visible;
                }
                else
                {
                    ShowNoPreview();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Office 미리보기 실패: {Error}", ex.Message);
                ShowNoPreview();
            }
            finally
            {
                LoadingContainer.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowNoPreview()
        {
            LoadingContainer.Visibility = Visibility.Collapsed;
            NoPreviewContainer.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 미리보기 초기화
        /// </summary>
        public void ClearPreview()
        {
            _currentItem = null;
            FileNameText.Text = string.Empty;
            FileSizeText.Text = string.Empty;
            FileModifiedText.Text = string.Empty;
            FileOwnerText.Text = string.Empty;
            PreviewImage.Source = null;
            ImagePreviewContainer.Visibility = Visibility.Collapsed;
            WebPreviewContainer.Visibility = Visibility.Collapsed;
            NoPreviewContainer.Visibility = Visibility.Collapsed;
            LoadingContainer.Visibility = Visibility.Collapsed;
        }

        private void ClosePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            ClearPreview();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentItem != null)
                DownloadRequested?.Invoke(this, _currentItem);
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentItem != null)
                ShareRequested?.Invoke(this, _currentItem);
        }

        private void VersionHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentItem != null)
                VersionHistoryRequested?.Invoke(this, _currentItem);
        }
    }
}
