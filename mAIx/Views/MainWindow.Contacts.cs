using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using mAIx.Services.Graph;
using mAIx.Services.Search;
using mAIx.ViewModels;
using mAIx.Utils;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — 연락처 관련 핸들러
    /// </summary>
    public partial class MainWindow
    {
        private ContactsViewModel? _contactsViewModel;
        private IServiceScope? _contactsScope;

        /// <summary>
        /// 연락처 뷰 초기화
        /// </summary>
        private void InitializeContactsView()
        {
            try
            {
                var app = (App)Application.Current;
                _contactsScope = app.ServiceProvider.CreateScope();

                var contactService = _contactsScope.ServiceProvider.GetRequiredService<GraphContactService>();
                var searchService = _contactsScope.ServiceProvider.GetRequiredService<ContactSearchService>();

                _contactsViewModel = new ContactsViewModel(contactService, searchService);

                ContactListPanel.DataContext = _contactsViewModel;

                // 선택 변경 시 상세 뷰 업데이트
                _contactsViewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ContactsViewModel.SelectedContact))
                    {
                        ContactDetailPanel.ShowContact(_contactsViewModel.SelectedContact);
                    }
                };

                // 이메일 보내기 이벤트
                _contactsViewModel.ComposeEmailRequested += OnContactComposeEmail;
                ContactDetailPanel.SendEmailRequested += (s, email) => OnContactComposeEmail(s, email);
                ContactDetailPanel.DeleteRequested += async (s, contact) =>
                {
                    if (_contactsViewModel != null)
                    {
                        await _contactsViewModel.DeleteContactCommand.ExecuteAsync(contact);
                    }
                };

                Log4.Info("연락처 뷰 초기화 완료");
            }
            catch (Exception ex)
            {
                Log4.Error($"연락처 뷰 초기화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 연락처 뷰 표시
        /// </summary>
        private async void ShowContactsView()
        {
            HideAllViews();

            if (ContactsViewBorder != null) ContactsViewBorder.Visibility = Visibility.Visible;

            // 첫 표시 시 초기화
            if (_contactsViewModel == null)
            {
                InitializeContactsView();
            }

            // 데이터 로드
            if (_contactsViewModel != null && !_contactsViewModel.IsInitialized)
            {
                await _contactsViewModel.InitializeCommand.ExecuteAsync(null);
            }

            _viewModel.StatusMessage = "연락처";
            Services.Theme.ThemeService.Instance.ApplyFeatureTheme("contacts");

            Log4.Info("연락처 뷰 표시 완료");
        }

        /// <summary>
        /// 연락처에서 이메일 작성 요청
        /// </summary>
        private void OnContactComposeEmail(object? sender, string email)
        {
            Log4.Info($"연락처에서 메일 작성 요청: {email}");
        }
    }
}
