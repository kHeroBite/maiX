using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Graph.Models;
using Serilog;

namespace mAIx.Controls
{
    /// <summary>
    /// 월간 캘린더 그리드 컨트롤 (Fantastical 스타일)
    /// 7열 × 6행 그리드에 날짜 셀과 이벤트를 표시
    /// </summary>
    public partial class CalendarGridControl : UserControl
    {
        private static readonly ILogger _logger = Log.ForContext<CalendarGridControl>();

        /// <summary>
        /// 현재 표시 중인 월
        /// </summary>
        public static readonly DependencyProperty CurrentMonthProperty =
            DependencyProperty.Register(nameof(CurrentMonth), typeof(DateTime), typeof(CalendarGridControl),
                new PropertyMetadata(DateTime.Today, OnCurrentMonthChanged));

        public DateTime CurrentMonth
        {
            get => (DateTime)GetValue(CurrentMonthProperty);
            set => SetValue(CurrentMonthProperty, value);
        }

        /// <summary>
        /// 선택된 날짜
        /// </summary>
        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime), typeof(CalendarGridControl),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

        public DateTime SelectedDate
        {
            get => (DateTime)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        /// <summary>
        /// 월별 이벤트 목록
        /// </summary>
        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(List<Event>), typeof(CalendarGridControl),
                new PropertyMetadata(null, OnEventsChanged));

