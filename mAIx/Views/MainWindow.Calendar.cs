using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Models;
using mAIx.Utils;
using mAIx.ViewModels;
using mAIx.Utilities;

using Application = System.Windows.Application;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — 캘린더 관련 핸들러
    /// 뷰 전환(월간/주간/일간/일정), 다중 캘린더, 자연어 입력 등
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 현재 캘린더 뷰 모드
        /// </summary>
        private CalendarViewMode _currentCalViewMode = CalendarViewMode.Month;

        #region 뷰 모드 전환

        /// <summary>
        /// 이전 기간으로 이동 (뷰 모드에 따라)
        /// </summary>
        private void CalPrevBtn_Click(object sender, RoutedEventArgs e)
        {
            switch (_currentCalViewMode)
            {
                case CalendarViewMode.Day:
                    _currentCalendarDate = _currentCalendarDate.AddDays(-1);
                    _selectedCalendarDate = _currentCalendarDate;
                    break;
                case CalendarViewMode.Week:
                    _currentCalendarDate = _currentCalendarDate.AddDays(-7);
                    break;
                case CalendarViewMode.Month:
                    _currentCalendarDate = _currentCalendarDate.AddMonths(-1);
                    break;
                case CalendarViewMode.Agenda:
                    _currentCalendarDate = _currentCalendarDate.AddDays(-7);
                    break;
            }
            RefreshCalendarView();
        }

        /// <summary>
        /// 다음 기간으로 이동 (뷰 모드에 따라)
        /// </summary>
        private void CalNextBtn_Click(object sender, RoutedEventArgs e)
        {
            switch (_currentCalViewMode)
            {
                case CalendarViewMode.Day:
                    _currentCalendarDate = _currentCalendarDate.AddDays(1);
                    _selectedCalendarDate = _currentCalendarDate;
                    break;
                case CalendarViewMode.Week:
                    _currentCalendarDate = _currentCalendarDate.AddDays(7);
                    break;
                case CalendarViewMode.Month:
                    _currentCalendarDate = _currentCalendarDate.AddMonths(1);
                    break;
                case CalendarViewMode.Agenda:
                    _currentCalendarDate = _currentCalendarDate.AddDays(7);
                    break;
            }
            RefreshCalendarView();
        }

        /// <summary>
        /// 일간 뷰로 전환
        /// </summary>
        private void CalDayViewBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchCalendarViewMode(CalendarViewMode.Day);
        }

        /// <summary>
        /// 주간 뷰로 전환
        /// </summary>
        private void CalWeekViewBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchCalendarViewMode(CalendarViewMode.Week);
        }

        /// <summary>
        /// 월간 뷰로 전환
        /// </summary>
        private void CalMonthViewBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchCalendarViewMode(CalendarViewMode.Month);
        }

        /// <summary>
        /// 일정(Agenda) 뷰로 전환
        /// </summary>
        private void CalAgendaViewBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchCalendarViewMode(CalendarViewMode.Agenda);
        }

        /// <summary>
        /// 뷰 모드 전환 공통 처리
        /// </summary>
        private void SwitchCalendarViewMode(CalendarViewMode mode)
        {
            _currentCalViewMode = mode;

            // 버튼 외관 업데이트
            if (CalDayViewBtn != null)
                CalDayViewBtn.Appearance = mode == CalendarViewMode.Day ?
                    Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            if (CalWeekViewBtn != null)
                CalWeekViewBtn.Appearance = mode == CalendarViewMode.Week ?
                    Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            if (CalMonthViewBtn != null)
                CalMonthViewBtn.Appearance = mode == CalendarViewMode.Month ?
                    Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            if (CalAgendaViewBtn != null)
                CalAgendaViewBtn.Appearance = mode == CalendarViewMode.Agenda ?
                    Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;

            // 뷰 패널 가시성 전환
            if (MonthViewPanel != null) MonthViewPanel.Visibility =
                mode == CalendarViewMode.Month ? Visibility.Visible : Visibility.Collapsed;
            if (WeekViewPanel != null) WeekViewPanel.Visibility =
                mode == CalendarViewMode.Week ? Visibility.Visible : Visibility.Collapsed;
            if (DayViewPanel != null) DayViewPanel.Visibility =
                mode == CalendarViewMode.Day ? Visibility.Visible : Visibility.Collapsed;
            if (AgendaViewPanel != null) AgendaViewPanel.Visibility =
                mode == CalendarViewMode.Agenda ? Visibility.Visible : Visibility.Collapsed;

            RefreshCalendarView();
        }

        /// <summary>
        /// 캘린더 뷰 새로고침 (현재 뷰 모드에 따라)
        /// </summary>
        private async void RefreshCalendarView()
        {
            try
            {
                // 월/년 텍스트 업데이트
                UpdateCalendarHeaderText();

                switch (_currentCalViewMode)
                {
                    case CalendarViewMode.Month:
                        await LoadMonthEventsFromDbAsync(_currentCalendarDate);
                        UpdateCalendarDisplay();
                        break;

                    case CalendarViewMode.Week:
                        await LoadWeekEventsAsync();
                        break;

                    case CalendarViewMode.Day:
                        await LoadDayEventsAsync();
                        break;

                    case CalendarViewMode.Agenda:
                        await LoadAgendaEventsAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"캘린더 뷰 새로고침 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 헤더 텍스트 업데이트 (뷰 모드별)
        /// </summary>
        private void UpdateCalendarHeaderText()
        {
            string headerText;
            switch (_currentCalViewMode)
            {
                case CalendarViewMode.Day:
                    headerText = $"{_currentCalendarDate:yyyy년 M월 d일 (ddd)}";
                    break;
                case CalendarViewMode.Week:
                    var weekStart = _currentCalendarDate.AddDays(-(int)_currentCalendarDate.DayOfWeek);
                    var weekEnd = weekStart.AddDays(6);
                    headerText = weekStart.Month == weekEnd.Month
                        ? $"{weekStart:yyyy년 M월 d일} - {weekEnd:d일}"
                        : $"{weekStart:yyyy년 M월 d일} - {weekEnd:M월 d일}";
                    break;
                case CalendarViewMode.Agenda:
                    headerText = $"{_currentCalendarDate:yyyy년 M월} - 일정 목록";
                    break;
                default:
                    headerText = $"{_currentCalendarDate.Year}년 {_currentCalendarDate.Month}월";
                    break;
            }

            if (CalMainMonthYearText != null) CalMainMonthYearText.Text = headerText;
            if (CalMonthYearText != null) CalMonthYearText.Text =
                $"{_currentCalendarDate.Year}년 {_currentCalendarDate.Month}월";
        }

        #endregion

        #region 주간 뷰

        /// <summary>
        /// 주간 이벤트 로드 및 WeekViewControl에 전달
        /// </summary>
        private async Task LoadWeekEventsAsync()
        {
            try
            {
                var weekStart = _currentCalendarDate.AddDays(-(int)_currentCalendarDate.DayOfWeek).Date;
                var weekEnd = weekStart.AddDays(7);

                var app = (App)Application.Current;
                using var scope = app.ServiceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Data.mAIxDbContext>();

                var dbEvents = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(dbContext.CalendarEvents
                        .Where(e => !e.IsDeleted && !e.IsCancelled)
                        .Where(e => e.StartDateTime.Date < weekEnd.Date && e.EndDateTime.Date >= weekStart.Date)
                        .OrderBy(e => e.StartDateTime)
                        .ThenBy(e => e.Subject)
                        .ThenBy(e => e.Id));

                var graphEvents = dbEvents.Select(ConvertToGraphEvent).ToList();

                if (WeekViewCtrl != null)
                {
                    WeekViewCtrl.WeekStart = weekStart;
                    WeekViewCtrl.Events = graphEvents;
                    WeekViewCtrl.EventClicked -= OnCalendarEventClicked;
                    WeekViewCtrl.EventClicked += OnCalendarEventClicked;
                    WeekViewCtrl.TimeSlotDoubleClicked -= OnTimeSlotDoubleClicked;
                    WeekViewCtrl.TimeSlotDoubleClicked += OnTimeSlotDoubleClicked;
                }

                // 세부 패널도 업데이트
                var todayEvents = graphEvents
                    .Where(e => e.Start?.DateTime != null && GetLocalStartTime(e).Date == _selectedCalendarDate.Date)
                    .OrderBy(e => GetLocalStartTime(e))
                    .ThenBy(e => e.Subject)
                    .ThenBy(e => e.Id)
                    .ToList();
                UpdateCalendarDetailPanel(_selectedCalendarDate, todayEvents);

                Log4.Info($"주간 뷰 로드 완료: {graphEvents.Count}건 ({weekStart:MM/dd}~{weekEnd:MM/dd})");
            }
            catch (Exception ex)
            {
                Log4.Error($"주간 이벤트 로드 실패: {ex.Message}");
            }
        }

        #endregion

        #region 일간 뷰

        /// <summary>
        /// 일간 이벤트 로드 및 DayViewControl에 전달
        /// </summary>
        private async Task LoadDayEventsAsync()
        {
            try
            {
                var date = _currentCalendarDate.Date;
                var nextDate = date.AddDays(1);

                var app = (App)Application.Current;
                using var scope = app.ServiceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Data.mAIxDbContext>();

                var dbEvents = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(dbContext.CalendarEvents
                        .Where(e => !e.IsDeleted && !e.IsCancelled)
                        .Where(e => e.StartDateTime.Date < nextDate.Date && e.EndDateTime.Date >= date.Date)
                        .OrderBy(e => e.StartDateTime)
                        .ThenBy(e => e.Subject)
                        .ThenBy(e => e.Id));

                var graphEvents = dbEvents.Select(ConvertToGraphEvent).ToList();

                if (DayViewCtrl != null)
                {
                    DayViewCtrl.SelectedDate = date;
                    DayViewCtrl.Events = graphEvents;
                    DayViewCtrl.EventClicked -= OnCalendarEventClicked;
                    DayViewCtrl.EventClicked += OnCalendarEventClicked;
                    DayViewCtrl.TimeSlotDoubleClicked -= OnTimeSlotDoubleClicked;
                    DayViewCtrl.TimeSlotDoubleClicked += OnTimeSlotDoubleClicked;
                }

                UpdateCalendarDetailPanel(date, graphEvents);

                Log4.Info($"일간 뷰 로드 완료: {graphEvents.Count}건 ({date:yyyy-MM-dd})");
            }
            catch (Exception ex)
            {
                Log4.Error($"일간 이벤트 로드 실패: {ex.Message}");
            }
        }

        #endregion

        #region 일정(Agenda) 뷰

        /// <summary>
        /// 일정 리스트 로드 (2주간)
        /// </summary>
        private async Task LoadAgendaEventsAsync()
        {
            try
            {
                var startDate = _currentCalendarDate.Date;
                var endDate = startDate.AddDays(14);

                var app = (App)Application.Current;
                using var scope = app.ServiceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Data.mAIxDbContext>();

                var dbEvents = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(dbContext.CalendarEvents
                        .Where(e => !e.IsDeleted && !e.IsCancelled)
                        .Where(e => e.StartDateTime.Date < endDate.Date && e.EndDateTime.Date >= startDate.Date)
                        .OrderBy(e => e.StartDateTime)
                        .ThenBy(e => e.Subject)
                        .ThenBy(e => e.Id));

                var graphEvents = dbEvents.Select(ConvertToGraphEvent).ToList();

                UpdateAgendaView(graphEvents, startDate);

                Log4.Info($"일정 뷰 로드 완료: {graphEvents.Count}건 ({startDate:MM/dd}~{endDate:MM/dd})");
            }
            catch (Exception ex)
            {
                Log4.Error($"일정 리스트 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// Agenda 뷰 UI 업데이트
        /// </summary>
        private void UpdateAgendaView(List<Event> events, DateTime startDate)
        {
            if (AgendaEventsPanel == null) return;

            // 기존 내용 제거 (NoEventsText 제외)
            var toRemove = AgendaEventsPanel.Children.Cast<UIElement>()
                .Where(c => c != AgendaNoEventsText)
                .ToList();
            foreach (var item in toRemove)
                AgendaEventsPanel.Children.Remove(item);

            if (AgendaNoEventsText != null)
                AgendaNoEventsText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 날짜별 그룹화
            var grouped = events
                .Where(e => e.Start?.DateTime != null)
                .GroupBy(e => GetLocalStartTime(e).Date)
                .OrderBy(g => g.Key);

            foreach (var dateGroup in grouped)
            {
                var isToday = dateGroup.Key == DateTime.Today;

                // 날짜 헤더
                var dateHeader = new TextBlock
                {
                    Text = isToday ? $"오늘 — {dateGroup.Key:M월 d일 (ddd)}" : $"{dateGroup.Key:M월 d일 (ddd)}",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isToday ? (Brush)FindResource("SystemAccentColorPrimaryBrush") :
                        (Brush)FindResource("TextFillColorPrimaryBrush"),
                    Margin = new Thickness(0, 16, 0, 8)
                };
                AgendaEventsPanel.Children.Add(dateHeader);

                // 이벤트 카드
                foreach (var evt in dateGroup.OrderBy(e => GetLocalStartTime(e)).ThenBy(e => e.Subject).ThenBy(e => e.Id))
                {
                    var capturedEvent = evt;
                    var card = CreateDetailEventCard(evt);
                    card.MouseLeftButtonDown += async (s, args) =>
                    {
                        try
                        {
                            args.Handled = true;
                            await OpenEventEditDialogAsync(capturedEvent, null);
                        }
                        catch (Exception ex)
                        {
                            Log4.Error($"[Calendar] 아젠다 이벤트 클릭 핸들러 실패: {ex}");
                        }
                    };
                    AgendaEventsPanel.Children.Add(card);
                }
            }
        }

        #endregion

        #region 이벤트 핸들러 (컨트롤 공통)

        /// <summary>
        /// 캘린더 컨트롤에서 이벤트 클릭 시
        /// </summary>
        private async void OnCalendarEventClicked(object? sender, Event evt)
        {
            Log4.Info($"이벤트 클릭: {evt.Subject}");
            await OpenEventEditDialogAsync(evt, null);
        }

        /// <summary>
        /// 시간 슬롯 더블클릭 시 새 이벤트 생성
        /// </summary>
        private async void OnTimeSlotDoubleClicked(object? sender, DateTime targetDateTime)
        {
            Log4.Info($"시간 슬롯 더블클릭: {targetDateTime:yyyy-MM-dd HH:mm}");
            await OpenEventEditDialogAsync(null, targetDateTime);
        }

        #endregion

        #region 다중 캘린더

        /// <summary>
        /// 다중 캘린더 목록 로드 및 UI 생성
        /// </summary>
        private async Task LoadCalendarListAsync()
        {
            try
            {
                var calendarService = ((App)Application.Current).GetService<Services.Graph.GraphCalendarService>();
                if (calendarService == null) return;

                var calendars = await calendarService.GetCalendarsAsync();

                if (CalendarListPanel != null)
                {
                    CalendarListPanel.Children.Clear();
                    foreach (var cal in calendars)
                    {
                        var checkbox = new CheckBox
                        {
                            Content = cal.Name,
                            IsChecked = true,
                            Tag = cal.Id,
                            Margin = new Thickness(0, 2, 0, 2),
                            FontSize = 12
                        };
                        // 색상 인디케이터 추가
                        var colorDot = new Border
                        {
                            Width = 10,
                            Height = 10,
                            CornerRadius = new CornerRadius(5),
                            Background = GetCalendarColorBrush(cal.Color),
                            Margin = new Thickness(0, 0, 6, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        var stack = new StackPanel { Orientation = Orientation.Horizontal };
                        stack.Children.Add(colorDot);
                        stack.Children.Add(new TextBlock { Text = cal.Name, VerticalAlignment = VerticalAlignment.Center });
                        checkbox.Content = stack;

                        CalendarListPanel.Children.Add(checkbox);
                    }

                    Log4.Info($"캘린더 목록 로드: {calendars.Count}개");
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"캘린더 목록 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 캘린더 색상 문자열을 Brush로 변환
        /// </summary>
        private static Brush GetCalendarColorBrush(string? color)
        {
            return color switch
            {
                "LightBlue" or "lightBlue" => new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                "LightGreen" or "lightGreen" => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                "LightOrange" or "lightOrange" => new SolidColorBrush(Color.FromRgb(243, 156, 18)),
                "LightGray" or "lightGray" => new SolidColorBrush(Color.FromRgb(149, 165, 166)),
                "LightYellow" or "lightYellow" => new SolidColorBrush(Color.FromRgb(241, 196, 15)),
                "LightTeal" or "lightTeal" => new SolidColorBrush(Color.FromRgb(26, 188, 156)),
                "LightPink" or "lightPink" => new SolidColorBrush(Color.FromRgb(232, 67, 147)),
                "LightRed" or "lightRed" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                _ => new SolidColorBrush(Color.FromRgb(0, 120, 212))
            };
        }

        #endregion
    }
}
