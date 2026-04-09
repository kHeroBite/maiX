using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using mAIx.ViewModels;
using NLog;

namespace mAIx.Controls
{
    /// <summary>
    /// 칸반 보드 컨트롤 — 버킷 컬럼 + 드래그&드롭 + 타임라인 뷰 전환
    /// </summary>
    public partial class KanbanBoardControl : UserControl
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // 타임라인 브러시 캐시 (Freeze로 렌더 스레드 공유 가능)
        private static readonly SolidColorBrush _weekendBrush = CreateFrozen(Color.FromArgb(20, 128, 128, 128));
        private static readonly SolidColorBrush _dateLabelBrush = CreateFrozen(Colors.Gray);
        private static readonly SolidColorBrush _todayLineBrush = CreateFrozen((Color)ColorConverter.ConvertFromString("#D13438"));
        private static readonly SolidColorBrush _barTextBrush = CreateFrozen(Colors.White);

        private static SolidColorBrush CreateFrozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // 드래그 관련
        private Point _dragStartPoint;
        private bool _isDragging;
        private TaskItemViewModel? _draggedTask;

        // 타임라인 관련
        private DateTime _timelineStart;
        private int _timelineWeeks = 4;

        /// <summary>
        /// 버킷 목록 (바인딩용)
        /// </summary>
        public static readonly DependencyProperty BucketsProperty =
            DependencyProperty.Register(nameof(Buckets), typeof(ObservableCollection<BucketViewModel>),
                typeof(KanbanBoardControl), new PropertyMetadata(null, OnBucketsChanged));

        public ObservableCollection<BucketViewModel> Buckets
        {
            get => (ObservableCollection<BucketViewModel>)GetValue(BucketsProperty);
            set => SetValue(BucketsProperty, value);
        }

        /// <summary>
        /// 보드 제목
        /// </summary>
        public static readonly DependencyProperty BoardTitleProperty =
            DependencyProperty.Register(nameof(BoardTitle), typeof(string),
                typeof(KanbanBoardControl), new PropertyMetadata("칸반 보드"));

        public string BoardTitle
        {
            get => (string)GetValue(BoardTitleProperty);
            set => SetValue(BoardTitleProperty, value);
        }

        /// <summary>
        /// 카드 클릭 이벤트
        /// </summary>
        public event Action<TaskItemViewModel>? CardClicked;

        /// <summary>
        /// 카드 이동 이벤트 (taskId, targetBucketId)
        /// </summary>
        public event Action<TaskItemViewModel, string>? CardMoved;

        /// <summary>
        /// 버킷에 작업 추가 요청 이벤트
        /// </summary>
        public event Action<BucketViewModel>? AddTaskRequested;

        /// <summary>
        /// 버킷 메뉴 요청 이벤트
        /// </summary>
        public event Action<BucketViewModel>? BucketMenuRequested;

        public KanbanBoardControl()
        {
            InitializeComponent();
            _timelineStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            UpdateTimelineRange();
        }