        public List<Event>? Events
        {
            get => (List<Event>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }

        /// <summary>
        /// 날짜 클릭 이벤트
        /// </summary>
        public event EventHandler<DateTime>? DateClicked;

        /// <summary>
        /// 날짜 더블클릭 이벤트 (새 이벤트 생성)
        /// </summary>
        public event EventHandler<DateTime>? DateDoubleClicked;

        /// <summary>
        /// 이벤트 클릭 이벤트
        /// </summary>
        public event EventHandler<Event>? EventClicked;

        /// <summary>
        /// 캘린더별 색상 매핑
        /// </summary>
        public Dictionary<string, Brush> CalendarColors { get; set; } = new();

        /// <summary>
        /// 숨겨진 캘린더 ID 목록
        /// </summary>
        public HashSet<string> HiddenCalendarIds { get; set; } = new();

        private Grid? _grid;

        public CalendarGridControl()
        {
            InitializeComponent();
            _grid = (Grid)Content;
            Loaded += (s, e) => UpdateGrid();
        }

        private static void OnCurrentMonthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CalendarGridControl control)
                control.UpdateGrid();
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CalendarGridControl control)
                control.UpdateGrid();
        }

        private static void OnEventsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CalendarGridControl control)
                control.UpdateGrid();
        }

        /// <summary>
        /// 그리드 전체 업데이트
        /// </summary>
        public void UpdateGrid()
        {
            if (_grid == null || !IsLoaded) return;

            // 기존 날짜 셀 제거 (Row 0 = 요일 헤더는 유지)
            var toRemove = _grid.Children.Cast<UIElement>()
                .Where(c => Grid.GetRow(c) > 0)
                .ToList();
            foreach (var child in toRemove)
                _grid.Children.Remove(child);

            var firstDay = new DateTime(CurrentMonth.Year, CurrentMonth.Month, 1);
            var daysInMonth = DateTime.DaysInMonth(CurrentMonth.Year, CurrentMonth.Month);
            var startDayOfWeek = (int)firstDay.DayOfWeek;

            var day = 1;
            for (int week = 0; week < 6 && day <= daysInMonth; week++)
            {
                for (int dayOfWeek = 0; dayOfWeek < 7 && day <= daysInMonth; dayOfWeek++)
                {
                    if (week == 0 && dayOfWeek < startDayOfWeek)
                        continue;

                    var cellDate = new DateTime(CurrentMonth.Year, CurrentMonth.Month, day);
                    var cell = CreateDayCell(cellDate);
                    Grid.SetRow(cell, week + 1);
                    Grid.SetColumn(cell, dayOfWeek);
                    _grid.Children.Add(cell);

                    day++;
                }
            }
        }

        /// <summary>
        /// 날짜 셀 생성
        /// </summary>
        private Border CreateDayCell(DateTime date)
        {
            var isToday = date.Date == DateTime.Today;
            var isSelected = date.Date == SelectedDate.Date;
            var dayEvents = GetEventsForDate(date);

            var cell = new Border
            {
                BorderBrush = (Brush)FindResource("ControlElevationBorderBrush"),
                BorderThickness = new Thickness(0.5),
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand
            };

            // 배경: 오늘 vs 선택됨 vs 기본
            if (isToday)
                cell.Background = (Brush)FindResource("SystemAccentColorSecondaryBrush");
            else if (isSelected)
                cell.Background = (Brush)FindResource("SubtleFillColorSecondaryBrush");
            else
                cell.Background = Brushes.Transparent;

            var stack = new StackPanel { Margin = new Thickness(4) };

            // 날짜 숫자
            var dayText = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 12,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                Foreground = GetDayOfWeekBrush(date.DayOfWeek),
                Margin = new Thickness(2, 0, 0, 4)
            };
            stack.Children.Add(dayText);

            // 이벤트 표시 (최대 3개)
            var displayEvents = dayEvents.Take(3);
            foreach (var evt in displayEvents)
            {
                var capturedEvent = evt;
                var eventBorder = new Border
                {
                    Background = GetEventBrush(evt),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    Tag = evt
                };
                var eventText = new TextBlock
                {
                    Text = evt.Subject ?? "(제목 없음)",
                    FontSize = 10,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                eventBorder.Child = eventText;

                eventBorder.MouseLeftButtonDown += (s, args) =>
                {
                    args.Handled = true;
                    EventClicked?.Invoke(this, capturedEvent);
                };

                stack.Children.Add(eventBorder);
            }

            // 더 많은 이벤트 표시
            if (dayEvents.Count > 3)
            {
                var moreText = new TextBlock
                {
                    Text = $"+{dayEvents.Count - 3}개 더",
                    FontSize = 9,
                    Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                    Margin = new Thickness(2, 2, 0, 0)
                };
                stack.Children.Add(moreText);
            }

            cell.Child = stack;

            // 클릭 이벤트
            cell.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    DateDoubleClicked?.Invoke(this, date);
                }
                else if (e.ClickCount == 1)
                {
                    SelectedDate = date;
                    DateClicked?.Invoke(this, date);
                }
            };

            return cell;
        }

        /// <summary>
        /// 특정 날짜의 이벤트 목록 가져오기
        /// </summary>
        private List<Event> GetEventsForDate(DateTime date)
        {
            if (Events == null) return new List<Event>();

            return Events
                .Where(e =>
                {
                    if (e.Start?.DateTime == null) return false;
                    // 숨겨진 캘린더 필터
                    // CalendarId는 Graph API에서 제공하지 않으므로 카테고리 기반 필터
                    var startTime = ConvertToLocalTime(e);
                    return startTime.Date == date.Date;
                })
                .OrderBy(e => ConvertToLocalTime(e))
                .ThenBy(e => e.Subject)
                .ThenBy(e => e.Id)
                .ToList();
        }

        /// <summary>
        /// Graph API 시간을 로컬 시간으로 변환
        /// </summary>
        private static DateTime ConvertToLocalTime(Event evt)
        {
            if (evt.Start?.DateTime == null) return DateTime.MinValue;

            if (DateTime.TryParse(evt.Start.DateTime, out var parsed))
            {
                var tz = evt.Start.TimeZone;
                if (!string.IsNullOrEmpty(tz) && tz != "Asia/Seoul" && tz != "Korea Standard Time")
                {
                    try
                    {
                        var sourceZone = TimeZoneInfo.FindSystemTimeZoneById(tz);
                        var utc = TimeZoneInfo.ConvertTimeToUtc(parsed, sourceZone);
                        return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
                    }
                    catch { /* 시간대 변환 실패 시 원본 사용 */ }
                }
                return parsed;
            }
            return DateTime.MinValue;
        }

        /// <summary>
        /// 요일별 텍스트 색상
        /// </summary>
        private Brush GetDayOfWeekBrush(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Sunday => new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                DayOfWeek.Saturday => new SolidColorBrush(Color.FromRgb(107, 157, 255)),
                _ => (Brush)FindResource("TextFillColorPrimaryBrush")
            };
        }

        /// <summary>
        /// 이벤트 색상 결정 (캘린더별 또는 카테고리별)
        /// </summary>
        private Brush GetEventBrush(Event evt)
        {
            // 카테고리 기반 색상
            var category = evt.Categories?.FirstOrDefault();
            return category switch
            {
                "이메일 마감" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                "할일" => new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                _ => new SolidColorBrush(Color.FromRgb(46, 204, 113))
            };
        }
    }
}
