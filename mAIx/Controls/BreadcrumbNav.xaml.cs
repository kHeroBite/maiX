using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using mAIx.ViewModels;
using Serilog;

namespace mAIx.Controls
{
    /// <summary>
    /// 경로 탐색 브레드크럼 컨트롤
    /// </summary>
    public partial class BreadcrumbNav : UserControl
    {
        private static readonly ILogger _log = Log.ForContext<BreadcrumbNav>();

        /// <summary>
        /// 브레드크럼 아이템 목록
        /// </summary>
        public ObservableCollection<BreadcrumbItem> Items
        {
            get => (ObservableCollection<BreadcrumbItem>)GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(nameof(Items), typeof(ObservableCollection<BreadcrumbItem>),
                typeof(BreadcrumbNav), new PropertyMetadata(null, OnItemsChanged));

        /// <summary>
        /// 경로 클릭 이벤트 (BreadcrumbItem)
        /// </summary>
        public event EventHandler<BreadcrumbItem>? PathClicked;

        /// <summary>
        /// 홈 클릭 이벤트
        /// </summary>
        public event EventHandler? HomeClicked;

        /// <summary>
        /// 상위 폴더 클릭 이벤트
        /// </summary>
        public event EventHandler? GoUpClicked;

        public BreadcrumbNav()
        {
            InitializeComponent();
        }

        private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BreadcrumbNav nav)
            {
                nav.BreadcrumbItems.ItemsSource = e.NewValue as ObservableCollection<BreadcrumbItem>;
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            HomeClicked?.Invoke(this, EventArgs.Empty);
        }

        private void GoUpButton_Click(object sender, RoutedEventArgs e)
        {
            GoUpClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BreadcrumbItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is BreadcrumbItem item)
            {
                PathClicked?.Invoke(this, item);
            }
        }
    }
}
