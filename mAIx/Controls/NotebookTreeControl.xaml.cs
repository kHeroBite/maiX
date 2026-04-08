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
    /// 노트북 트리 탐색 컨트롤 — 노트북 > 섹션 > 페이지 계층 TreeView
    /// </summary>
    public partial class NotebookTreeControl : UserControl
    {
        private static readonly ILogger _log = Log.ForContext<NotebookTreeControl>();

        /// <summary>
        /// 노트북 목록 (바인딩)
        /// </summary>
        public static readonly DependencyProperty NotebooksProperty =
            DependencyProperty.Register(nameof(Notebooks), typeof(ObservableCollection<NotebookItemViewModel>),
                typeof(NotebookTreeControl), new PropertyMetadata(null, OnNotebooksChanged));

        public ObservableCollection<NotebookItemViewModel> Notebooks
        {
            get => (ObservableCollection<NotebookItemViewModel>)GetValue(NotebooksProperty);
            set => SetValue(NotebooksProperty, value);
        }

        /// <summary>
        /// 페이지 선택 이벤트
        /// </summary>
        public event Action<PageItemViewModel>? PageSelected;

        /// <summary>
        /// 섹션 선택 이벤트
        /// </summary>
        public event Action<SectionItemViewModel>? SectionSelected;

        /// <summary>
        /// 노트북 선택 이벤트
        /// </summary>
        public event Action<NotebookItemViewModel>? NotebookSelected;

        /// <summary>
        /// 이름 변경 요청 이벤트
        /// </summary>
        public event Action<object>? RenameRequested;

        /// <summary>
        /// 삭제 요청 이벤트
        /// </summary>
        public event Action<object>? DeleteRequested;

        /// <summary>
        /// 이동 요청 이벤트
        /// </summary>
        public event Action<object>? MoveRequested;

        public NotebookTreeControl()
        {
            InitializeComponent();
        }

        private static void OnNotebooksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NotebookTreeControl control && e.NewValue is ObservableCollection<NotebookItemViewModel> notebooks)
            {
                control.NotebookTreeView.ItemsSource = notebooks;
                control.NotebookTreeView.ItemTemplate = (HierarchicalDataTemplate)control.NotebookTreeView.Resources["NotebookTemplate"];
            }
        }

        #region 트리 선택

        private void NotebookTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            switch (e.NewValue)
            {
                case PageItemViewModel page:
                    _log.Debug("페이지 선택: {Title}", page.Title);
                    PageSelected?.Invoke(page);
                    break;

                case SectionItemViewModel section:
                    _log.Debug("섹션 선택: {Name}", section.DisplayName);
                    SectionSelected?.Invoke(section);
                    break;

                case NotebookItemViewModel notebook:
                    _log.Debug("노트북 선택: {Name}", notebook.DisplayName);
                    NotebookSelected?.Invoke(notebook);
                    break;
            }
        }

        #endregion

        #region 검색

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Text = "";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(query))
            {
                // 검색 초기화 — 모든 항목 표시
                if (Notebooks != null)
                {
                    NotebookTreeView.ItemsSource = Notebooks;
                }
                return;
            }

            // 검색 필터: 노트북/섹션/페이지명에 쿼리 포함
            if (Notebooks == null) return;

            var filtered = new ObservableCollection<NotebookItemViewModel>();
            foreach (var nb in Notebooks)
            {
                if (nb.DisplayName.ToLowerInvariant().Contains(query))
                {
                    filtered.Add(nb);
                    continue;
                }

                // 섹션/페이지 검색
                var hasMatch = nb.Sections.Any(s =>
                    s.DisplayName.ToLowerInvariant().Contains(query) ||
                    s.Pages.Any(p => p.Title.ToLowerInvariant().Contains(query)));

                if (hasMatch)
                {
                    filtered.Add(nb);
                }
            }

            NotebookTreeView.ItemsSource = filtered;
        }

        #endregion

        #region 컨텍스트 메뉴

        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                item.IsSelected = true;
                e.Handled = true;
            }
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            var selected = NotebookTreeView.SelectedItem;
            if (selected != null)
            {
                RenameRequested?.Invoke(selected);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var selected = NotebookTreeView.SelectedItem;
            if (selected != null)
            {
                DeleteRequested?.Invoke(selected);
            }
        }

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            var selected = NotebookTreeView.SelectedItem;
            if (selected != null)
            {
                MoveRequested?.Invoke(selected);
            }
        }

        #endregion

        /// <summary>
        /// 특정 페이지를 선택 상태로 강조
        /// </summary>
        public void HighlightPage(string pageId)
        {
            if (Notebooks == null) return;

            foreach (var nb in Notebooks)
            {
                foreach (var section in nb.Sections)
                {
                    var page = section.Pages.FirstOrDefault(p => p.Id == pageId);
                    if (page != null)
                    {
                        nb.IsExpanded = true;
                        // SectionItemViewModel은 IsExpanded 프로퍼티가 없으므로 노트북만 확장
                        _log.Debug("페이지 강조: {Title}", page.Title);
                        return;
                    }
                }
            }
        }
    }
}
