using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.Views.Dialogs
{
    /// <summary>
    /// 버전 히스토리 다이얼로그
    /// </summary>
    public partial class VersionHistoryDialog : Wpf.Ui.Controls.FluentWindow
    {
        private static readonly ILogger _log = Log.ForContext<VersionHistoryDialog>();
        private readonly GraphOneDriveService _oneDriveService;
        private readonly string _itemId;
        private readonly string _itemName;

        public ObservableCollection<VersionHistoryItem> Versions { get; } = new();

        public VersionHistoryDialog(GraphOneDriveService oneDriveService, string itemId, string itemName)
        {
            _oneDriveService = oneDriveService ?? throw new ArgumentNullException(nameof(oneDriveService));
            _itemId = itemId;
            _itemName = itemName;
            InitializeComponent();
            FileNameText.Text = itemName;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadVersionsAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[VersionHistoryDialog] Window_Loaded 실패");
            }
        }

        private async System.Threading.Tasks.Task LoadVersionsAsync()
        {
            try
            {
                var versions = await _oneDriveService.GetFileVersionsAsync(_itemId);

                Versions.Clear();
                bool isFirst = true;
                int versionNum = versions.Count;

                foreach (var ver in versions)
                {
                    Versions.Add(new VersionHistoryItem
                    {
                        VersionId = ver.Id ?? string.Empty,
                        VersionLabel = $"버전 {versionNum}",
                        ModifiedDate = ver.LastModifiedDateTime?.DateTime.ToString("yyyy-MM-dd HH:mm") ?? "-",
                        ModifiedBy = ver.LastModifiedBy?.User?.DisplayName ?? "알 수 없음",
                        SizeDisplay = GraphOneDriveService.FormatFileSize(ver.Size),
                        IsCurrent = isFirst,
                        IsCurrentVisibility = isFirst ? Visibility.Visible : Visibility.Collapsed,
                        RestoreVisibility = isFirst ? Visibility.Collapsed : Visibility.Visible
                    });

                    isFirst = false;
                    versionNum--;
                }

                VersionListView.ItemsSource = Versions;
                _log.Debug("버전 목록 로드: {Count}개", Versions.Count);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "버전 목록 로드 실패");
            }
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string versionId)
                {
                    var result = MessageBox.Show(
                        "이 버전으로 복원하시겠습니까?\n현재 버전은 새 버전으로 저장됩니다.",
                        "버전 복원",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            btn.IsEnabled = false;
                            var success = await _oneDriveService.RestoreVersionAsync(_itemId, versionId);
                            if (success)
                            {
                                _log.Information("버전 복원 완료: {VersionId}", versionId);
                                await LoadVersionsAsync();
                            }
                            else
                            {
                                MessageBox.Show("버전 복원에 실패했습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, "버전 복원 실패");
                            MessageBox.Show($"복원 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            btn.IsEnabled = true;
                        }
                    }
                }
            }
            catch (Exception exOuter)
            {
                _log.Error(exOuter, "[VersionHistoryDialog] RestoreButton_Click 실패");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// 버전 히스토리 아이템
    /// </summary>
    public class VersionHistoryItem
    {
        public string VersionId { get; set; } = string.Empty;
        public string VersionLabel { get; set; } = string.Empty;
        public string ModifiedDate { get; set; } = string.Empty;
        public string ModifiedBy { get; set; } = string.Empty;
        public string SizeDisplay { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public Visibility IsCurrentVisibility { get; set; } = Visibility.Collapsed;
        public Visibility RestoreVisibility { get; set; } = Visibility.Visible;
    }
}
