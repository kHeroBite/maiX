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
    /// 주간 타임라인 뷰 컨트롤
    /// 24시간 세로 타임라인 × 7일 그리드로 이벤트를 시각적으로 배치
    /// </summary>
    public partial class WeekViewControl : UserControl
    {
        private static readonly ILogger _logger = Log.ForContext<WeekViewControl>();
        private const int 시간당높이 = 60; // 1시간 = 60px
        private const int 총시간 = 24;

        /// <summary>
        /// 주의 시작일 (일요일)
        /// </summary>
        public static readonly DependencyProperty WeekStartProperty =
            DependencyProperty.Register(nameof(WeekStart), typeof(DateTime), typeof(WeekViewControl),
                new PropertyMetadata(GetWeekStart(DateTime.Today), OnWeekStartChanged));

        public DateTime WeekStart
        {
            get => (DateTime)GetValue(WeekStartProperty);
            set => SetValue(WeekStartProperty, value);
        }

        /// <summary>
        /// 주간 이벤트 목록
        /// </summary>
        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(List<Event>), typeof(WeekViewControl),
                new PropertyMetadata(null, OnEventsChanged));

        public List<Event>? Events
        {
            get => (List<Event>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }

        /// <summary>
        /// 이벤트 클릭
        /// </summary>
        public event EventHandler<Event>? EventClicked;

        /// <summary>
        /// 시간 슬롯 더블클릭 (새 이벤트 생성)
        /// </summary>
        public event EventHandler<DateTime>? TimeSlotDoubleClicked;

        public WeekViewControl()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                BuildTimeline();
                UpdateEvents();
                // 현재 시간으로 스크롤
                ScrollToCurrentTime();
            };
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            return date.AddDays(-(int)date.DayOfWeek).Date;
        }

        private static void OnWeekStartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WeekViewControl control && control.IsLoaded)
            {
                control.BuildTimeline();
                control.UpdateEvents();
            }
        }

        private static void OnEventsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WeekViewControl control && control.IsLoaded)
                control.UpdateEvents();
        }

        /// <summary>
        /// 타임라인 기본 구조 (시간 레이블 + 가로선) 생성
        /// </summary>
        private void BuildTimeline()
        {
            if (TimelineGrid == null) return;

            // 기존 자식 모두 제거
            TimelineGrid.Children.Clear();
            TimelineGrid.RowDefinitions.Clear();

            // 24시간 * 시간당높이
            TimelineGrid.Height = 총시간 * 시간당높이;

            // 요일 헤더 업데이트
            UpdateDayHeaders();

            // Canvas를 각 요일 열에 배치 (이벤트 블록 배치용)
            for (int dayIdx = 0; dayIdx < 7; dayIdx++)
            {
                var dayCanvas = new Canvas
                {
                    Name = $"DayCanvas{dayIdx}",
                    Background = Brushes.Transparent,
                    ClipToBounds = true
                };

                // 더블클릭으로 새 이벤트 생성
                var capturedDay = dayIdx;
                dayCanvas.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        var position = e.GetPosition(dayCanvas);
                        var hour = (int)(position.Y / 시간당높이);
                        var minute = (int)((position.Y % 시간당높이) / 시간당높이 * 60);
                        minute = (minute / 15) * 15; // 15분 단위 스냅
                        var targetDate = WeekStart.AddDays(capturedDay).Date.AddHours(hour).AddMinutes(minute);
                        TimeSlotDoubleClicked?.Invoke(this, targetDate);
                    }
                };

                Grid.SetColumn(dayCanvas, dayIdx + 1);
                TimelineGrid.Children.Add(dayCanvas);
            }

            // 시간 레이블 + 가로선
            for (int hour = 0; hour < 총시간; hour++)
            {
                // 시간 레이블
                var timeLabel = new TextBlock
                {
                    Text = hour == 0 ? "" : $"{hour:D2}:00",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, hour * 시간당높이 - 6, 8, 0)
                };
                Grid.SetColumn(timeLabel, 0);
                TimelineGrid.Children.Add(timeLabel);

                // 가로선 (모든 열에 걸쳐)
                for (int col = 1; col <= 7; col++)
                {
                    var line = new Border
                    {
                        BorderBrush = (Brush)FindResource("ControlElevationBorderBrush"),
                        BorderThickness = new Thickness(0, 0.5, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, hour * 시간당높이, 0, 0),
                        Height = 시간당높이,
                        IsHitTestVisible = false
                    };
                    Grid.SetColumn(line, col);
                    TimelineGrid.Children.Add(line);
                }
            }

            // 현재 시간 표시선
            AddCurrentTimeLine();
        }

        /// <summary>
        /// 요일 헤더 업데이트
        /// </summary>
        private void UpdateDayHeaders()
        {
            var headers = new[] { DayHeader0, DayHeader1, DayHeader2, DayHeader3, DayHeader4, DayHeader5, DayHeader6 };
            var dayNames = new[] { "일", "월", "화", "수", "목", "금", "토" };

            for (int i = 0; i < 7; i++)
            {
                if (headers[i] == null) continue;
                headers[i].Children.Clear();

                var date = WeekStart.AddDays(i);
                var isToday = date.Date == DateTime.Today;

                // 요일명
                var dayNameBlock = new TextBlock
                {
                    Text = dayNames[i],
                    FontSize = 11,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = i == 0 ? new SolidColorBrush(Color.FromRgb(255, 107, 107)) :
                                 i == 6 ? new SolidColorBrush(Color.FromRgb(107, 157, 255)) :
                                 (Brush)FindResource("TextFillColorPrimaryBrush")
                };
                headers[i].Children.Add(dayNameBlock);

                // 날짜 숫자
                var dateBlock = new TextBlock
                {
                    Text = date.Day.ToString(),
                    FontSize = 18,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                if (isToday)
                {
                    // 오늘은 원형 배경
                    var todayBorder = new Border
                    {
                        Background = (Brush)FindResource("SystemAccentColorPrimaryBrush"),
                        CornerRadius = new CornerRadius(16),
                        Width = 32,
                        Height = 32,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    dateBlock.Foreground = Brushes.White;
                    todayBorder.Child = dateBlock;
                    dateBlock.HorizontalAlignment = HorizontalAlignment.Center;
                    dateBlock.VerticalAlignment = VerticalAlignment.Center;
                    headers[i].Children.Add(todayBorder);
                }
                else
                {
                    dateBlock.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
                    headers[i].Children.Add(dateBlock);
                }
            }
        }

        /// <summary>
        /// 이벤트 블록 업데이트
        /// </summary>
        public void UpdateEvents()
        {
            if (TimelineGrid == null || Events == null) return;

            // 기존 이벤트 블록 제거 (DayCanvas의 자식만)
            var canvases = TimelineGrid.Children.OfType<Canvas>().ToList();
            foreach (var canvas in canvases)
                canvas.Children.Clear();

            // 종일 이벤트와 시간 이벤트 분리
            var allDayEvents = new List<Event>();
            var timedEvents = new List<Event>();

            foreach (var evt in Events)
            {
                if (evt.IsAllDay ?? false)
                    allDayEvents.Add(evt);
                else
                    timedEvents.Add(evt);
            }

            // 종일 이벤트 헤더에 표시
            UpdateAllDayEvents(allDayEvents);

            // 시간별 이벤트를 캔버스에 배치
            foreach (var evt in timedEvents)
            {
                var startTime = ConvertToLocalTime(evt);
                if (startTime == DateTime.MinValue) continue;

                var dayIndex = (int)(startTime.Date - WeekStart.Date).TotalDays;
                if (dayIndex < 0 || dayIndex >= 7) continue;

                var canvas = canvases.ElementAtOrDefault(dayIndex);
                if (canvas == null) continue;

                var endTime = ConvertEndToLocalTime(evt);
                var durationMinutes = Math.Max(15, (endTime - startTime).TotalMinutes);

                var top = startTime.Hour * 시간당높이 + (startTime.Minute / 60.0 * 시간당높이);
                var height = Math.Max(15, durationMinutes / 60.0 * 시간당높이);

                var eventBlock = CreateEventBlock(evt, startTime, endTime);

                Canvas.SetTop(eventBlock, top);
                Canvas.SetLeft(eventBlock, 2);
                eventBlock.Width = double.NaN; // Auto
                eventBlock.MaxWidth = 200;
                eventBlock.Height = height;

                // 실제 너비는 Canvas의 ActualWidth에 기반
                canvas.SizeChanged += (s, e) =>
                {
                    eventBlock.Width = Math.Max(20, canvas.ActualWidth - 6);
                };
                if (canvas.ActualWidth > 0)
                    eventBlock.Width = Math.Max(20, canvas.ActualWidth - 6);

                canvas.Children.Add(eventBlock);
            }
        }

        /// <summary>
        /// 종일 이벤트를 헤더에 표시
        /// </summary>
        private void UpdateAllDayEvents(List<Event> allDayEvents)
        {
            var headers = new[] { DayHeader0, DayHeader1, DayHeader2, DayHeader3, DayHeader4, DayHeader5, DayHeader6 };

            foreach (var evt in allDayEvents)
            {
                var startTime = ConvertToLocalTime(evt);
                if (startTime == DateTime.MinValue) continue;

                var dayIndex = (int)(startTime.Date - WeekStart.Date).TotalDays;
                if (dayIndex < 0 || dayIndex >= 7) continue;

                var header = headers[dayIndex];
                if (header == null) continue;

                var capturedEvent = evt;
                var eventLabel = new Border
                {
                    Background = GetEventBrush(evt),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 2, 0, 0),
                    Cursor = Cursors.Hand
                };
                var text = new TextBlock
                {
                    Text = evt.Subject ?? "(제목 없음)",
                    FontSize = 10,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                eventLabel.Child = text;
                eventLabel.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    EventClicked?.Invoke(this, capturedEvent);
                };
                header.Children.Add(eventLabel);
            }
        }

        /// <summary>
        /// 이벤트 블록 UI 생성
        /// </summary>
        private Border CreateEventBlock(Event evt, DateTime startTime, DateTime endTime)
        {
            var capturedEvent = evt;
            var block = new Border
            {
                Background = GetEventBrush(evt),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Cursor = Cursors.Hand,
                ClipToBounds = true
            };

            var stack = new StackPanel();

            // 제목
            var titleText = new TextBlock
            {
                Text = evt.Subject ?? "(제목 없음)",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(titleText);

            // 시간
            var timeText = new TextBlock
            {
                Text = $"{startTime:HH:mm} - {endTime:HH:mm}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
            };
            stack.Children.Add(timeText);

            // 장소 (공간 있으면)
            if (!string.IsNullOrEmpty(evt.Location?.DisplayName))
            {
                var locText = new TextBlock
                {
                    Text = $"📍 {evt.Location.DisplayName}",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                stack.Children.Add(locText);
            }

            block.Child = stack;

            // 클릭
            block.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                EventClicked?.Invoke(this, capturedEvent);
            };

            // 호버 효과
            block.MouseEnter += (s, e) => block.Opacity = 0.85;
            block.MouseLeave += (s, e) => block.Opacity = 1.0;

            return block;
        }

        /// <summary>
        /// 현재 시간 표시선
        /// </summary>
        private void AddCurrentTimeLine()
        {
            var now = DateTime.Now;
            var todayIndex = (int)(now.Date - WeekStart.Date).TotalDays;
            if (todayIndex < 0 || todayIndex >= 7) return;

            var top = now.Hour * 시간당높이 + (now.Minute / 60.0 * 시간당높이);

            // 빨간 가로선
            var timeLine = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                Height = 2,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top, 0, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(timeLine, todayIndex + 1);
            TimelineGrid.Children.Add(timeLine);

            // 빨간 동그라미
            var circle = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-4, top - 3, 0, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(circle, todayIndex + 1);
            TimelineGrid.Children.Add(circle);
        }

        /// <summary>
        /// 현재 시간으로 스크롤
        /// </summary>
        private void ScrollToCurrentTime()
        {
            if (TimelineScrollViewer == null) return;

            var now = DateTime.Now;
            var scrollOffset = Math.Max(0, (now.Hour - 1) * 시간당높이);
            TimelineScrollViewer.ScrollToVerticalOffset(scrollOffset);
        }

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
                    catch { }
                }
                return parsed;
            }
            return DateTime.MinValue;
        }

        private static DateTime ConvertEndToLocalTime(Event evt)
        {
            if (evt.End?.DateTime == null) return ConvertToLocalTime(evt).AddHours(1);
            if (DateTime.TryParse(evt.End.DateTime, out var parsed))
            {
                var tz = evt.End.TimeZone;
                if (!string.IsNullOrEmpty(tz) && tz != "Asia/Seoul" && tz != "Korea Standard Time")
                {
                    try
                    {
                        var sourceZone = TimeZoneInfo.FindSystemTimeZoneById(tz);
                        var utc = TimeZoneInfo.ConvertTimeToUtc(parsed, sourceZone);
                        return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
                    }
                    catch { }
                }
                return parsed;
            }
            return ConvertToLocalTime(evt).AddHours(1);
        }

        private Brush GetEventBrush(Event evt)
        {
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
