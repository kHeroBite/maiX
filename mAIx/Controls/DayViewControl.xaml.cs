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
    /// 일간 상세 뷰 컨트롤
    /// 선택된 하루의 24시간 타임라인에 이벤트를 상세 표시
    /// </summary>
    public partial class DayViewControl : UserControl
    {
        private static readonly ILogger _logger = Log.ForContext<DayViewControl>();
        private const int 시간당높이 = 60;
        private const int 총시간 = 24;

        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime), typeof(DayViewControl),
                new PropertyMetadata(DateTime.Today, OnSelectedDateChanged));

        public DateTime SelectedDate
        {
            get => (DateTime)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(List<Event>), typeof(DayViewControl),
                new PropertyMetadata(null, OnEventsChanged));

        public List<Event>? Events
        {
            get => (List<Event>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }

        public event EventHandler<Event>? EventClicked;
        public event EventHandler<DateTime>? TimeSlotDoubleClicked;

        private Canvas? _eventCanvas;

        public DayViewControl()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                BuildDayTimeline();
                UpdateDisplay();
                ScrollToCurrentTime();
            };
        }

        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DayViewControl control && control.IsLoaded)
                control.UpdateDisplay();
        }

        private static void OnEventsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DayViewControl control && control.IsLoaded)
                control.UpdateDisplay();
        }

        private void BuildDayTimeline()
        {
            if (DayTimelineGrid == null) return;

            DayTimelineGrid.Children.Clear();
            DayTimelineGrid.Height = 총시간 * 시간당높이;

            // 이벤트 캔버스
            _eventCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                ClipToBounds = true
            };
            _eventCanvas.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    var pos = e.GetPosition(_eventCanvas);
                    var hour = (int)(pos.Y / 시간당높이);
                    var minute = ((int)(pos.Y % 시간당높이 / 시간당높이 * 60) / 15) * 15;
                    TimeSlotDoubleClicked?.Invoke(this, SelectedDate.Date.AddHours(hour).AddMinutes(minute));
                }
            };
            Grid.SetColumn(_eventCanvas, 1);
            DayTimelineGrid.Children.Add(_eventCanvas);

            // 시간 레이블 + 가로선
            for (int hour = 0; hour < 총시간; hour++)
            {
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
                DayTimelineGrid.Children.Add(timeLabel);

                var line = new Border
                {
                    BorderBrush = (Brush)FindResource("ControlElevationBorderBrush"),
                    BorderThickness = new Thickness(0, 0.5, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, hour * 시간당높이, 0, 0),
                    Height = 시간당높이,
                    IsHitTestVisible = false
                };
                Grid.SetColumn(line, 1);
                DayTimelineGrid.Children.Add(line);
            }
        }

        private void UpdateDisplay()
        {
            UpdateDateHeader();
            UpdateEvents();
            AddCurrentTimeLine();
        }

        private void UpdateDateHeader()
        {
            if (DateHeaderPanel == null) return;
            DateHeaderPanel.Children.Clear();

            var isToday = SelectedDate.Date == DateTime.Today;
            var dayNames = new[] { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };

            var dayName = new TextBlock
            {
                Text = dayNames[(int)SelectedDate.DayOfWeek],
                FontSize = 14,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            DateHeaderPanel.Children.Add(dayName);

            var dateNumber = new TextBlock
            {
                Text = SelectedDate.Day.ToString(),
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (isToday)
            {
                var todayBorder = new Border
                {
                    Background = (Brush)FindResource("SystemAccentColorPrimaryBrush"),
                    CornerRadius = new CornerRadius(20),
                    Width = 40,
                    Height = 40,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                dateNumber.Foreground = Brushes.White;
                dateNumber.HorizontalAlignment = HorizontalAlignment.Center;
                dateNumber.VerticalAlignment = VerticalAlignment.Center;
                todayBorder.Child = dateNumber;
                DateHeaderPanel.Children.Add(todayBorder);
            }
            else
            {
                dateNumber.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
                DateHeaderPanel.Children.Add(dateNumber);
            }
        }

        private void UpdateEvents()
        {
            if (_eventCanvas == null || Events == null) return;
            _eventCanvas.Children.Clear();

            var dayEvents = Events
                .Where(e =>
                {
                    if (e.Start?.DateTime == null) return false;
                    return ConvertToLocalTime(e).Date == SelectedDate.Date;
                })
                .ToList();

            // 종일 이벤트
            var allDay = dayEvents.Where(e => e.IsAllDay ?? false).ToList();
            var timed = dayEvents.Where(e => !(e.IsAllDay ?? false)).ToList();

            UpdateAllDayEvents(allDay);

            foreach (var evt in timed)
            {
                var startTime = ConvertToLocalTime(evt);
                var endTime = ConvertEndToLocalTime(evt);
                var durationMinutes = Math.Max(15, (endTime - startTime).TotalMinutes);

                var top = startTime.Hour * 시간당높이 + (startTime.Minute / 60.0 * 시간당높이);
                var height = Math.Max(20, durationMinutes / 60.0 * 시간당높이);

                var block = CreateEventBlock(evt, startTime, endTime);
                Canvas.SetTop(block, top);
                Canvas.SetLeft(block, 4);
                block.Height = height;

                _eventCanvas.SizeChanged += (s, e) =>
                {
                    block.Width = Math.Max(40, _eventCanvas.ActualWidth - 12);
                };
                if (_eventCanvas.ActualWidth > 0)
                    block.Width = Math.Max(40, _eventCanvas.ActualWidth - 12);

                _eventCanvas.Children.Add(block);
            }
        }

        private void UpdateAllDayEvents(List<Event> allDayEvents)
        {
            if (AllDayEventsPanel == null || AllDayBorder == null) return;
            AllDayEventsPanel.Children.Clear();

            if (allDayEvents.Count == 0)
            {
                AllDayBorder.Visibility = Visibility.Collapsed;
                return;
            }

            AllDayBorder.Visibility = Visibility.Visible;
            foreach (var evt in allDayEvents)
            {
                var capturedEvent = evt;
                var label = new Border
                {
                    Background = GetEventBrush(evt),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 2, 0, 2),
                    Cursor = Cursors.Hand
                };
                var text = new TextBlock
                {
                    Text = $"종일: {evt.Subject ?? "(제목 없음)"}",
                    FontSize = 12,
                    Foreground = Brushes.White
                };
                label.Child = text;
                label.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    EventClicked?.Invoke(this, capturedEvent);
                };
                AllDayEventsPanel.Children.Add(label);
            }
        }

        private Border CreateEventBlock(Event evt, DateTime startTime, DateTime endTime)
        {
            var capturedEvent = evt;
            var block = new Border
            {
                Background = GetEventBrush(evt),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Cursor = Cursors.Hand,
                ClipToBounds = true
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = evt.Subject ?? "(제목 없음)",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{startTime:HH:mm} - {endTime:HH:mm}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Margin = new Thickness(0, 2, 0, 0)
            });

            if (!string.IsNullOrEmpty(evt.Location?.DisplayName))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"📍 {evt.Location.DisplayName}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            if (evt.Attendees != null && evt.Attendees.Any())
            {
                var names = evt.Attendees.Where(a => a.EmailAddress?.Name != null)
                    .Take(3).Select(a => a.EmailAddress!.Name);
                var attendeeText = string.Join(", ", names);
                if (evt.Attendees.Count() > 3)
                    attendeeText += $" 외 {evt.Attendees.Count() - 3}명";

                stack.Children.Add(new TextBlock
                {
                    Text = $"👥 {attendeeText}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            block.Child = stack;
            block.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                EventClicked?.Invoke(this, capturedEvent);
            };
            block.MouseEnter += (s, e) => block.Opacity = 0.85;
            block.MouseLeave += (s, e) => block.Opacity = 1.0;

            return block;
        }

        private void AddCurrentTimeLine()
        {
            if (DayTimelineGrid == null || SelectedDate.Date != DateTime.Today) return;

            var now = DateTime.Now;
            var top = now.Hour * 시간당높이 + (now.Minute / 60.0 * 시간당높이);

            var line = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                Height = 2,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top, 0, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(line, 1);
            DayTimelineGrid.Children.Add(line);
        }

        private void ScrollToCurrentTime()
        {
            if (DayTimelineScrollViewer == null) return;
            var now = DateTime.Now;
            DayTimelineScrollViewer.ScrollToVerticalOffset(Math.Max(0, (now.Hour - 1) * 시간당높이));
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
                        return TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(parsed, sourceZone), TimeZoneInfo.Local);
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
                        return TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(parsed, sourceZone), TimeZoneInfo.Local);
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
