using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Graph.Models;
using mAIx.Services.Graph;
using Serilog;
using Wpf.Ui.Controls;

namespace mAIx.Dialogs;

/// <summary>
/// Teams 미팅 예약 다이얼로그
/// </summary>
public partial class MeetingScheduleDialog : FluentWindow
{
    private static readonly ILogger _logger = Log.ForContext<MeetingScheduleDialog>();
    private readonly GraphTeamsService _teamsService;

    /// <summary>
    /// 생성된 미팅 결과 (성공 시)
    /// </summary>
    public OnlineMeeting? CreatedMeeting { get; private set; }

    public MeetingScheduleDialog(GraphTeamsService teamsService)
    {
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        InitializeComponent();

        // 기본값 설정: 오늘 날짜, 다음 정시
        var now = DateTime.Now;
        var nextHour = now.AddHours(1);
        nextHour = new DateTime(nextHour.Year, nextHour.Month, nextHour.Day, nextHour.Hour, 0, 0);

        StartDatePicker.SelectedDate = nextHour.Date;
        EndDatePicker.SelectedDate = nextHour.Date;
        StartTimeBox.Text = nextHour.ToString("HH:mm");
        EndTimeBox.Text = nextHour.AddHours(1).ToString("HH:mm");
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 입력 검증
            var subject = MeetingSubjectBox.Text?.Trim();
            if (string.IsNullOrEmpty(subject))
            {
                StatusMessage.Text = "미팅 제목을 입력하세요.";
                return;
            }

            if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
            {
                StatusMessage.Text = "날짜를 선택하세요.";
                return;
            }

            if (!TimeSpan.TryParse(StartTimeBox.Text, out var startTime) ||
                !TimeSpan.TryParse(EndTimeBox.Text, out var endTime))
            {
                StatusMessage.Text = "시간 형식이 올바르지 않습니다. (HH:mm)";
                return;
            }

            var startDateTime = StartDatePicker.SelectedDate.Value.Date + startTime;
            var endDateTime = EndDatePicker.SelectedDate.Value.Date + endTime;

            if (endDateTime <= startDateTime)
            {
                StatusMessage.Text = "종료 시간은 시작 시간 이후여야 합니다.";
                return;
            }

            // 참석자 파싱
            var attendeesText = AttendeesBox.Text?.Trim();
            List<string>? attendees = null;
            if (!string.IsNullOrEmpty(attendeesText))
            {
                attendees = attendeesText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => a.Contains('@'))
                    .ToList();
            }

            // 미팅 생성
            CreateButton.IsEnabled = false;
            StatusMessage.Text = "미팅 생성 중...";

            try
            {
                CreatedMeeting = await _teamsService.CreateOnlineMeetingAsync(subject, startDateTime, endDateTime, attendees);

                if (CreatedMeeting != null)
                {
                    _logger.Information("미팅 생성 성공: {Subject}, 링크: {JoinUrl}", subject, CreatedMeeting.JoinWebUrl);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    StatusMessage.Text = "미팅 생성에 실패했습니다.";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "미팅 생성 실패");
                StatusMessage.Text = $"오류: {ex.Message}";
            }
            finally
            {
                CreateButton.IsEnabled = true;
            }
        }
        catch (Exception exOuter)
        {
            _logger.Error(exOuter, "[MeetingScheduleDialog] CreateButton_Click 실패");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