        private static void OnBucketsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KanbanBoardControl control && e.NewValue is ObservableCollection<BucketViewModel> buckets)
            {
                control.BucketsItemsControl.ItemsSource = buckets;
                control.UpdateTotalTaskCount();
            }
        }

        private void UpdateTotalTaskCount()
        {
            var total = Buckets?.Sum(b => b.Tasks.Count) ?? 0;
            TotalTaskCountText.Text = total.ToString();
        }

        #region 뷰 전환

        private void KanbanViewButton_Click(object sender, RoutedEventArgs e)
        {
            BoardScrollViewer.Visibility = Visibility.Visible;
            TimelineView.Visibility = Visibility.Collapsed;
            KanbanViewButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            TimelineViewButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        }

        private void TimelineViewButton_Click(object sender, RoutedEventArgs e)
        {
            BoardScrollViewer.Visibility = Visibility.Collapsed;
            TimelineView.Visibility = Visibility.Visible;
            KanbanViewButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
            TimelineViewButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            RenderTimeline();
        }

        #endregion

        #region 필터

        private void PriorityFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 필터 로직 — 선택된 우선순위에 따라 카드 표시/숨김
            _log.Debug("우선순위 필터 변경");
        }

        #endregion

        #region 드래그&드롭

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;

            if (sender is FrameworkElement fe && fe.DataContext is TaskItemViewModel task)
            {
                _draggedTask = task;
            }
        }

        private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedTask == null)
                return;

            var currentPos = e.GetPosition(null);
            var diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                var data = new DataObject("PlannerTask", _draggedTask);
                DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
            }
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging && _draggedTask != null)
            {
                CardClicked?.Invoke(_draggedTask);
            }
            _isDragging = false;
            _draggedTask = null;
        }

        private void Bucket_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PlannerTask") && sender is Border border)
            {
                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6BB700"));
                border.BorderThickness = new Thickness(2);
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
        }

        private void Bucket_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
            }
        }

        private void Bucket_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
            }

            if (e.Data.GetDataPresent("PlannerTask") &&
                sender is FrameworkElement fe &&
                fe.Tag is BucketViewModel targetBucket)
            {
                var task = (TaskItemViewModel)e.Data.GetData("PlannerTask");
                if (task.BucketId != targetBucket.Id)
                {
                    _log.Info("카드 이동: {0} → {1}", task.Title, targetBucket.Name);
                    CardMoved?.Invoke(task, targetBucket.Id);
                }
            }
            e.Handled = true;
        }

        #endregion

        #region 버킷 액션

        private void BucketAddTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is BucketViewModel bucket)
            {
                AddTaskRequested?.Invoke(bucket);
            }
        }

        private void BucketMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is BucketViewModel bucket)
            {
                BucketMenuRequested?.Invoke(bucket);
            }
        }

        #endregion

        #region 수평 스크롤

        private void BoardScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        #endregion

        #region 타임라인

        private void UpdateTimelineRange()
        {
            var end = _timelineStart.AddDays(_timelineWeeks * 7);
            TimelineRangeText.Text = $"{_timelineStart:yyyy.MM.dd} — {end:yyyy.MM.dd}";
        }

        private void TimelinePrev_Click(object sender, RoutedEventArgs e)
        {
            _timelineStart = _timelineStart.AddDays(-7);
            UpdateTimelineRange();
            RenderTimeline();
        }

        private void TimelineNext_Click(object sender, RoutedEventArgs e)
        {
            _timelineStart = _timelineStart.AddDays(7);
            UpdateTimelineRange();
            RenderTimeline();
        }

        private void TimelineToday_Click(object sender, RoutedEventArgs e)
        {
            _timelineStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            UpdateTimelineRange();
            RenderTimeline();
        }

        private void RenderTimeline()
        {
            TimelineCanvas.Children.Clear();

            if (Buckets == null || Buckets.Count == 0)
                return;

            var allTasks = Buckets.SelectMany(b => b.Tasks)
                .Where(t => t.StartDateTime.HasValue || t.DueDateTime.HasValue)
                .OrderBy(t => t.StartDateTime ?? t.DueDateTime)
                .ToList();

            double rowHeight = 36;
            double dayWidth = 40;
            int totalDays = _timelineWeeks * 7;
            var endDate = _timelineStart.AddDays(totalDays);

            TimelineCanvas.Width = totalDays * dayWidth + 200;
            TimelineCanvas.Height = Math.Max(400, allTasks.Count * rowHeight + 60);

            // UIElement 일괄 수집 후 한 번에 Add (레이아웃 패스 최소화)
            var items = new System.Collections.Generic.List<UIElement>(totalDays * 2 + allTasks.Count * 2 + 1);

            // 날짜 헤더 그리기
            for (int d = 0; d < totalDays; d++)
            {
                var date = _timelineStart.AddDays(d);
                var x = 200 + d * dayWidth;

                // 주말 배경
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                {
                    var bg = new System.Windows.Shapes.Rectangle
                    {
                        Width = dayWidth,
                        Height = TimelineCanvas.Height,
                        Fill = _weekendBrush
                    };
                    Canvas.SetLeft(bg, x);
                    Canvas.SetTop(bg, 0);
                    items.Add(bg);
                }

                // 날짜 라벨
                if (date.Day == 1 || d == 0 || date.DayOfWeek == DayOfWeek.Monday)
                {
                    var label = new TextBlock
                    {
                        Text = date.ToString("MM/dd"),
                        FontSize = 10,
                        Foreground = _dateLabelBrush
                    };
                    Canvas.SetLeft(label, x);
                    Canvas.SetTop(label, 2);
                    items.Add(label);
                }
            }

            // 오늘 표시선
            if (DateTime.Today >= _timelineStart && DateTime.Today < endDate)
            {
                var todayX = 200 + (DateTime.Today - _timelineStart).Days * dayWidth;
                var todayLine = new System.Windows.Shapes.Line
                {
                    X1 = todayX, Y1 = 20,
                    X2 = todayX, Y2 = TimelineCanvas.Height,
                    Stroke = _todayLineBrush,
                    StrokeThickness = 2
                };
                items.Add(todayLine);
            }

            // 작업 바 그리기
            for (int i = 0; i < allTasks.Count; i++)
            {
                var task = allTasks[i];
                var y = 30 + i * rowHeight;

                // 작업 이름 (좌측)
                var nameText = new TextBlock
                {
                    Text = task.Title,
                    FontSize = 12,
                    Width = 190,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas.SetLeft(nameText, 4);
                Canvas.SetTop(nameText, y + 4);
                items.Add(nameText);

                // 바 그리기
                var start = task.StartDateTime ?? task.DueDateTime ?? DateTime.Today;
                var end = task.DueDateTime ?? start.AddDays(1);
                if (end < start) end = start.AddDays(1);

                var barStart = Math.Max(0, (start - _timelineStart).Days);
                var barEnd = Math.Min(totalDays, (end - _timelineStart).Days + 1);

                if (barEnd > barStart)
                {
                    var barX = 200 + barStart * dayWidth;
                    var barWidth = (barEnd - barStart) * dayWidth;

                    var bar = new Border
                    {
                        Width = barWidth,
                        Height = 24,
                        CornerRadius = new CornerRadius(4),
                        Background = task.PriorityColor,
                        Opacity = task.IsComplete ? 0.4 : 0.85,
                        ToolTip = $"{task.Title}\n{start:MM/dd} — {end:MM/dd}\n{task.PriorityDisplay}"
                    };

                    // 바 내부 텍스트
                    if (barWidth > 60)
                    {
                        bar.Child = new TextBlock
                        {
                            Text = task.Title,
                            FontSize = 10,
                            Foreground = _barTextBrush,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(6, 0, 6, 0)
                        };
                    }

                    Canvas.SetLeft(bar, barX);
                    Canvas.SetTop(bar, y + 2);
                    items.Add(bar);
                }
            }

            // 일괄 추가
            foreach (var item in items)
                TimelineCanvas.Children.Add(item);

            _log.Debug("타임라인 렌더 완료: {Count}개 작업", allTasks.Count);
        }

        #endregion
    }
}
