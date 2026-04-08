using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.ViewModels;
using Serilog;

namespace mAIx.Controls
{
    /// <summary>
    /// 파일 그리드/리스트 뷰 전환 컨트롤
    /// </summary>
    public partial class FileGridControl : UserControl
    {
        private static readonly ILogger _log = Log.ForContext<FileGridControl>();
        private bool _isGridView;
        private DateTime _lastClickTime;
        private DriveItemViewModel? _lastClickedItem;

        /// <summary>
        /// 아이템 소스
        /// </summary>
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(ObservableCollection<DriveItemViewModel>),
                typeof(FileGridControl), new PropertyMetadata(null, OnItemsSourceChanged));

        public ObservableCollection<DriveItemViewModel>? ItemsSource
        {
            get => (ObservableCollection<DriveItemViewModel>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>
        /// 그리드 뷰 모드 여부
        /// </summary>
        public bool IsGridView
        {
            get => _isGridView;
            set
            {
                _isGridView = value;
                UpdateViewMode();
            }
        }

        /// <summary>
        /// 선택 변경 이벤트
        /// </summary>
        public event EventHandler<DriveItemViewModel?>? SelectionChanged;

        /// <summary>
        /// 아이템 더블클릭 이벤트
        /// </summary>
        public event EventHandler<DriveItemViewModel>? ItemDoubleClicked;

        /// <summary>
        /// 선택된 아이템
        /// </summary>
        public DriveItemViewModel? SelectedItem =>
            FileListView.SelectedItem as DriveItemViewModel;

        public FileGridControl()
        {
            InitializeComponent();
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FileGridControl ctrl)
            {
                ctrl.FileListView.ItemsSource = e.NewValue as System.Collections.IEnumerable;
                ctrl.FileGridItems.ItemsSource = e.NewValue as System.Collections.IEnumerable;
            }
        }

        private void UpdateViewMode()
        {
            if (_isGridView)
            {
                FileListView.Visibility = Visibility.Collapsed;
                FileGridScrollViewer.Visibility = Visibility.Visible;
            }
            else
            {
                FileListView.Visibility = Visibility.Visible;
                FileGridScrollViewer.Visibility = Visibility.Collapsed;
            }
        }

        private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectionChanged?.Invoke(this, FileListView.SelectedItem as DriveItemViewModel);
        }

        private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileListView.SelectedItem is DriveItemViewModel item)
            {
                ItemDoubleClicked?.Invoke(this, item);
            }
        }

        private void GridItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DriveItemViewModel item)
            {
                var now = DateTime.Now;

                // 더블클릭 감지 (500ms 이내 같은 아이템)
                if (_lastClickedItem == item && (now - _lastClickTime).TotalMilliseconds < 500)
                {
                    ItemDoubleClicked?.Invoke(this, item);
                    _lastClickedItem = null;
                }
                else
                {
                    SelectionChanged?.Invoke(this, item);
                    _lastClickedItem = item;
                    _lastClickTime = now;
                }
            }
        }

        private void GridItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 선택 시각 피드백용 (필요시 구현)
        }
    }
}
