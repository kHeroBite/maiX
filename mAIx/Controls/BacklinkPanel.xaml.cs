using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Serilog;

namespace mAIx.Controls
{
    /// <summary>
    /// 백링크 아이템 — 현재 페이지를 참조하는 페이지 정보
    /// </summary>
    public class BacklinkItem
    {
        public string PageId { get; set; } = "";
        public string Title { get; set; } = "";
        public string NotebookName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string? PreviewText { get; set; }
    }

    /// <summary>
    /// OneNote 백링크 패널 — 현재 페이지를 참조하는 다른 페이지 목록
    /// </summary>
    public partial class BacklinkPanel : UserControl
    {
        private static readonly ILogger _log = Log.ForContext<BacklinkPanel>();

        /// <summary>
        /// 백링크 목록
        /// </summary>
        public static readonly DependencyProperty BacklinksProperty =
            DependencyProperty.Register(nameof(Backlinks), typeof(ObservableCollection<BacklinkItem>),
                typeof(BacklinkPanel), new PropertyMetadata(null, OnBacklinksChanged));

        public ObservableCollection<BacklinkItem> Backlinks
        {
            get => (ObservableCollection<BacklinkItem>)GetValue(BacklinksProperty);
            set => SetValue(BacklinksProperty, value);
        }

        /// <summary>
        /// 로딩 중 여부
        /// </summary>
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(nameof(IsLoading), typeof(bool),
                typeof(BacklinkPanel), new PropertyMetadata(false, OnIsLoadingChanged));

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        /// <summary>
        /// 백링크 클릭 시 해당 페이지로 이동 이벤트
        /// </summary>
        public event Action<BacklinkItem>? BacklinkNavigated;

        /// <summary>
        /// 새로고침 요청 이벤트
        /// </summary>
        public event Action? RefreshRequested;

        public BacklinkPanel()
        {
            InitializeComponent();
        }

        private static void OnBacklinksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BacklinkPanel panel && e.NewValue is ObservableCollection<BacklinkItem> items)
            {
                panel.BacklinksListView.ItemsSource = items;
                panel.BacklinkCountText.Text = items.Count.ToString();
                panel.EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                panel.BacklinksListView.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BacklinkPanel panel && e.NewValue is bool isLoading)
            {
                panel.LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshRequested?.Invoke();
        }

        private void BacklinksListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BacklinksListView.SelectedItem is BacklinkItem item)
            {
                _log.Debug("백링크 클릭: {Title}", item.Title);
                BacklinkNavigated?.Invoke(item);
                BacklinksListView.SelectedItem = null;
            }
        }
    }
}
