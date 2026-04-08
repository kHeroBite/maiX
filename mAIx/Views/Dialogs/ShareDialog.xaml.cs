using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.Views.Dialogs
{
    /// <summary>
    /// 공유 링크 생성/관리 다이얼로그
    /// </summary>
    public partial class ShareDialog : Wpf.Ui.Controls.FluentWindow
    {
        private static readonly ILogger _log = Log.ForContext<ShareDialog>();
        private readonly GraphOneDriveService _oneDriveService;
        private readonly string _itemId;
        private readonly string _itemName;

        public ObservableCollection<SharePermissionItem> Permissions { get; } = new();

        public ShareDialog(GraphOneDriveService oneDriveService, string itemId, string itemName)
        {
            _oneDriveService = oneDriveService ?? throw new ArgumentNullException(nameof(oneDriveService));
            _itemId = itemId;
            _itemName = itemName;
            InitializeComponent();
            FileNameText.Text = itemName;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPermissionsAsync();
        }

        private async System.Threading.Tasks.Task LoadPermissionsAsync()
        {
            try
            {
                var permissions = await _oneDriveService.GetSharePermissionsAsync(_itemId);
                Permissions.Clear();

                foreach (var perm in permissions)
                {
                    var displayName = perm.GrantedToV2?.User?.DisplayName
                        ?? perm.Link?.Scope
                        ?? "알 수 없음";
                    var roles = perm.Roles != null ? string.Join(", ", perm.Roles) : "";
                    var permType = perm.Link != null ? "링크" : "직접";

                    Permissions.Add(new SharePermissionItem
                    {
                        DisplayName = displayName,
                        RoleDescription = roles,
                        PermissionType = permType
                    });
                }

                PermissionsListView.ItemsSource = Permissions;
                _log.Debug("공유 권한 로드: {Count}개", Permissions.Count);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "공유 권한 로드 실패");
            }
        }

        private async void CreateLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CreateLinkButton.IsEnabled = false;
                var linkType = LinkTypeComboBox.SelectedIndex == 0 ? "view" : "edit";
                DateTimeOffset? expiry = ExpiryDatePicker.SelectedDate.HasValue
                    ? new DateTimeOffset(ExpiryDatePicker.SelectedDate.Value)
                    : null;

                var permission = await _oneDriveService.CreateShareLinkWithOptionsAsync(_itemId, linkType, expiry);
                if (permission?.Link?.WebUrl != null)
                {
                    ShareLinkTextBox.Text = permission.Link.WebUrl;
                    _log.Information("공유 링크 생성 완료: {Url}", permission.Link.WebUrl);
                }
                else
                {
                    ShareLinkTextBox.Text = "링크 생성 실패";
                }

                await LoadPermissionsAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "공유 링크 생성 실패");
                ShareLinkTextBox.Text = "오류 발생";
            }
            finally
            {
                CreateLinkButton.IsEnabled = true;
            }
        }

        private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ShareLinkTextBox.Text))
            {
                Clipboard.SetText(ShareLinkTextBox.Text);
                _log.Debug("공유 링크 클립보드 복사");
            }
        }

        private async void InviteButton_Click(object sender, RoutedEventArgs e)
        {
            var email = InviteEmailTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(email))
                return;

            try
            {
                InviteButton.IsEnabled = false;
                // 편집 권한으로 공유 링크 생성 (사용자별 초대는 Graph Beta API 필요)
                var permission = await _oneDriveService.CreateShareLinkWithOptionsAsync(_itemId, "edit");
                if (permission?.Link?.WebUrl != null)
                {
                    ShareLinkTextBox.Text = permission.Link.WebUrl;
                    InviteEmailTextBox.Text = string.Empty;
                    _log.Information("사용자 초대 링크 생성: {Email}", email);
                }

                await LoadPermissionsAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "사용자 초대 실패");
            }
            finally
            {
                InviteButton.IsEnabled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// 공유 권한 아이템
    /// </summary>
    public class SharePermissionItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string RoleDescription { get; set; } = string.Empty;
        public string PermissionType { get; set; } = string.Empty;
    }
}
