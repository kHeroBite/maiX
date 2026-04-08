using System;
using System.Windows;
using mAIx.Services;
using mAIx.Utils;
using mAIx.ViewModels;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — 통화 탭 확장 로직 (통화 이력, 크로스탭 연동)
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 통화 탭 활성화 시 호출 — 통화 이력 + 연락처 로드
        /// </summary>
        private async void OnCallsTabActivated()
        {
            try
            {
                if (_callsViewModel == null)
                {
                    _callsViewModel = ((App)Application.Current).GetService<CallsViewModel>()!;
                }

                await _callsViewModel.InitializeAsync();

                // 연락처 + 통화 이력 바인딩
                CallsContactsListView.ItemsSource = _callsViewModel.FrequentContacts;

                // 부재중 통화 업데이트
                UpdateCallsStatusDisplay();
            }
            catch (Exception ex)
            {
                Log4.Error($"통화 탭 활성화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 통화 상태 표시 업데이트
        /// </summary>
        private void UpdateCallsStatusDisplay()
        {
            if (_callsViewModel == null) return;

            var availability = _callsViewModel.MyAvailability;
            var color = availability switch
            {
                "Available" => System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10),
                "Busy" or "InACall" or "InAMeeting" => System.Windows.Media.Color.FromRgb(0xD1, 0x34, 0x38),
                "DoNotDisturb" => System.Windows.Media.Color.FromRgb(0xD1, 0x34, 0x38),
                "Away" or "BeRightBack" => System.Windows.Media.Color.FromRgb(0xFF, 0xAA, 0x44),
                _ => System.Windows.Media.Color.FromRgb(0x8A, 0x88, 0x86)
            };

            CallsMyStatusBrush.Color = color;
            CallsMyStatusText.Text = availability switch
            {
                "Available" => "대화 가능",
                "Busy" => "다른 용무 중",
                "InACall" => "통화 중",
                "InAMeeting" => "회의 중",
                "DoNotDisturb" => "방해 금지",
                "Away" => "자리 비움",
                "BeRightBack" => "곧 돌아옴",
                "Offline" => "오프라인",
                _ => "알 수 없음"
            };
        }

        /// <summary>
        /// 연락처에서 Teams 채팅 시작 (크로스탭)
        /// </summary>
        private async void StartTeamsChatFromContact(ContactItemViewModel contact)
        {
            if (contact == null || _callsViewModel == null) return;

            await _callsViewModel.StartTeamsChatAsync(contact);
        }
    }
}
