using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui;
using Wpf.Ui.Controls;
using mailX.Models;
using mailX.Models.Settings;
using mailX.Utils;
using mailX.ViewModels;
using mailX.Views.Dialogs;

namespace mailX.Views;

/// <summary>
/// 메인 윈도우 - 3단 레이아웃 (폴더트리 | 메일리스트 | 본문+AI)
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private Folder? _rightClickedFolder;
    private bool _webView2Initialized;

    // 드래그&드롭용 변수
    private Point _dragStartPoint;
    private Folder? _draggedFolder;

    // 최근 검색어 (최대 10개)
    private readonly ObservableCollection<string> _recentSearches = new();
    private const int MaxRecentSearches = 10;

    public MainWindow(MainViewModel viewModel)
    {
        Log4.Debug("MainWindow 생성자 시작");
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // 검색 폴더 옵션 초기화
        _viewModel.InitializeSearchFolderOptions();

        // 최근 검색어 로드 및 바인딩
        LoadRecentSearches();
        RecentSearchItems.ItemsSource = _recentSearches;

        // 타이틀바 설정
        TitleBar.CloseClicked += (_, _) =>
        {
            Log4.Debug("MainWindow 닫기 버튼 클릭됨");
            Close();
        };

        // 테마 변경 시 메일 목록 새로고침 (글자색 업데이트) + WebView2 테마 갱신 + Mica 백드롭 재적용
        Services.Theme.ThemeService.Instance.ThemeChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                // Mica 백드롭 재적용 (테마 전환 시 유지되도록)
                WindowBackdrop.RemoveBackground(this);
                WindowBackdrop.ApplyBackdrop(this, WindowBackdropType.Mica);

                // CollectionView 새로고침으로 컨버터 재평가
                if (EmailListBox.ItemsSource != null)
                {
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(EmailListBox.ItemsSource);
                    view?.Refresh();
                }

                // WebView2 테마 업데이트
                if (_viewModel.SelectedEmail != null)
                {
                    LoadMailBodyAsync(_viewModel.SelectedEmail);
                }
            });
        };

        // SelectedEmail 변경 감지
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedEmail))
            {
                LoadMailBodyAsync(_viewModel.SelectedEmail);
            }
        };

        // 캘린더 데이터 업데이트 시 뷰 새로고침
        _viewModel.CalendarDataUpdated += () =>
        {
            Dispatcher.Invoke(async () =>
            {
                // 캘린더 모드일 때만 새로고침
                if (CalendarViewBorder?.Visibility == Visibility.Visible)
                {
                    Log4.Info("캘린더 동기화 완료 - 뷰 새로고침");
                    await LoadMonthEventsAsync(_currentCalendarDate);
                    UpdateCalendarDisplay();
                }
            });
        };

        // WebView2 초기화
        InitializeWebView2Async();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;
        Log4.Debug("MainWindow 생성자 완료");
    }

    /// <summary>
    /// 창 크기 변경 시 검색창 너비 조절
    /// </summary>
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSearchBoxWidth();
    }

    /// <summary>
    /// 검색창 너비를 창 크기에 맞게 조절
    /// </summary>
    private void UpdateSearchBoxWidth()
    {
        // 타이틀바 고정 요소들의 너비 합계
        // 아이콘(18) + 로고(80) + 메뉴(250) + 폴더콤보(144) + 고급검색버튼(34) + 우측버튼들(5*34=170) + 창버튼(138) + 여백(86)
        const double fixedWidth = 920;
        const double minSearchWidth = 100;
        const double maxSearchWidth = 300;

        double availableWidth = ActualWidth - fixedWidth;
        double searchWidth = Math.Max(minSearchWidth, Math.Min(maxSearchWidth, availableWidth));

        if (TitleBarSearchBox != null)
        {
            TitleBarSearchBox.Width = searchWidth;
        }
    }

    /// <summary>
    /// WebView2 초기화
    /// </summary>
    private async void InitializeWebView2Async()
    {
        try
        {
            await MailBodyWebView.EnsureCoreWebView2Async();
            _webView2Initialized = true;

            // WebView2 설정
            MailBodyWebView.CoreWebView2.Settings.IsScriptEnabled = true;
            MailBodyWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            MailBodyWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            MailBodyWebView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;

            Log4.Debug("WebView2 초기화 완료");

            // 이미 선택된 메일이 있으면 로드
            if (_viewModel.SelectedEmail != null)
            {
                LoadMailBodyAsync(_viewModel.SelectedEmail);
            }
        }
        catch (System.Exception ex)
        {
            Log4.Error($"WebView2 초기화 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 메일 본문을 WebView2에 로드
    /// </summary>
    private async void LoadMailBodyAsync(Email? email)
    {
        if (!_webView2Initialized || MailBodyWebView.CoreWebView2 == null)
            return;

        if (email == null || string.IsNullOrEmpty(email.Body))
        {
            await MailBodyWebView.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML = ''");
            return;
        }

        try
        {
            // 테마에 따른 스타일 결정
            var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            var isDark = theme == Wpf.Ui.Appearance.ApplicationTheme.Dark;
            var bgColor = isDark ? "#1e1e1e" : "#ffffff";
            var textColor = isDark ? "#e0e0e0" : "#1e1e1e";
            var scrollbarThumbColor = isDark ? "#555555" : "#c0c0c0";
            var scrollbarThumbHoverColor = isDark ? "#777777" : "#a0a0a0";
            var scrollbarTrackColor = isDark ? "#2d2d2d" : "#f0f0f0";

            string htmlContent;
            if (email.IsHtml)
            {
                // HTML 메일: 스타일 래핑
                // 다크모드일 때 인라인 스타일을 덮어쓰기 위해 !important 사용
                var darkModeOverride = isDark ? @"
        /* 다크모드: 인라인 스타일 강제 덮어쓰기 */
        body, p, div, span, td, th, li, h1, h2, h3, h4, h5, h6, font, blockquote, pre, code {
            color: #e0e0e0 !important;
            background-color: transparent !important;
        }
        /* 이미지 배경 제외 (투명하게) */
        img { background-color: transparent !important; }
        a { color: #6db3f2 !important; }
        table { border-color: #444444 !important; background-color: transparent !important; }
        td, th { border-color: #444444 !important; background-color: transparent !important; }
        /* 강제 배경색 리셋 */
        [style*='background'] { background-color: transparent !important; background: transparent !important; }
        [bgcolor] { background-color: transparent !important; }
" : "";

                htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{
            font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 20px;
            margin: 0;
            background-color: {bgColor} !important;
            color: {textColor};
        }}
        img {{ max-width: 100%; height: auto; }}
        a {{ color: #0078d4; }}
        table {{ border-collapse: collapse; }}
        td, th {{ padding: 8px; }}
        /* 스크롤바 스타일 (Webkit 브라우저용) */
        ::-webkit-scrollbar {{ width: 12px; height: 12px; }}
        ::-webkit-scrollbar-track {{ background: {scrollbarTrackColor}; border-radius: 6px; }}
        ::-webkit-scrollbar-thumb {{ background: {scrollbarThumbColor}; border-radius: 6px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: {scrollbarThumbHoverColor}; }}
        {darkModeOverride}
    </style>
</head>
<body>
{email.Body}
</body>
</html>";
            }
            else
            {
                // 텍스트 메일: pre 태그로 래핑
                var escapedBody = WebUtility.HtmlEncode(email.Body);
                htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{
            font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 20px;
            margin: 0;
            background-color: {bgColor};
            color: {textColor};
        }}
        pre {{
            white-space: pre-wrap;
            word-wrap: break-word;
            font-family: inherit;
            margin: 0;
        }}
        /* 스크롤바 스타일 (Webkit 브라우저용) */
        ::-webkit-scrollbar {{ width: 12px; height: 12px; }}
        ::-webkit-scrollbar-track {{ background: {scrollbarTrackColor}; border-radius: 6px; }}
        ::-webkit-scrollbar-thumb {{ background: {scrollbarThumbColor}; border-radius: 6px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: {scrollbarThumbHoverColor}; }}
    </style>
</head>
<body>
<pre>{escapedBody}</pre>
</body>
</html>";
            }

            MailBodyWebView.CoreWebView2.NavigateToString(htmlContent);
        }
        catch (System.Exception ex)
        {
            Log4.Error($"메일 본문 로드 실패: {ex.Message}");
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Log4.Debug("MainWindow_Loaded 시작");

        // GPU 모드 체크마크 초기화
        UpdateGpuModeCheckmark();

        // 저장된 동기화 설정 로드
        LoadSavedSyncSettings();

        // 동기화 기간 현재 설정 표시 초기화
        UpdateSyncPeriodCurrentDisplay();

        // 자동 로그인 메뉴 상태 초기화
        InitializeAutoLoginMenu();

        // 테마 아이콘 초기화
        UpdateThemeIcon();

        // 검색창 초기 크기 설정
        UpdateSearchBoxWidth();

        // 검색 자동완성 팝업 닫기를 위한 전역 클릭 이벤트
        PreviewMouseDown += MainWindow_PreviewMouseDown;

        // 윈도우 비활성화 시 팝업 닫기
        Deactivated += (s, args) => SearchAutocompletePopup.IsOpen = false;

        // 폴더 목록 초기 로드
        await _viewModel.LoadFoldersCommand.ExecuteAsync(null);
        Log4.Debug("MainWindow_Loaded 완료");
    }

    /// <summary>
    /// 전역 마우스 클릭 시 검색 자동완성 팝업 닫기
    /// </summary>
    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 검색창 또는 팝업 내부 클릭이 아니면 팝업 닫기
        if (SearchAutocompletePopup.IsOpen)
        {
            var clickedElement = e.OriginalSource as DependencyObject;

            // 검색창 내부 클릭인지 확인
            if (IsDescendantOf(clickedElement, TitleBarSearchBox))
                return;

            // 팝업 내부 클릭인지 확인
            if (IsDescendantOf(clickedElement, SearchAutocompletePopup.Child))
                return;

            SearchAutocompletePopup.IsOpen = false;
        }
    }

    /// <summary>
    /// 요소가 부모의 자손인지 확인
    /// </summary>
    private static bool IsDescendantOf(DependencyObject? element, DependencyObject? parent)
    {
        if (element == null || parent == null)
            return false;

        var current = element;
        while (current != null)
        {
            if (current == parent)
                return true;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void MainWindow_Closed(object? sender, System.EventArgs e)
    {
        Log4.Debug("MainWindow_Closed - 애플리케이션 종료");
        // OnExplicitShutdown 모드에서는 명시적으로 종료 호출 필요
        Application.Current.Shutdown();
    }

    /// <summary>
    /// TreeView 폴더 선택 이벤트 핸들러
    /// </summary>
    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is Folder selectedFolder)
        {
            _viewModel.SelectedFolder = selectedFolder;
            // 즐겨찾기 ListBox 선택 해제
            ClearFavoriteListBoxSelection();
        }
    }

    /// <summary>
    /// 즐겨찾기 ListBox 선택 해제
    /// </summary>
    private void ClearFavoriteListBoxSelection()
    {
        var favoriteListBox = FindName("FavoriteListBox") as System.Windows.Controls.ListBox;
        if (favoriteListBox != null)
        {
            favoriteListBox.SelectedItem = null;
        }
    }

    /// <summary>
    /// TreeView 선택 해제
    /// </summary>
    private void ClearTreeViewSelection()
    {
        if (FolderTreeView.SelectedItem != null)
        {
            // 재귀적으로 선택된 TreeViewItem 찾아서 해제
            ClearTreeViewItemSelection(FolderTreeView);
        }
    }

    /// <summary>
    /// TreeView의 모든 항목에서 선택 해제 (재귀)
    /// </summary>
    private void ClearTreeViewItemSelection(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.TreeViewItem;
            if (container != null)
            {
                if (container.IsSelected)
                {
                    container.IsSelected = false;
                }
                // 자식 항목도 재귀적으로 확인
                if (container.HasItems)
                {
                    ClearTreeViewItemSelection(container);
                }
            }
        }
    }

    /// <summary>
    /// 즐겨찾기 ListBox 선택 변경 이벤트
    /// </summary>
    private void FavoriteListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox && listBox.SelectedItem is Folder folder)
        {
            _viewModel.SelectedFolder = folder;
            // TreeView 선택 해제
            ClearTreeViewSelection();
        }
    }

    /// <summary>
    /// TreeViewItem 우클릭 시 폴더 컨텍스트 메뉴 표시
    /// </summary>
    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.TreeViewItem treeViewItem &&
            treeViewItem.DataContext is Folder folder)
        {
            _rightClickedFolder = folder;
            treeViewItem.IsSelected = true; // 우클릭한 항목 선택

            // 동적으로 컨텍스트 메뉴 생성
            var contextMenu = new System.Windows.Controls.ContextMenu
            {
                Background = (Brush)FindResource("ApplicationBackgroundBrush"),
                BorderBrush = (Brush)FindResource("ControlElevationBorderBrush"),
                Padding = new Thickness(4)
            };

            // 하위 폴더 만들기
            var createItem = new System.Windows.Controls.MenuItem
            {
                Header = "📁 하위 폴더 만들기",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            createItem.Click += FolderCreate_Click;
            contextMenu.Items.Add(createItem);

            // 이름 바꾸기
            var renameItem = new System.Windows.Controls.MenuItem
            {
                Header = "✏️ 이름 바꾸기",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            renameItem.Click += FolderRename_Click;
            contextMenu.Items.Add(renameItem);

            // 즐겨찾기 추가/제거
            var favoriteItem = new System.Windows.Controls.MenuItem
            {
                Header = folder.IsFavorite ? "⭐ 즐겨찾기 해제" : "⭐ 즐겨찾기 추가",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            favoriteItem.Click += (s, args) => _viewModel.ToggleFavoriteCommand.Execute(folder);
            contextMenu.Items.Add(favoriteItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator
            {
                Background = (Brush)FindResource("ControlElevationBorderBrush")
            });

            // 삭제
            var deleteItem = new System.Windows.Controls.MenuItem
            {
                Header = "🗑️ 삭제",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            deleteItem.Click += FolderDelete_Click;
            contextMenu.Items.Add(deleteItem);

            // 컨텍스트 메뉴 표시
            treeViewItem.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;

            e.Handled = true;
        }
    }

    /// <summary>
    /// 즐겨찾기 추가 (폴더 트리에서)
    /// </summary>
    private void AddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null && !_rightClickedFolder.IsFavorite)
        {
            _viewModel.ToggleFavoriteCommand.Execute(_rightClickedFolder);
        }
    }

    #region 폴더 CRUD 이벤트 핸들러

    /// <summary>
    /// 하위 폴더 만들기
    /// </summary>
    private async void FolderCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder == null)
        {
            Log4.Warn("폴더 생성 실패: 선택된 폴더 없음");
            return;
        }

        // 폴더 이름 입력 다이얼로그 (간단한 InputBox 대용)
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "새 폴더 만들기",
            Content = new System.Windows.Controls.TextBox
            {
                Name = "FolderNameInput",
                Width = 300,
                Text = "새 폴더",
                SelectionStart = 0,
                SelectionLength = 4
            },
            PrimaryButtonText = "만들기",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            var textBox = dialog.Content as System.Windows.Controls.TextBox;
            var folderName = textBox?.Text?.Trim();

            if (!string.IsNullOrEmpty(folderName))
            {
                Log4.Info($"폴더 생성 요청: '{folderName}' (상위: {_rightClickedFolder.DisplayName})");
                // 선택된 폴더를 설정한 후 Command 호출
                _viewModel.SelectedFolder = _rightClickedFolder;
                if (_viewModel.CreateFolderCommand.CanExecute(folderName))
                {
                    await _viewModel.CreateFolderCommand.ExecuteAsync(folderName);
                }
            }
        }
    }

    /// <summary>
    /// 폴더 이름 바꾸기
    /// </summary>
    private async void FolderRename_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder == null)
        {
            Log4.Warn("폴더 이름 변경 실패: 선택된 폴더 없음");
            return;
        }

        // 시스템 폴더는 이름 변경 불가
        if (IsSystemFolder(_rightClickedFolder.DisplayName))
        {
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "알림",
                Content = "시스템 폴더는 이름을 변경할 수 없습니다.",
                CloseButtonText = "확인"
            }.ShowDialogAsync();
            return;
        }

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "폴더 이름 바꾸기",
            Content = new System.Windows.Controls.TextBox
            {
                Name = "FolderNameInput",
                Width = 300,
                Text = _rightClickedFolder.DisplayName
            },
            PrimaryButtonText = "변경",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            var textBox = dialog.Content as System.Windows.Controls.TextBox;
            var newName = textBox?.Text?.Trim();

            if (!string.IsNullOrEmpty(newName) && newName != _rightClickedFolder.DisplayName)
            {
                Log4.Info($"폴더 이름 변경 요청: '{_rightClickedFolder.DisplayName}' → '{newName}'");
                var args = (_rightClickedFolder, newName);
                if (_viewModel.RenameFolderCommand.CanExecute(args))
                {
                    await _viewModel.RenameFolderCommand.ExecuteAsync(args);
                }
            }
        }
    }

    /// <summary>
    /// 폴더 삭제
    /// </summary>
    private async void FolderDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder == null)
        {
            Log4.Warn("폴더 삭제 실패: 선택된 폴더 없음");
            return;
        }

        // 시스템 폴더는 삭제 불가
        if (IsSystemFolder(_rightClickedFolder.DisplayName))
        {
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "알림",
                Content = "시스템 폴더는 삭제할 수 없습니다.",
                CloseButtonText = "확인"
            }.ShowDialogAsync();
            return;
        }

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "폴더 삭제",
            Content = $"'{_rightClickedFolder.DisplayName}' 폴더를 삭제하시겠습니까?\n폴더 내 모든 메일이 함께 삭제됩니다.",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            Log4.Info($"폴더 삭제 요청: '{_rightClickedFolder.DisplayName}'");
            if (_viewModel.DeleteFolderCommand.CanExecute(_rightClickedFolder))
            {
                await _viewModel.DeleteFolderCommand.ExecuteAsync(_rightClickedFolder);
            }
        }
    }

    /// <summary>
    /// 시스템 폴더 여부 확인
    /// </summary>
    private bool IsSystemFolder(string folderName)
    {
        var systemFolders = new[] { "받은 편지함", "보낸 편지함", "임시 보관함", "지운 편지함", "정크 메일",
                                     "Inbox", "Sent Items", "Drafts", "Deleted Items", "Junk Email" };
        return systemFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    /// <summary>
    /// 즐겨찾기 ListBox 우클릭 시 해당 폴더 저장
    /// </summary>
    private void FavoriteListBox_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 우클릭한 항목의 DataContext에서 Folder 가져오기
        if (e.OriginalSource is FrameworkElement element)
        {
            var folder = FindParentDataContext<Folder>(element);
            if (folder != null)
            {
                _rightClickedFolder = folder;
                _viewModel.SelectedFolder = folder;
            }
        }
    }

    /// <summary>
    /// 부모 요소에서 DataContext 찾기
    /// </summary>
    private T? FindParentDataContext<T>(FrameworkElement element) where T : class
    {
        var current = element;
        while (current != null)
        {
            if (current.DataContext is T data)
                return data;
            current = current.Parent as FrameworkElement;
        }
        return null;
    }

    /// <summary>
    /// 즐겨찾기 제거 (즐겨찾기 영역에서)
    /// </summary>
    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null && _rightClickedFolder.IsFavorite)
        {
            _viewModel.ToggleFavoriteCommand.Execute(_rightClickedFolder);
        }
    }

    #region 즐겨찾기 드래그&드롭

    /// <summary>
    /// 즐겨찾기 드래그 시작점 기록
    /// </summary>
    private void FavoriteListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    /// <summary>
    /// 즐겨찾기 드래그 시작 감지
    /// </summary>
    private void FavoriteListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPosition = e.GetPosition(null);
        var diff = _dragStartPoint - currentPosition;

        // 최소 드래그 거리 확인
        if (System.Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 드래그 대상 폴더 찾기
        if (e.OriginalSource is FrameworkElement element)
        {
            var folder = FindParentDataContext<Folder>(element);
            if (folder != null)
            {
                _draggedFolder = folder;
                var data = new DataObject(typeof(Folder), folder);
                DragDrop.DoDragDrop(FavoriteListBox, data, DragDropEffects.Move);
            }
        }
    }

    /// <summary>
    /// 즐겨찾기 드래그 오버 (폴더 순서 변경 + 메일 이동)
    /// </summary>
    private void FavoriteListBox_DragOver(object sender, DragEventArgs e)
    {
        // 메일 드래그 처리
        if (e.Data.GetDataPresent("EmailDragData"))
        {
            var element = e.OriginalSource as DependencyObject;
            var listBoxItem = FindAncestor<ListBoxItem>(element);

            if (listBoxItem?.DataContext is Folder)
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
            return;
        }

        // 폴더 순서 변경 처리
        if (!e.Data.GetDataPresent(typeof(Folder)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>
    /// 즐겨찾기 드롭 처리 (폴더 순서 변경 + 메일 이동)
    /// </summary>
    private async void FavoriteListBox_Drop(object sender, DragEventArgs e)
    {
        // 메일 드롭 처리 - 메일을 폴더로 이동
        if (e.Data.GetDataPresent("EmailDragData"))
        {
            var element = e.OriginalSource as DependencyObject;
            var listBoxItem = FindAncestor<ListBoxItem>(element);

            if (listBoxItem?.DataContext is Folder targetFolder)
            {
                var emails = e.Data.GetData("EmailDragData") as List<Email>;
                if (emails != null && emails.Count > 0)
                {
                    Log4.Info($"메일 드롭 (즐겨찾기): {emails.Count}건 → {targetFolder.DisplayName}");
                    await _viewModel.MoveEmailsToFolderAsync(emails, targetFolder);
                }
            }
            e.Handled = true;
            return;
        }

        // 폴더 순서 변경 처리
        if (!e.Data.GetDataPresent(typeof(Folder)))
            return;

        var droppedFolder = e.Data.GetData(typeof(Folder)) as Folder;
        if (droppedFolder == null || _draggedFolder == null)
            return;

        // 드롭 위치의 폴더 찾기
        Folder? targetFolderForOrder = null;
        if (e.OriginalSource is FrameworkElement element2)
        {
            targetFolderForOrder = FindParentDataContext<Folder>(element2);
        }

        // 같은 폴더면 무시
        if (targetFolderForOrder == null || targetFolderForOrder.Id == droppedFolder.Id)
        {
            _draggedFolder = null;
            return;
        }

        // ViewModel에 순서 변경 요청
        _viewModel.MoveFavoriteOrder(droppedFolder, targetFolderForOrder);

        _draggedFolder = null;
    }

    #endregion

    #region 메뉴바 이벤트

    /// <summary>
    /// 메일 새로고침 메뉴 클릭
    /// </summary>
    private async void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 메일 새로고침 클릭");
        await _viewModel.RefreshMailsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// 동기화 일시정지 메뉴 클릭 (기존 - 사용 안함)
    /// </summary>
    private void MenuSyncPause_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 동기화 일시정지 클릭");
        _viewModel.PauseSyncCommand.Execute(null);
    }

    /// <summary>
    /// 동기화 시작 메뉴 클릭 (기존 - 사용 안함)
    /// </summary>
    private void MenuSyncResume_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 동기화 시작 클릭");
        _viewModel.ResumeSyncCommand.Execute(null);
    }

    /// <summary>
    /// 메일 동기화 중지 메뉴 클릭
    /// </summary>
    private void MenuMailSyncPause_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 메일 동기화 중지 클릭");
        _viewModel.PauseSyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsMailSyncPaused = true;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// 메일 동기화 시작 메뉴 클릭
    /// </summary>
    private void MenuMailSyncResume_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 메일 동기화 시작 클릭");
        _viewModel.ResumeSyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsMailSyncPaused = false;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// AI 분석 일시정지 메뉴 클릭
    /// </summary>
    private void MenuAISyncPause_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: AI 분석 일시정지 클릭");
        _viewModel.PauseAISyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsAiAnalysisPaused = true;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// AI 분석 시작 메뉴 클릭
    /// </summary>
    private void MenuAISyncResume_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: AI 분석 시작 클릭");
        _viewModel.ResumeAISyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsAiAnalysisPaused = false;
        App.Settings.SaveUserPreferences();
    }

    #endregion

    #region 접속 메뉴 이벤트

    private readonly Services.Storage.LoginSettingsService _loginSettingsService = new();

    /// <summary>
    /// 자동 로그인 메뉴 클릭
    /// </summary>
    private async void MenuAutoLogin_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 자동 로그인 클릭");

        var graphAuthService = ((App)Application.Current).GetService<Services.Graph.GraphAuthService>();
        if (graphAuthService == null)
        {
            Log4.Error("GraphAuthService를 찾을 수 없습니다.");
            return;
        }

        // 현재 자동 로그인 상태 확인
        var loginSettings = _loginSettingsService.Load();
        var isAutoLoginEnabled = loginSettings?.AutoLogin ?? false;

        if (isAutoLoginEnabled)
        {
            // 자동 로그인 해제
            var result = System.Windows.MessageBox.Show(
                "자동 로그인을 해제하시겠습니까?\n\n다음 실행 시 로그인 창이 표시됩니다.",
                "자동 로그인 해제",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                loginSettings!.AutoLogin = false;
                _loginSettingsService.Save(loginSettings);

                // 토큰 캐시 삭제
                Services.Graph.TokenCacheHelper.ClearCache();

                Log4.Info("자동 로그인 해제됨");
                _viewModel.StatusMessage = "자동 로그인이 해제되었습니다.";
                UpdateAutoLoginMenuState(false);
            }
        }
        else
        {
            // 자동 로그인 설정 - 로그인 창 표시
            var result = System.Windows.MessageBox.Show(
                "자동 로그인을 설정하시겠습니까?\n\n로그인 창이 표시되며, 로그인 성공 시 자동 로그인이 활성화됩니다.",
                "자동 로그인 설정",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    _viewModel.StatusMessage = "로그인 중...";

                    // 기존 토큰 캐시 삭제 후 새로 로그인
                    Services.Graph.TokenCacheHelper.ClearCache();

                    var loginSuccess = await graphAuthService.LoginInteractiveAsync();
                    if (loginSuccess)
                    {
                        // 로그인 성공 - 자동 로그인 설정 저장
                        var newSettings = loginSettings ?? new Models.LoginSettings();
                        newSettings.Email = graphAuthService.CurrentUserEmail;
                        newSettings.DisplayName = graphAuthService.CurrentUserDisplayName;
                        newSettings.AutoLogin = true;
                        newSettings.LastLoginAt = DateTime.Now;

                        // Azure AD 설정도 저장
                        if (!string.IsNullOrEmpty(graphAuthService.ClientId))
                        {
                            newSettings.AzureAd = new Models.Settings.AzureAdSettings
                            {
                                ClientId = graphAuthService.ClientId,
                                TenantId = "common"
                            };
                        }

                        _loginSettingsService.Save(newSettings);

                        Log4.Info($"자동 로그인 설정 완료: {newSettings.Email}");
                        _viewModel.StatusMessage = $"자동 로그인이 설정되었습니다. ({newSettings.Email})";
                        UpdateAutoLoginMenuState(true);
                    }
                    else
                    {
                        Log4.Warn("로그인 취소됨");
                        _viewModel.StatusMessage = "로그인이 취소되었습니다.";
                    }
                }
                catch (Exception ex)
                {
                    Log4.Error($"자동 로그인 설정 실패: {ex.Message}");
                    _viewModel.StatusMessage = "로그인 중 오류가 발생했습니다.";
                }
            }
        }
    }

    /// <summary>
    /// 로그아웃 메뉴 클릭
    /// </summary>
    private async void MenuLogout_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 로그아웃 클릭");

        var result = System.Windows.MessageBox.Show(
            "로그아웃 하시겠습니까?\n\n프로그램이 종료되고 다시 로그인 창이 표시됩니다.",
            "로그아웃",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                var graphAuthService = ((App)Application.Current).GetService<Services.Graph.GraphAuthService>();
                if (graphAuthService != null)
                {
                    await graphAuthService.LogoutAsync();
                }

                // 자동 로그인 해제
                var loginSettings = _loginSettingsService.Load();
                if (loginSettings != null)
                {
                    loginSettings.AutoLogin = false;
                    _loginSettingsService.Save(loginSettings);
                }

                Log4.Info("로그아웃 완료 - 앱 재시작");

                // 앱 재시작 (로그인 창 표시)
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    System.Diagnostics.Process.Start(exePath);
                }

                // 현재 앱 종료
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log4.Error($"로그아웃 실패: {ex.Message}");
                _viewModel.StatusMessage = "로그아웃 중 오류가 발생했습니다.";
            }
        }
    }

    /// <summary>
    /// 자동 로그인 메뉴 체크 상태 업데이트
    /// </summary>
    private void UpdateAutoLoginMenuState(bool isEnabled)
    {
        if (AutoLoginCheckIcon != null)
        {
            AutoLoginCheckIcon.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 자동 로그인 메뉴 초기화 (Loaded 이벤트에서 호출)
    /// </summary>
    private void InitializeAutoLoginMenu()
    {
        var loginSettings = _loginSettingsService.Load();
        var isAutoLoginEnabled = loginSettings?.AutoLogin ?? false;
        UpdateAutoLoginMenuState(isAutoLoginEnabled);
    }

    /// <summary>
    /// 저장된 동기화 설정 로드 및 적용
    /// </summary>
    private void LoadSavedSyncSettings()
    {
        var prefs = App.Settings.UserPreferences;

        // 메일 동기화 기간 설정 로드
        if (Enum.TryParse<SyncPeriodType>(prefs.MailSyncPeriodType, out var mailPeriodType))
        {
            var mailSettings = new SyncPeriodSettings { PeriodType = mailPeriodType, Value = prefs.MailSyncPeriodValue };
            _viewModel.MailSyncPeriodSettings = mailSettings;
            Log4.Debug($"메일 동기화 기간 로드: {mailSettings.ToDisplayString()}");
        }

        // AI 분석 기간 설정 로드
        if (Enum.TryParse<SyncPeriodType>(prefs.AiAnalysisPeriodType, out var aiPeriodType))
        {
            var aiSettings = new SyncPeriodSettings { PeriodType = aiPeriodType, Value = prefs.AiAnalysisPeriodValue };
            _viewModel.AiAnalysisPeriodSettings = aiSettings;
            Log4.Debug($"AI 분석 기간 로드: {aiSettings.ToDisplayString()}");
        }

        // 동기화 주기 로드 (분 -> 초)
        if (prefs.MailSyncIntervalMinutes > 0)
        {
            _viewModel.SetSyncInterval(prefs.MailSyncIntervalMinutes * 60);
            Log4.Debug($"동기화 주기 로드: {prefs.MailSyncIntervalMinutes}분");
        }

        // 메일 동기화 일시정지 상태 로드
        if (prefs.IsMailSyncPaused)
        {
            _viewModel.PauseSyncCommand.Execute(null);
            Log4.Debug("메일 동기화 일시정지 상태 로드");
        }

        // AI 분석 일시정지 상태 로드
        if (prefs.IsAiAnalysisPaused)
        {
            _viewModel.PauseAISyncCommand.Execute(null);
            Log4.Debug("AI 분석 일시정지 상태 로드");
        }
    }

    #endregion

    #region 테마 메뉴 이벤트

    /// <summary>
    /// 다크 모드 메뉴 클릭
    /// </summary>
    private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 다크 모드 클릭");
        Services.Theme.ThemeService.Instance.SetDarkMode();
    }

    /// <summary>
    /// 라이트 모드 메뉴 클릭
    /// </summary>
    private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 라이트 모드 클릭");
        Services.Theme.ThemeService.Instance.SetLightMode();
    }

    /// <summary>
    /// GPU 모드 메뉴 클릭 (토글)
    /// </summary>
    private void MenuGpuMode_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: GPU 모드 토글");
        Services.Theme.RenderModeService.Instance.ToggleGpuMode();
        UpdateGpuModeCheckmark();

        // 사용자에게 재시작 안내
        var currentMode = Services.Theme.RenderModeService.Instance.GetCurrentModeString();
        _viewModel.StatusMessage = $"렌더링 모드가 {currentMode}로 변경되었습니다. 완전 적용을 위해 앱을 재시작하세요.";
    }

    /// <summary>
    /// GPU 모드 체크마크 업데이트
    /// </summary>
    private void UpdateGpuModeCheckmark()
    {
        var isGpuMode = Services.Theme.RenderModeService.Instance.IsGpuMode;
        // 체크마크 표시/숨김
        if (GpuModeCheckMark != null)
        {
            GpuModeCheckMark.Visibility = isGpuMode ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// API 관리 메뉴 클릭
    /// </summary>
    private void MenuApiSettings_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: API 관리 클릭");
        var apiSettingsWindow = new ApiSettingsWindow(App.Settings);
        apiSettingsWindow.Owner = this;
        apiSettingsWindow.ShowDialog();
    }

    /// <summary>
    /// 서명 관리 메뉴 클릭
    /// </summary>
    private void MenuSignatureSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log4.Info("메뉴: 서명 관리 클릭");
            Log4.Debug("SignatureSettingsDialog 생성 시작");
            var dialog = new SignatureSettingsDialog();
            Log4.Debug("SignatureSettingsDialog Owner 설정");
            dialog.Owner = this;
            Log4.Debug("SignatureSettingsDialog LoadSettings 호출");
            dialog.LoadSettings(_viewModel.SignatureSettings);
            Log4.Debug("SignatureSettingsDialog ShowDialog 호출");

            if (dialog.ShowDialog() == true && dialog.IsSaved && dialog.ResultSettings != null)
            {
                _viewModel.SignatureSettings = dialog.ResultSettings;
                Log4.Info($"서명 설정 저장 완료: {dialog.ResultSettings.Signatures.Count}개");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"서명 관리 다이얼로그 오류: {ex.Message}\n{ex.StackTrace}");
            System.Windows.MessageBox.Show($"서명 관리 다이얼로그를 열 수 없습니다.\n{ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region 동기화 메뉴 이벤트

    // 메일 동기화 기간 설정
    private void MenuMailSync5_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Count, 5);
    private void MenuMailSyncDay_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Days, 1);
    private void MenuMailSyncWeek_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Weeks, 1);
    private void MenuMailSyncMonth_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Months, 1);
    private void MenuMailSyncYear_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Years, 1);
    private void MenuMailSyncAll_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.All, 0);

    private void SetMailSyncPeriod(SyncPeriodType periodType, int value)
    {
        var settings = new SyncPeriodSettings { PeriodType = periodType, Value = value };
        _viewModel.MailSyncPeriodSettings = settings;
        Log4.Info($"메일 동기화 기간 설정: {settings.ToDisplayString()}");
        _viewModel.StatusMessage = $"메일 동기화 기간: {settings.ToDisplayString()}";
        UpdateSyncPeriodCurrentDisplay(settings);

        // 설정 저장
        App.Settings.UserPreferences.MailSyncPeriodType = periodType.ToString();
        App.Settings.UserPreferences.MailSyncPeriodValue = value;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// 동기화 기간 현재 설정 표시 업데이트
    /// </summary>
    private void UpdateSyncPeriodCurrentDisplay(SyncPeriodSettings? settings = null)
    {
        settings ??= _viewModel.MailSyncPeriodSettings ?? SyncPeriodSettings.Default;
        if (MenuSyncPeriodCurrent != null)
        {
            MenuSyncPeriodCurrent.Header = $"현재: {settings.ToDisplayString()}";
        }

        // 동기화 기간 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new[] { MenuMailSync5, MenuMailSyncDay, MenuMailSyncWeek, MenuMailSyncMonth, MenuMailSyncYear, MenuMailSyncAll };
        var periodTypes = new[] { (SyncPeriodType.Count, 5), (SyncPeriodType.Days, 1), (SyncPeriodType.Weeks, 1), (SyncPeriodType.Months, 1), (SyncPeriodType.Years, 1), (SyncPeriodType.All, 0) };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = settings.PeriodType == periodTypes[i].Item1 && settings.Value == periodTypes[i].Item2;
                menuItems[i].Foreground = isSelected ? highlightColor : null;
            }
        }
    }

    // AI 분석 기간 설정
    private void MenuAIAnalysis5_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Count, 5);
    private void MenuAIAnalysisDay_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Days, 1);
    private void MenuAIAnalysisWeek_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Weeks, 1);
    private void MenuAIAnalysisMonth_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Months, 1);
    private void MenuAIAnalysisYear_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Years, 1);
    private void MenuAIAnalysisAll_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.All, 0);

    private void SetAiAnalysisPeriod(SyncPeriodType periodType, int value)
    {
        var settings = new SyncPeriodSettings { PeriodType = periodType, Value = value };
        _viewModel.AiAnalysisPeriodSettings = settings;
        Log4.Info($"AI 분석 기간 설정: {settings.ToDisplayString()}");
        _viewModel.StatusMessage = $"AI 분석 기간: {settings.ToDisplayString()}";
        UpdateAIAnalysisPeriodCurrentDisplay(settings);

        // 설정 저장
        App.Settings.UserPreferences.AiAnalysisPeriodType = periodType.ToString();
        App.Settings.UserPreferences.AiAnalysisPeriodValue = value;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// AI 분석 기간 현재 설정 표시 업데이트
    /// </summary>
    private void UpdateAIAnalysisPeriodCurrentDisplay(SyncPeriodSettings? settings = null)
    {
        settings ??= _viewModel.AiAnalysisPeriodSettings ?? SyncPeriodSettings.Default;
        if (MenuAIAnalysisPeriodCurrent != null)
        {
            MenuAIAnalysisPeriodCurrent.Header = $"현재: {settings.ToDisplayString()}";
        }

        // AI 분석 기간 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new[] { MenuAIAnalysis5, MenuAIAnalysisDay, MenuAIAnalysisWeek, MenuAIAnalysisMonth, MenuAIAnalysisYear, MenuAIAnalysisAll };
        var periodTypes = new[] { (SyncPeriodType.Count, 5), (SyncPeriodType.Days, 1), (SyncPeriodType.Weeks, 1), (SyncPeriodType.Months, 1), (SyncPeriodType.Years, 1), (SyncPeriodType.All, 0) };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = settings.PeriodType == periodTypes[i].Item1 && settings.Value == periodTypes[i].Item2;
                menuItems[i].Foreground = isSelected ? highlightColor : null;
            }
        }
    }

    // 메일 동기화 주기 설정
    private void MenuSyncInterval1s_Click(object sender, RoutedEventArgs e) => SetSyncInterval(1);
    private void MenuSyncInterval5s_Click(object sender, RoutedEventArgs e) => SetSyncInterval(5);
    private void MenuSyncInterval10s_Click(object sender, RoutedEventArgs e) => SetSyncInterval(10);
    private void MenuSyncInterval30s_Click(object sender, RoutedEventArgs e) => SetSyncInterval(30);
    private void MenuSyncInterval1m_Click(object sender, RoutedEventArgs e) => SetSyncInterval(60);
    private void MenuSyncInterval5m_Click(object sender, RoutedEventArgs e) => SetSyncInterval(300);
    private void MenuSyncInterval10m_Click(object sender, RoutedEventArgs e) => SetSyncInterval(600);
    private void MenuSyncInterval30m_Click(object sender, RoutedEventArgs e) => SetSyncInterval(1800);
    private void MenuSyncInterval1h_Click(object sender, RoutedEventArgs e) => SetSyncInterval(3600);

    private void SetSyncInterval(int seconds)
    {
        _viewModel.SetSyncInterval(seconds);
        var displayText = GetIntervalDisplayText(seconds);
        Log4.Info($"동기화 주기 설정: {displayText}");
        _viewModel.StatusMessage = $"동기화 주기: {displayText}";
        UpdateSyncIntervalCurrentDisplay(seconds);

        // 설정 저장 (분 단위로 저장)
        App.Settings.UserPreferences.MailSyncIntervalMinutes = seconds / 60;
        App.Settings.SaveUserPreferences();
    }

    private void UpdateSyncIntervalCurrentDisplay(int? seconds = null)
    {
        seconds ??= _viewModel.SyncIntervalSeconds;
        if (MenuSyncIntervalCurrent != null)
        {
            MenuSyncIntervalCurrent.Header = $"현재: {GetIntervalDisplayText(seconds.Value)}";
        }

        // 동기화 주기 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new[] { MenuSyncInterval1s, MenuSyncInterval5s, MenuSyncInterval10s, MenuSyncInterval30s, MenuSyncInterval1m, MenuSyncInterval5m, MenuSyncInterval10m, MenuSyncInterval30m, MenuSyncInterval1h };
        var intervalSeconds = new[] { 1, 5, 10, 30, 60, 300, 600, 1800, 3600 };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = seconds == intervalSeconds[i];
                menuItems[i].Foreground = isSelected ? highlightColor : null;
            }
        }
    }

    // AI 분석 주기 설정
    private void MenuAIInterval1s_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(1);
    private void MenuAIInterval5s_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(5);
    private void MenuAIInterval10s_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(10);
    private void MenuAIInterval30s_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(30);
    private void MenuAIInterval1m_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(60);
    private void MenuAIInterval5m_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(300);
    private void MenuAIInterval10m_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(600);
    private void MenuAIInterval30m_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(1800);
    private void MenuAIInterval1h_Click(object sender, RoutedEventArgs e) => SetAIAnalysisInterval(3600);

    private void SetAIAnalysisInterval(int seconds)
    {
        _viewModel.SetAIAnalysisInterval(seconds);
        var displayText = GetIntervalDisplayText(seconds);
        Log4.Info($"AI 분석 주기 설정: {displayText}");
        _viewModel.StatusMessage = $"AI 분석 주기: {displayText}";
        UpdateAIAnalysisIntervalCurrentDisplay(seconds);
    }

    private void UpdateAIAnalysisIntervalCurrentDisplay(int? seconds = null)
    {
        seconds ??= _viewModel.AIAnalysisIntervalSeconds;
        if (MenuAIAnalysisIntervalCurrent != null)
        {
            MenuAIAnalysisIntervalCurrent.Header = $"현재: {GetIntervalDisplayText(seconds.Value)}";
        }

        // AI 분석 주기 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new[] { MenuAIInterval1s, MenuAIInterval5s, MenuAIInterval10s, MenuAIInterval30s, MenuAIInterval1m, MenuAIInterval5m, MenuAIInterval10m, MenuAIInterval30m, MenuAIInterval1h };
        var intervalSeconds = new[] { 1, 5, 10, 30, 60, 300, 600, 1800, 3600 };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = seconds == intervalSeconds[i];
                menuItems[i].Foreground = isSelected ? highlightColor : null;
            }
        }
    }

    private static string GetIntervalDisplayText(int seconds)
    {
        return seconds switch
        {
            < 60 => $"{seconds}초",
            60 => "1분",
            < 3600 => $"{seconds / 60}분",
            3600 => "1시간",
            _ => $"{seconds / 3600}시간"
        };
    }

    /// <summary>
    /// 동기화 상세 설정 다이얼로그 열기
    /// </summary>
    private void MenuSyncSettings_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("동기화 설정 다이얼로그 열기");

        var dialog = new SyncSettingsDialog
        {
            Owner = this
        };

        // 현재 설정 로드
        dialog.LoadSettings(
            _viewModel.MailSyncPeriodSettings ?? SyncPeriodSettings.Default,
            _viewModel.AiAnalysisPeriodSettings ?? SyncPeriodSettings.Default
        );

        // 다이얼로그 표시
        if (dialog.ShowDialog() == true && dialog.IsSaved)
        {
            // 설정 적용
            if (dialog.MailSyncSettings != null)
                _viewModel.MailSyncPeriodSettings = dialog.MailSyncSettings;

            if (dialog.AiAnalysisSettings != null)
                _viewModel.AiAnalysisPeriodSettings = dialog.AiAnalysisSettings;

            _viewModel.StatusMessage = "동기화 설정이 저장되었습니다.";
        }
    }

    /// <summary>
    /// 전체 재동기화 메뉴 클릭
    /// </summary>
    private async void MenuForceResync_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 전체 재동기화 클릭");
        _viewModel.StatusMessage = "전체 재동기화 시작...";

        try
        {
            // BackgroundSyncService를 통해 강제 동기화
            await _viewModel.ForceResyncAllAsync();
            _viewModel.StatusMessage = "전체 재동기화 완료";
        }
        catch (Exception ex)
        {
            Log4.Error($"전체 재동기화 실패: {ex.Message}");
            _viewModel.StatusMessage = $"재동기화 실패: {ex.Message}";
        }
    }

    #endregion

    #region 폴더 컨텍스트 메뉴 이벤트

    /// <summary>
    /// 폴더 재동기화
    /// </summary>
    private void FolderResync_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null)
        {
            Log4.Info($"폴더 재동기화: {_rightClickedFolder.DisplayName}");
            // TODO: 해당 폴더의 메일 강제 재동기화
            _viewModel.StatusMessage = $"'{_rightClickedFolder.DisplayName}' 폴더 재동기화 중...";
        }
    }

    /// <summary>
    /// 폴더 AI 재분석
    /// </summary>
    private void FolderAIReanalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null)
        {
            Log4.Info($"폴더 AI 재분석: {_rightClickedFolder.DisplayName}");
            // TODO: 해당 폴더의 메일 AI 강제 재분석
            _viewModel.StatusMessage = $"'{_rightClickedFolder.DisplayName}' 폴더 AI 재분석 중...";
        }
    }

    #endregion

    #region 메일 컨텍스트 메뉴 이벤트

    /// <summary>
    /// 메일 리스트 우클릭 시 선택 처리
    /// </summary>
    private void EmailListBox_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 우클릭한 항목이 선택되어 있지 않으면 해당 항목만 선택
        if (e.OriginalSource is FrameworkElement element)
        {
            var email = FindParentDataContext<Email>(element);
            if (email != null && !EmailListBox.SelectedItems.Contains(email))
            {
                EmailListBox.SelectedItems.Clear();
                EmailListBox.SelectedItems.Add(email);
            }
        }
    }

    /// <summary>
    /// 선택된 메일 AI 재분석
    /// </summary>
    private void EmailAIReanalyze_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"메일 AI 재분석: {selectedEmails.Count}건");
            // TODO: 선택된 메일들 AI 강제 재분석
            _viewModel.StatusMessage = $"{selectedEmails.Count}건 메일 AI 재분석 중...";
        }
    }

    /// <summary>
    /// 플래그 설정
    /// </summary>
    private async void EmailSetFlag_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"플래그 설정: {selectedEmails.Count}건");
            await _viewModel.UpdateFlagStatusAsync(selectedEmails, "flagged");
        }
    }

    /// <summary>
    /// 플래그 완료
    /// </summary>
    private async void EmailCompleteFlag_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"플래그 완료: {selectedEmails.Count}건");
            await _viewModel.UpdateFlagStatusAsync(selectedEmails, "complete");
        }
    }

    /// <summary>
    /// 플래그 해제
    /// </summary>
    private async void EmailClearFlag_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"플래그 해제: {selectedEmails.Count}건");
            await _viewModel.UpdateFlagStatusAsync(selectedEmails, "notFlagged");
        }
    }

    /// <summary>
    /// 핀 고정/해제 토글 (컨텍스트 메뉴)
    /// </summary>
    private void EmailTogglePin_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            foreach (var email in selectedEmails)
            {
                email.IsPinned = !email.IsPinned;
            }
            _viewModel.TogglePinnedCommand.Execute(null);
            Log4.Info($"핀 고정 토글: {selectedEmails.Count}건");
        }
    }

    /// <summary>
    /// 읽음으로 표시
    /// </summary>
    private async void EmailMarkAsRead_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"읽음 표시: {selectedEmails.Count}건");
            await _viewModel.UpdateReadStatusAsync(selectedEmails, true);
        }
    }

    /// <summary>
    /// 읽지 않음으로 표시
    /// </summary>
    private async void EmailMarkAsUnread_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"읽지 않음 표시: {selectedEmails.Count}건");
            await _viewModel.UpdateReadStatusAsync(selectedEmails, false);
        }
    }

    /// <summary>
    /// 선택된 메일 삭제
    /// </summary>
    private async void EmailDelete_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = EmailListBox.SelectedItem as Email;
        if (selectedEmail != null)
        {
            Log4.Info($"메일 삭제: {selectedEmail.Subject}");
            await _viewModel.DeleteEmailCommand.ExecuteAsync(selectedEmail);
        }
    }

    /// <summary>
    /// 새 메일 버튼 클릭
    /// </summary>
    private void NewMailButton_Click(object sender, RoutedEventArgs e)
    {
        OpenComposeWindow(ViewModels.ComposeMode.New);
    }

    /// <summary>
    /// 답장 클릭
    /// </summary>
    private void EmailReply_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = EmailListBox.SelectedItem as Email;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Reply, selectedEmail);
        }
    }

    /// <summary>
    /// 전체 답장 클릭
    /// </summary>
    private void EmailReplyAll_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = EmailListBox.SelectedItem as Email;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.ReplyAll, selectedEmail);
        }
    }

    /// <summary>
    /// 전달 클릭
    /// </summary>
    private void EmailForward_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = EmailListBox.SelectedItem as Email;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Forward, selectedEmail);
        }
    }

    #endregion

    #region 메일 본문 상단 액션 버튼 핸들러

    /// <summary>
    /// 회신 버튼 클릭
    /// </summary>
    private void ReplyButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Reply, selectedEmail);
        }
    }

    /// <summary>
    /// 전체 회신 버튼 클릭
    /// </summary>
    private void ReplyAllButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.ReplyAll, selectedEmail);
        }
    }

    /// <summary>
    /// 전달 버튼 클릭
    /// </summary>
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Forward, selectedEmail);
        }
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 삭제 확인
            var result = System.Windows.MessageBox.Show(
                $"'{selectedEmail.Subject}' 메일을 삭제하시겠습니까?",
                "삭제 확인",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 기존 컨텍스트 메뉴의 삭제 기능 호출
                EmailDelete_Click(sender, e);
            }
        }
    }

    /// <summary>
    /// 플래그 버튼 클릭 (토글)
    /// </summary>
    private void FlagButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 현재 플래그 상태에 따라 설정/해제 호출
            if (selectedEmail.FlagStatus == "flagged")
            {
                EmailClearFlag_Click(sender, e);
            }
            else
            {
                EmailSetFlag_Click(sender, e);
            }
        }
    }

    /// <summary>
    /// 읽음/안읽음 토글 버튼 클릭
    /// </summary>
    private void ReadUnreadButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 현재 읽음 상태에 따라 토글
            if (selectedEmail.IsRead)
            {
                EmailMarkAsUnread_Click(sender, e);
            }
            else
            {
                EmailMarkAsRead_Click(sender, e);
            }
        }
    }

    /// <summary>
    /// 더보기 버튼 클릭 (추가 작업 메뉴)
    /// </summary>
    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            // 더보기 컨텍스트 메뉴 생성
            var contextMenu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)FindResource("ApplicationBackgroundBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"),
                Padding = new Thickness(4)
            };

            // AI 재분석 메뉴
            var aiItem = new System.Windows.Controls.MenuItem { Header = "🤖 AI 재분석" };
            aiItem.Click += EmailAIReanalyze_Click;
            contextMenu.Items.Add(aiItem);

            contextMenu.Items.Add(new Separator());

            // 카테고리 설정
            var categoryItem = new System.Windows.Controls.MenuItem { Header = "🏷️ 카테고리 설정..." };
            categoryItem.Click += EmailSetCategories_Click;
            contextMenu.Items.Add(categoryItem);

            // 즐겨찾기 추가
            var starItem = new System.Windows.Controls.MenuItem { Header = "⭐ 즐겨찾기 토글" };
            starItem.Click += EmailToggleStar_Click;
            contextMenu.Items.Add(starItem);

            button.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// 즐겨찾기 토글
    /// </summary>
    private void EmailToggleStar_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            selectedEmail.IsStarred = !selectedEmail.IsStarred;
            Log4.Info($"즐겨찾기 토글: {selectedEmail.Subject} -> {(selectedEmail.IsStarred ? "추가" : "해제")}");
        }
    }

    /// <summary>
    /// 카테고리 설정 (더보기 메뉴에서 호출)
    /// </summary>
    private void EmailSetCategories_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 카테고리 선택 다이얼로그를 열거나 간단한 처리
            Log4.Info($"카테고리 설정 요청: {selectedEmail.Subject}");
            System.Windows.MessageBox.Show(
                "카테고리 설정 기능은 추후 구현 예정입니다.",
                "알림",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    #endregion

    /// <summary>
    /// 메일 작성 창 열기
    /// </summary>
    private void OpenComposeWindow(ViewModels.ComposeMode mode, Email? originalEmail = null)
    {
        try
        {
            var graphMailService = (App.Current as App)?.GraphMailService;
            if (graphMailService == null)
            {
                Log4.Error("GraphMailService를 찾을 수 없습니다.");
                return;
            }

            // 보낸메일 즉시 동기화를 위해 BackgroundSyncService도 전달
            var syncService = (App.Current as App)?.BackgroundSyncService;
            var viewModel = new ViewModels.ComposeViewModel(graphMailService, syncService, mode, originalEmail);
            var composeWindow = new ComposeWindow(viewModel);
            composeWindow.Owner = this;
            composeWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 작성 창 열기 실패: {ex.Message}");
        }
    }

    #region 정렬 및 전체 선택 핸들러

    /// <summary>
    /// 정렬 드롭다운 버튼 클릭 - 정렬 옵션 메뉴 표시
    /// </summary>
    private void SortDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            var contextMenu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)FindResource("ApplicationBackgroundBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"),
                Padding = new Thickness(4)
            };

            // 날짜 정렬
            var dateItem = new System.Windows.Controls.MenuItem { Header = "📅 날짜" };
            dateItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("ReceivedDateTime");
            contextMenu.Items.Add(dateItem);

            // 제목 정렬
            var subjectItem = new System.Windows.Controls.MenuItem { Header = "📝 제목" };
            subjectItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("Subject");
            contextMenu.Items.Add(subjectItem);

            // 발신자 정렬
            var fromItem = new System.Windows.Controls.MenuItem { Header = "👤 발신자" };
            fromItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("From");
            contextMenu.Items.Add(fromItem);

            // 중요도 정렬
            var priorityItem = new System.Windows.Controls.MenuItem { Header = "⭐ 중요도" };
            priorityItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("PriorityScore");
            contextMenu.Items.Add(priorityItem);

            contextMenu.Items.Add(new Separator());

            // 읽지 않은 메일 정렬
            var unreadItem = new System.Windows.Controls.MenuItem { Header = "📧 읽지 않음" };
            unreadItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("IsRead");
            contextMenu.Items.Add(unreadItem);

            // 플래그 정렬
            var flagItem = new System.Windows.Controls.MenuItem { Header = "🚩 플래그" };
            flagItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("FlagStatus");
            contextMenu.Items.Add(flagItem);

            button.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// 전체 선택 체크박스 체크됨
    /// </summary>
    private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Emails != null)
        {
            EmailListBox.SelectAll();
            Log4.Debug($"전체 선택: {_viewModel.Emails.Count}개");
        }
    }

    /// <summary>
    /// 전체 선택 체크박스 해제됨
    /// </summary>
    private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        EmailListBox.UnselectAll();
        Log4.Debug("전체 선택 해제");
    }

    #endregion

    #region Phase 1: 검색 및 키보드 단축키

    /// <summary>
    /// 검색 텍스트 박스 키 이벤트 핸들러
    /// Enter 키 누르면 검색 실행
    /// </summary>
    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    #endregion

    #region Phase 2: 메일 드래그&드롭

    private Point _emailDragStartPoint;
    private bool _isDraggingEmail;
    private List<Email>? _draggedEmails;

    /// <summary>
    /// 메일 리스트 마우스 다운 - 드래그 시작점 기록
    /// </summary>
    private void EmailListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _emailDragStartPoint = e.GetPosition(null);
        _isDraggingEmail = false;
    }

    /// <summary>
    /// 메일 리스트 마우스 이동 - 드래그 시작 감지
    /// </summary>
    private void EmailListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var currentPos = e.GetPosition(null);
        var diff = _emailDragStartPoint - currentPos;

        // 최소 이동 거리 체크
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 선택된 메일들 가져오기
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count == 0) return;

        _isDraggingEmail = true;
        _draggedEmails = selectedEmails;

        // 드래그 데이터 설정
        var dragData = new DataObject("EmailDragData", selectedEmails);

        // 드래그 시작
        DragDrop.DoDragDrop(EmailListBox, dragData, DragDropEffects.Move);

        _isDraggingEmail = false;
        _draggedEmails = null;
    }

    /// <summary>
    /// 폴더 트리 드래그 진입 - 드롭 가능 여부 표시
    /// </summary>
    private void FolderTreeView_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("EmailDragData"))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    /// <summary>
    /// 폴더 트리 드래그 오버
    /// </summary>
    private void FolderTreeView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (e.Data.GetDataPresent("EmailDragData"))
        {
            // 드롭 대상 폴더 찾기
            var element = e.OriginalSource as DependencyObject;
            var treeViewItem = FindAncestor<System.Windows.Controls.TreeViewItem>(element);

            if (treeViewItem?.DataContext is Folder targetFolder)
            {
                e.Effects = DragDropEffects.Move;
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// 폴더 트리에 드롭 - 메일 이동
    /// </summary>
    private async void FolderTreeView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("EmailDragData")) return;

        // 드롭 대상 폴더 찾기
        var element = e.OriginalSource as DependencyObject;
        var treeViewItem = FindAncestor<System.Windows.Controls.TreeViewItem>(element);

        if (treeViewItem?.DataContext is Folder targetFolder)
        {
            var emails = e.Data.GetData("EmailDragData") as List<Email>;
            if (emails != null && emails.Count > 0)
            {
                Log4.Info($"메일 드롭: {emails.Count}건 → {targetFolder.DisplayName}");
                await _viewModel.MoveEmailsToFolderAsync(emails, targetFolder);
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// 시각적 트리에서 특정 타입의 부모 요소 찾기
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T target)
                return target;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }


    /// <summary>
    /// 키보드 단축키 처리
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // 검색 텍스트 박스에 포커스가 있으면 단축키 처리 안함
        if (TitleBarSearchBox.IsFocused)
            return;

        // Ctrl 키 조합
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.R:
                    // Ctrl+R: 답장
                    if (_viewModel.SelectedEmail != null)
                    {
                        OpenComposeWindow(ComposeMode.Reply, _viewModel.SelectedEmail);
                        e.Handled = true;
                    }
                    break;

                case Key.F:
                    // Ctrl+F: 검색 텍스트 박스로 포커스
                    TitleBarSearchBox.Focus();
                    e.Handled = true;
                    break;

                case Key.N:
                    // Ctrl+N: 새 메일
                    OpenComposeWindow(ComposeMode.New, null);
                    e.Handled = true;
                    break;
            }
        }
        // Ctrl+Shift 키 조합
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.R:
                    // Ctrl+Shift+R: 전체 답장
                    if (_viewModel.SelectedEmail != null)
                    {
                        OpenComposeWindow(ComposeMode.ReplyAll, _viewModel.SelectedEmail);
                        e.Handled = true;
                    }
                    break;

                case Key.F:
                    // Ctrl+Shift+F: 전달
                    if (_viewModel.SelectedEmail != null)
                    {
                        OpenComposeWindow(ComposeMode.Forward, _viewModel.SelectedEmail);
                        e.Handled = true;
                    }
                    break;
            }
        }
        // 단일 키
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.Delete:
                    // Delete: 선택된 메일 삭제
                    if (_viewModel.SelectedEmail != null)
                    {
                        _viewModel.DeleteEmailCommand.Execute(_viewModel.SelectedEmail);
                        e.Handled = true;
                    }
                    break;

                case Key.F5:
                    // F5: 새로고침
                    _viewModel.LoadEmailsCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    // Escape: 검색 초기화
                    if (_viewModel.IsSearchMode)
                    {
                        _viewModel.ClearSearchCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    #endregion

    #region 좌측 네비게이션 아이콘바

    /// <summary>
    /// 메일 모드로 전환
    /// </summary>
    private void NavMailButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 메일 모드");
        ShowMailView();
    }

    /// <summary>
    /// 캘린더 모드로 전환
    /// </summary>
    private void NavCalendarButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 캘린더 모드");
        ShowCalendarView();
    }

    /// <summary>
    /// 설정 버튼 클릭
    /// </summary>
    private void NavSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 설정");
        // API 설정 다이얼로그 열기
        MenuApiSettings_Click(sender, e);
    }

    /// <summary>
    /// 타이틀바 설정 버튼 클릭 (동기화 설정으로 연결)
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 동기화 설정");
        MenuSyncSettings_Click(sender, e);
    }

    /// <summary>
    /// 알림 버튼 클릭 - 알림 패널 팝업 열기
    /// </summary>
    private void NotificationButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 알림 패널 열기");
        NotificationPopup.IsOpen = !NotificationPopup.IsOpen;
    }

    /// <summary>
    /// 알림 패널 닫기 버튼 클릭
    /// </summary>
    private void CloseNotificationPopup_Click(object sender, RoutedEventArgs e)
    {
        NotificationPopup.IsOpen = false;
    }

    /// <summary>
    /// 타이틀바 검색창 키 이벤트
    /// </summary>
    private void TitleBarSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var searchText = TitleBarSearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(searchText))
            {
                AddRecentSearch(searchText);
            }
            _viewModel.SearchCommand.Execute(null);
            SearchAutocompletePopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.ClearSearchCommand.Execute(null);
            TitleBarSearchBox.Text = "";
            SearchAutocompletePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 검색창 포커스 시 자동완성 팝업 열기
    /// </summary>
    private void TitleBarSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchAutocompletePopup.IsOpen = true;
    }

    /// <summary>
    /// 검색창 포커스 해제 시 자동완성 팝업 닫기
    /// </summary>
    private void TitleBarSearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // 포커스가 팝업 내부로 이동한 경우는 닫지 않음
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var focusedElement = Keyboard.FocusedElement as DependencyObject;
            if (focusedElement != null)
            {
                // 포커스가 팝업 내부에 있는지 확인
                var parent = focusedElement;
                while (parent != null)
                {
                    if (parent == SearchAutocompletePopup.Child)
                        return;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            // 검색창 자체에 포커스가 있으면 닫지 않음
            if (TitleBarSearchBox.IsFocused || TitleBarSearchBox.IsKeyboardFocusWithin)
                return;

            SearchAutocompletePopup.IsOpen = false;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 검색 자동완성 뒤로가기 버튼
    /// </summary>
    private void SearchAutocompleteBack_Click(object sender, RoutedEventArgs e)
    {
        SearchAutocompletePopup.IsOpen = false;
    }

    /// <summary>
    /// 검색 탭 클릭 (모두/메일/사람)
    /// </summary>
    private void SearchTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button clickedTab) return;

        // 모든 탭 버튼을 Secondary로 변경
        SearchTabAll.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        SearchTabMail.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        SearchTabPerson.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

        // 클릭한 탭을 Primary로 변경
        clickedTab.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;

        // TODO: 탭에 따라 검색 결과 필터링
        Log4.Info($"검색 탭 변경: {clickedTab.Content}");
    }

    /// <summary>
    /// 메일 뷰 표시
    /// </summary>
    private void ShowMailView()
    {
        // 메일 관련 UI 요소 표시
        if (FolderTreeBorder != null) FolderTreeBorder.Visibility = Visibility.Visible;
        if (Splitter1 != null) Splitter1.Visibility = Visibility.Visible;
        if (MailListBorder != null) MailListBorder.Visibility = Visibility.Visible;
        if (Splitter2 != null) Splitter2.Visibility = Visibility.Visible;
        if (BodyAreaGrid != null) BodyAreaGrid.Visibility = Visibility.Visible;

        // 캘린더 뷰 숨김
        if (CalendarViewBorder != null) CalendarViewBorder.Visibility = Visibility.Collapsed;

        // 우측 패널: AI 패널 표시, 캘린더 세부 패널 숨김
        if (AIPanelBorder != null) AIPanelBorder.Visibility = Visibility.Visible;
        if (CalendarDetailPanel != null) CalendarDetailPanel.Visibility = Visibility.Collapsed;

        _viewModel.StatusMessage = "메일";
        _viewModel.IsCalendarViewActive = false;
        _viewModel.IsCalendarMode = false;
    }

    /// <summary>
    /// 캘린더 뷰 표시
    /// </summary>
    private void ShowCalendarView()
    {
        // 메일 관련 UI 요소 숨김
        if (FolderTreeBorder != null) FolderTreeBorder.Visibility = Visibility.Collapsed;
        if (Splitter1 != null) Splitter1.Visibility = Visibility.Collapsed;
        if (MailListBorder != null) MailListBorder.Visibility = Visibility.Collapsed;
        if (Splitter2 != null) Splitter2.Visibility = Visibility.Collapsed;
        if (BodyAreaGrid != null) BodyAreaGrid.Visibility = Visibility.Collapsed;

        // 캘린더 뷰 표시
        if (CalendarViewBorder != null) CalendarViewBorder.Visibility = Visibility.Visible;

        // 우측 패널: AI 패널 숨김, 캘린더 세부 패널 표시
        if (AIPanelBorder != null) AIPanelBorder.Visibility = Visibility.Collapsed;
        if (CalendarDetailPanel != null) CalendarDetailPanel.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "일정";
        _viewModel.IsCalendarViewActive = true;
        _viewModel.IsCalendarMode = true;

        // 캘린더 데이터 로드
        LoadCalendarDataAsync();
    }

    /// <summary>
    /// 캘린더 데이터 비동기 로드
    /// </summary>
    private async void LoadCalendarDataAsync()
    {
        try
        {
            // 현재 월의 일정 로드
            await LoadMonthEventsAsync(_currentCalendarDate);
            UpdateCalendarDisplay();
        }
        catch (Exception ex)
        {
            Log4.Error($"캘린더 데이터 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region 캘린더 뷰 로직

    private DateTime _currentCalendarDate = DateTime.Today;
    private DateTime _selectedCalendarDate = DateTime.Today;
    private List<Microsoft.Graph.Models.Event>? _currentMonthEvents;

    /// <summary>
    /// 이전 월로 이동
    /// </summary>
    private void CalPrevMonthBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarDate = _currentCalendarDate.AddMonths(-1);
        LoadCalendarDataAsync();
    }

    /// <summary>
    /// 다음 월로 이동
    /// </summary>
    private void CalNextMonthBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarDate = _currentCalendarDate.AddMonths(1);
        LoadCalendarDataAsync();
    }

    /// <summary>
    /// 오늘로 이동
    /// </summary>
    private void CalTodayBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarDate = DateTime.Today;
        LoadCalendarDataAsync();
    }

    /// <summary>
    /// 일간 뷰로 전환
    /// </summary>
    private void CalDayViewBtn_Click(object sender, RoutedEventArgs e)
    {
        CalDayViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        CalWeekViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalMonthViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        // TODO: 일간 뷰 구현
        _viewModel.StatusMessage = "일간 뷰 (구현 예정)";
    }

    /// <summary>
    /// 주간 뷰로 전환
    /// </summary>
    private void CalWeekViewBtn_Click(object sender, RoutedEventArgs e)
    {
        CalDayViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalWeekViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        CalMonthViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        // TODO: 주간 뷰 구현
        _viewModel.StatusMessage = "주간 뷰 (구현 예정)";
    }

    /// <summary>
    /// 월간 뷰로 전환
    /// </summary>
    private void CalMonthViewBtn_Click(object sender, RoutedEventArgs e)
    {
        CalDayViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalWeekViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalMonthViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        UpdateCalendarDisplay();
    }

    /// <summary>
    /// 새 일정 버튼 클릭
    /// </summary>
    private async void NewEventButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("새 일정 생성 클릭");
        await OpenEventEditDialogAsync(null, _currentCalendarDate);
    }

    /// <summary>
    /// 일정 편집 다이얼로그 열기
    /// </summary>
    private async Task OpenEventEditDialogAsync(Microsoft.Graph.Models.Event? existingEvent, DateTime? targetDate = null)
    {
        try
        {
            EventEditDialog dialog;
            if (existingEvent != null)
            {
                dialog = new EventEditDialog(existingEvent);
                dialog.Owner = this;
            }
            else if (targetDate.HasValue)
            {
                dialog = new EventEditDialog(targetDate.Value);
                dialog.Owner = this;
            }
            else
            {
                dialog = new EventEditDialog();
                dialog.Owner = this;
            }

            var result = dialog.ShowDialog();
            if (result == true)
            {
                if (dialog.IsDeleted)
                {
                    _viewModel.StatusMessage = "일정이 삭제되었습니다.";
                    Log4.Info("일정 삭제 완료");
                }
                else if (dialog.ResultEvent != null)
                {
                    _viewModel.StatusMessage = existingEvent != null ?
                        "일정이 수정되었습니다." : "새 일정이 생성되었습니다.";
                    Log4.Info($"일정 저장 완료: {dialog.ResultEvent.Subject}");
                }

                // 캘린더 새로고침
                await LoadMonthEventsAsync(_currentCalendarDate);
                UpdateCalendarDisplay();
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"일정 편집 다이얼로그 오류: {ex.Message}");
            _viewModel.StatusMessage = "일정 편집 중 오류가 발생했습니다.";
        }
    }

    /// <summary>
    /// 월별 일정 로드
    /// </summary>
    private async Task LoadMonthEventsAsync(DateTime month)
    {
        try
        {
            var firstDay = new DateTime(month.Year, month.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var calendarService = ((App)Application.Current).GetService<Services.Graph.GraphCalendarService>();
            if (calendarService != null)
            {
                Log4.Info($"캘린더 일정 조회 시작: {firstDay:yyyy-MM-dd} ~ {lastDay.AddDays(1):yyyy-MM-dd}");
                var events = await calendarService.GetEventsAsync(firstDay, lastDay.AddDays(1));
                _currentMonthEvents = events?.ToList();
                _viewModel.CurrentMonthEventCount = _currentMonthEvents?.Count ?? 0;
                Log4.Info($"캘린더 일정 로드 완료: {_currentMonthEvents?.Count ?? 0}건 ({month:yyyy-MM})");
            }
            else
            {
                Log4.Warn("캘린더 서비스가 null입니다.");
                _currentMonthEvents = new List<Microsoft.Graph.Models.Event>();
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"월별 일정 로드 실패: {ex.Message}\n{ex.StackTrace}");
            _currentMonthEvents = new List<Microsoft.Graph.Models.Event>();
        }
    }

    /// <summary>
    /// 캘린더 표시 업데이트
    /// </summary>
    private void UpdateCalendarDisplay()
    {
        // 월/년 텍스트 업데이트
        var monthYearText = $"{_currentCalendarDate.Year}년 {_currentCalendarDate.Month}월";
        if (CalMonthYearText != null) CalMonthYearText.Text = monthYearText;
        if (CalMainMonthYearText != null) CalMainMonthYearText.Text = monthYearText;

        // 월간 캘린더 그리드 업데이트
        UpdateMonthCalendarGrid();

        // 미니 캘린더 업데이트
        UpdateMiniCalendarGrid();

        // 오늘 날짜 또는 선택된 날짜의 일정으로 세부 패널 초기화
        var targetDate = _selectedCalendarDate.Month == _currentCalendarDate.Month &&
                         _selectedCalendarDate.Year == _currentCalendarDate.Year
            ? _selectedCalendarDate
            : DateTime.Today;

        var dayEvents = _currentMonthEvents?
            .Where(e => e.Start?.DateTime != null &&
                        DateTime.Parse(e.Start.DateTime).Date == targetDate.Date)
            .ToList() ?? new List<Microsoft.Graph.Models.Event>();

        UpdateSelectedDateEventsPanel(targetDate, dayEvents);
        UpdateCalendarDetailPanel(targetDate, dayEvents);
    }

    /// <summary>
    /// 월간 캘린더 그리드 동적 생성
    /// </summary>
    private void UpdateMonthCalendarGrid()
    {
        if (MonthCalendarGrid == null) return;

        // 기존 날짜 셀 제거 (요일 헤더 제외)
        var toRemove = MonthCalendarGrid.Children.Cast<UIElement>()
            .Where(c => Grid.GetRow(c) > 0)
            .ToList();
        foreach (var child in toRemove)
            MonthCalendarGrid.Children.Remove(child);

        var firstDay = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_currentCalendarDate.Year, _currentCalendarDate.Month);
        var startDayOfWeek = (int)firstDay.DayOfWeek;

        var day = 1;
        for (int week = 0; week < 6 && day <= daysInMonth; week++)
        {
            for (int dayOfWeek = 0; dayOfWeek < 7 && day <= daysInMonth; dayOfWeek++)
            {
                if (week == 0 && dayOfWeek < startDayOfWeek)
                    continue;

                var cellDate = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, day);
                var cell = CreateDayCell(cellDate);
                Grid.SetRow(cell, week + 1);
                Grid.SetColumn(cell, dayOfWeek);
                MonthCalendarGrid.Children.Add(cell);

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
        var dayEvents = _currentMonthEvents?
            .Where(e => e.Start?.DateTime != null &&
                        DateTime.Parse(e.Start.DateTime).Date == date.Date)
            .ToList() ?? new List<Microsoft.Graph.Models.Event>();

        var cell = new Border
        {
            BorderBrush = (Brush)FindResource("ControlElevationBorderBrush"),
            BorderThickness = new Thickness(0.5),
            Margin = new Thickness(1),
            Background = isToday ?
                (Brush)FindResource("SystemAccentColorSecondaryBrush") :
                Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel { Margin = new Thickness(4) };

        // 날짜 숫자
        var dayText = new System.Windows.Controls.TextBlock
        {
            Text = date.Day.ToString(),
            FontSize = 12,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground = date.DayOfWeek == DayOfWeek.Sunday ? new SolidColorBrush(Color.FromRgb(255, 107, 107)) :
                         date.DayOfWeek == DayOfWeek.Saturday ? new SolidColorBrush(Color.FromRgb(107, 157, 255)) :
                         (Brush)FindResource("TextFillColorPrimaryBrush"),
            Margin = new Thickness(2, 0, 0, 4)
        };
        stack.Children.Add(dayText);

        // 일정 표시 (최대 3개)
        var displayEvents = dayEvents.Take(3);
        foreach (var evt in displayEvents)
        {
            var capturedEvent = evt; // 람다에서 사용할 변수 캡처
            var eventBorder = new Border
            {
                Background = GetEventColor(evt),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Tag = evt // 이벤트 객체 저장
            };
            var eventText = new System.Windows.Controls.TextBlock
            {
                Text = evt.Subject ?? "(제목 없음)",
                FontSize = 10,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            eventBorder.Child = eventText;

            // 일정 클릭 시 편집 다이얼로그 열기
            eventBorder.MouseLeftButtonDown += async (s, args) =>
            {
                args.Handled = true; // 날짜 셀 클릭 이벤트 전파 방지
                Log4.Info($"일정 클릭: {capturedEvent.Subject}");
                await OpenEventEditDialogAsync(capturedEvent, null);
            };

            stack.Children.Add(eventBorder);
        }

        // 더 많은 일정이 있으면 표시
        if (dayEvents.Count > 3)
        {
            var moreText = new System.Windows.Controls.TextBlock
            {
                Text = $"+{dayEvents.Count - 3}개 더",
                FontSize = 9,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                Margin = new Thickness(2, 2, 0, 0)
            };
            stack.Children.Add(moreText);
        }

        cell.Child = stack;

        // 클릭 이벤트: 단일 클릭=날짜 선택, 더블 클릭=새 일정 생성
        cell.MouseLeftButtonDown += async (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                Log4.Info($"날짜 더블클릭: {date:yyyy-MM-dd} - 새 일정 생성");
                await OpenEventEditDialogAsync(null, date);
            }
            else if (e.ClickCount == 1)
            {
                Log4.Info($"날짜 클릭: {date:yyyy-MM-dd}");
                _selectedCalendarDate = date;
                _viewModel.StatusMessage = $"{date:yyyy년 M월 d일} 선택됨 ({dayEvents.Count}건 일정)";
                UpdateSelectedDateEventsPanel(date, dayEvents);
                UpdateCalendarDetailPanel(date, dayEvents);
            }
        };

        return cell;
    }

    /// <summary>
    /// 일정 색상 결정
    /// </summary>
    private Brush GetEventColor(Microsoft.Graph.Models.Event evt)
    {
        // 카테고리 기반 색상
        var categories = evt.Categories?.FirstOrDefault();
        return categories switch
        {
            "이메일 마감" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
            "할일" => new SolidColorBrush(Color.FromRgb(52, 152, 219)),
            _ => new SolidColorBrush(Color.FromRgb(46, 204, 113))
        };
    }

    /// <summary>
    /// 선택된 날짜의 일정 목록 패널 업데이트 (좌측 패널)
    /// </summary>
    private void UpdateSelectedDateEventsPanel(DateTime date, List<Microsoft.Graph.Models.Event> events)
    {
        if (SelectedDateEventsPanel == null) return;

        // 헤더 텍스트 업데이트
        if (SelectedDateText != null)
        {
            var dateText = date.Date == DateTime.Today ? "오늘의 일정" : $"{date:M월 d일} 일정";
            SelectedDateText.Text = $"{dateText} ({events.Count}건)";
        }

        // 기존 일정 아이템 제거 (NoEventsText 제외)
        var itemsToRemove = SelectedDateEventsPanel.Children.Cast<UIElement>()
            .Where(c => c != NoEventsText)
            .ToList();
        foreach (var item in itemsToRemove)
            SelectedDateEventsPanel.Children.Remove(item);

        // 일정 없음 표시
        if (NoEventsText != null)
            NoEventsText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 일정 아이템 추가
        foreach (var evt in events.OrderBy(e => e.Start?.DateTime))
        {
            var capturedEvent = evt;
            var eventCard = CreateEventCard(evt);
            eventCard.MouseLeftButtonDown += async (s, e) =>
            {
                e.Handled = true;
                await OpenEventEditDialogAsync(capturedEvent, null);
            };
            SelectedDateEventsPanel.Children.Insert(0, eventCard);
        }
    }

    /// <summary>
    /// 일정 카드 UI 생성 (좌측 패널용)
    /// </summary>
    private Border CreateEventCard(Microsoft.Graph.Models.Event evt)
    {
        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = GetEventColor(evt),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel();

        // 시간
        string timeText = "";
        if (evt.IsAllDay ?? false)
        {
            timeText = "종일";
        }
        else if (evt.Start?.DateTime != null)
        {
            var startTime = DateTime.Parse(evt.Start.DateTime);
            timeText = startTime.ToString("HH:mm");
            if (evt.End?.DateTime != null)
            {
                var endTime = DateTime.Parse(evt.End.DateTime);
                timeText += $" - {endTime:HH:mm}";
            }
        }

        var timeBlock = new System.Windows.Controls.TextBlock
        {
            Text = timeText,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
        };
        stack.Children.Add(timeBlock);

        // 제목
        var titleBlock = new System.Windows.Controls.TextBlock
        {
            Text = evt.Subject ?? "(제목 없음)",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
        };
        stack.Children.Add(titleBlock);

        // 장소 (있는 경우)
        if (!string.IsNullOrEmpty(evt.Location?.DisplayName))
        {
            var locationBlock = new System.Windows.Controls.TextBlock
            {
                Text = $"📍 {evt.Location.DisplayName}",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(locationBlock);
        }

        card.Child = stack;

        // 호버 효과
        card.MouseEnter += (s, e) =>
        {
            card.Background = (Brush)FindResource("SubtleFillColorSecondaryBrush");
        };
        card.MouseLeave += (s, e) =>
        {
            card.Background = Brushes.Transparent;
        };

        return card;
    }

    /// <summary>
    /// 캘린더 세부 패널(우측) 업데이트
    /// </summary>
    private void UpdateCalendarDetailPanel(DateTime date, List<Microsoft.Graph.Models.Event> events)
    {
        if (CalendarDetailPanel == null) return;

        // 날짜 정보 업데이트
        if (CalDetailDateText != null)
            CalDetailDateText.Text = $"{date:yyyy년 M월 d일}";
        if (CalDetailDayText != null)
            CalDetailDayText.Text = date.ToString("dddd", new System.Globalization.CultureInfo("ko-KR"));

        // 일정 개수 업데이트
        if (CalDetailEventCountText != null)
            CalDetailEventCountText.Text = $"일정 ({events.Count}건)";

        // 기존 일정 아이템 제거
        if (CalDetailEventsList != null)
        {
            var itemsToRemove = CalDetailEventsList.Children.Cast<UIElement>()
                .Where(c => c != CalDetailNoEventsText)
                .ToList();
            foreach (var item in itemsToRemove)
                CalDetailEventsList.Children.Remove(item);

            // 일정 없음 텍스트 표시
            if (CalDetailNoEventsText != null)
                CalDetailNoEventsText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 일정 카드 추가
            foreach (var evt in events.OrderBy(e => e.Start?.DateTime))
            {
                var capturedEvent = evt;
                var eventCard = CreateDetailEventCard(evt);
                eventCard.MouseLeftButtonDown += async (s, e) =>
                {
                    e.Handled = true;
                    await OpenEventEditDialogAsync(capturedEvent, null);
                };
                CalDetailEventsList.Children.Insert(CalDetailEventsList.Children.Count - 1, eventCard);
            }
        }

        // 월간 요약 업데이트
        UpdateCalendarMonthlySummary();
    }

    /// <summary>
    /// 세부 일정 카드 생성 (우측 패널용)
    /// </summary>
    private Border CreateDetailEventCard(Microsoft.Graph.Models.Event evt)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("SubtleFillColorSecondaryBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };

        var mainStack = new StackPanel();

        // 상단: 시간 + 아이콘
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string timeText = "";
        if (evt.IsAllDay ?? false)
        {
            timeText = "종일";
        }
        else if (evt.Start?.DateTime != null)
        {
            var startTime = DateTime.Parse(evt.Start.DateTime);
            timeText = startTime.ToString("HH:mm");
            if (evt.End?.DateTime != null)
            {
                var endTime = DateTime.Parse(evt.End.DateTime);
                timeText += $" - {endTime:HH:mm}";
            }
        }

        var timeBlock = new System.Windows.Controls.TextBlock
        {
            Text = timeText,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
        };
        Grid.SetColumn(timeBlock, 0);
        headerGrid.Children.Add(timeBlock);

        // Teams 회의 아이콘
        if (evt.IsOnlineMeeting ?? false)
        {
            var teamsIcon = new System.Windows.Controls.TextBlock
            {
                Text = "🔗",
                FontSize = 14,
                ToolTip = "Teams 온라인 회의"
            };
            Grid.SetColumn(teamsIcon, 1);
            headerGrid.Children.Add(teamsIcon);
        }

        mainStack.Children.Add(headerGrid);

        // 제목
        var titleBlock = new System.Windows.Controls.TextBlock
        {
            Text = evt.Subject ?? "(제목 없음)",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
        };
        mainStack.Children.Add(titleBlock);

        // 장소
        if (!string.IsNullOrEmpty(evt.Location?.DisplayName))
        {
            var locationStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            locationStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "📍",
                FontSize = 12,
                Margin = new Thickness(0, 0, 4, 0)
            });
            locationStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = evt.Location.DisplayName,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
            });
            mainStack.Children.Add(locationStack);
        }

        // 참석자
        if (evt.Attendees != null && evt.Attendees.Any())
        {
            var attendeeStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            attendeeStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "👥",
                FontSize = 12,
                Margin = new Thickness(0, 0, 4, 0)
            });
            var attendeeNames = evt.Attendees
                .Where(a => a.EmailAddress?.Name != null)
                .Take(3)
                .Select(a => a.EmailAddress!.Name);
            var attendeeText = string.Join(", ", attendeeNames);
            if (evt.Attendees.Count() > 3)
                attendeeText += $" 외 {evt.Attendees.Count() - 3}명";

            attendeeStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = attendeeText,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            mainStack.Children.Add(attendeeStack);
        }

        // 색상 표시 막대
        card.BorderBrush = GetEventColor(evt);
        card.BorderThickness = new Thickness(3, 0, 0, 0);

        card.Child = mainStack;

        // 호버 효과
        card.MouseEnter += (s, e) =>
        {
            card.Background = (Brush)FindResource("SubtleFillColorTertiaryBrush");
        };
        card.MouseLeave += (s, e) =>
        {
            card.Background = (Brush)FindResource("SubtleFillColorSecondaryBrush");
        };

        return card;
    }

    /// <summary>
    /// 월간 요약 업데이트
    /// </summary>
    private void UpdateCalendarMonthlySummary()
    {
        if (_currentMonthEvents == null) return;

        // 총 일정
        if (CalDetailMonthTotalText != null)
            CalDetailMonthTotalText.Text = $"{_currentMonthEvents.Count}건";

        // 이번 주 일정
        var startOfWeek = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);
        var weekEvents = _currentMonthEvents.Count(e =>
        {
            if (e.Start?.DateTime == null) return false;
            var eventDate = DateTime.Parse(e.Start.DateTime).Date;
            return eventDate >= startOfWeek && eventDate < endOfWeek;
        });

        if (CalDetailWeekTotalText != null)
            CalDetailWeekTotalText.Text = $"{weekEvents}건";
    }

    /// <summary>
    /// 캘린더 세부 패널의 일정 추가 버튼 클릭
    /// </summary>
    private async void CalDetailAddEventButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenEventEditDialogAsync(null, _selectedCalendarDate);
    }

    /// <summary>
    /// TODO 추가 버튼 클릭 (캘린더 패널 To Do 탭)
    /// </summary>
    private void AddTodoButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Graph API Tasks 연동하여 할 일 추가 다이얼로그 열기
        Log4.Info("TODO 추가 버튼 클릭");
        _viewModel.StatusMessage = "할 일 추가 기능은 추후 지원 예정입니다.";
    }

    /// <summary>
    /// 미니 캘린더 그리드 업데이트
    /// </summary>
    private void UpdateMiniCalendarGrid()
    {
        if (MiniCalendarGrid == null) return;

        // 기존 날짜 버튼 제거 (요일 헤더 제외)
        var toRemove = MiniCalendarGrid.Children.Cast<UIElement>()
            .Where(c => Grid.GetRow(c) > 0)
            .ToList();
        foreach (var child in toRemove)
            MiniCalendarGrid.Children.Remove(child);

        var firstDay = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_currentCalendarDate.Year, _currentCalendarDate.Month);
        var startDayOfWeek = (int)firstDay.DayOfWeek;

        var day = 1;
        for (int week = 0; week < 6 && day <= daysInMonth; week++)
        {
            for (int dayOfWeek = 0; dayOfWeek < 7 && day <= daysInMonth; dayOfWeek++)
            {
                if (week == 0 && dayOfWeek < startDayOfWeek)
                    continue;

                var date = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, day);
                var isToday = date.Date == DateTime.Today;

                var btn = new System.Windows.Controls.Button
                {
                    Content = day.ToString(),
                    Width = 28,
                    Height = 28,
                    FontSize = 11,
                    Background = isToday ?
                        (Brush)FindResource("SystemAccentColorSecondaryBrush") :
                        Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Tag = date
                };
                btn.Click += MiniCalendarDay_Click;

                Grid.SetRow(btn, week + 1);
                Grid.SetColumn(btn, dayOfWeek);
                MiniCalendarGrid.Children.Add(btn);

                day++;
            }
        }
    }

    /// <summary>
    /// 미니 캘린더 날짜 클릭
    /// </summary>
    private void MiniCalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is DateTime date)
        {
            _currentCalendarDate = date;
            UpdateCalendarDisplay();
            Log4.Info($"미니 캘린더 날짜 선택: {date:yyyy-MM-dd}");
        }
    }

    #endregion

    #region 새로운 타이틀바 기능 (테마 토글, 고급 검색, 재로그인)

    /// <summary>
    /// 테마 토글 버튼 클릭
    /// </summary>
    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 테마 토글");
        var themeService = Services.Theme.ThemeService.Instance;
        themeService.ToggleTheme();
        UpdateThemeIcon();
    }

    /// <summary>
    /// 테마 아이콘 업데이트
    /// </summary>
    private void UpdateThemeIcon()
    {
        var themeService = Services.Theme.ThemeService.Instance;
        // 다크모드일 때 해(Sun) 아이콘 = "라이트모드로 전환"
        // 라이트모드일 때 달(Moon) 아이콘 = "다크모드로 전환"
        ThemeIcon.Symbol = themeService.IsDarkMode
            ? Wpf.Ui.Controls.SymbolRegular.WeatherSunny24
            : Wpf.Ui.Controls.SymbolRegular.WeatherMoon24;

        // AI 분석 별 색상 업데이트 (라이트모드: 진한 주황, 다크모드: 밝은 골드)
        UpdateAISyncStarColors(themeService.IsDarkMode);

        // 테마 메뉴 하이라이팅 업데이트
        UpdateThemeMenuHighlight(themeService.IsDarkMode);
    }

    /// <summary>
    /// 테마 메뉴 하이라이팅 업데이트
    /// </summary>
    private void UpdateThemeMenuHighlight(bool isDarkMode)
    {
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));
        var normalBrush = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush");

        if (MenuThemeDarkIcon != null && MenuThemeDarkText != null)
        {
            MenuThemeDarkIcon.Foreground = isDarkMode ? highlightColor : normalBrush;
            MenuThemeDarkText.Foreground = isDarkMode ? highlightColor : normalBrush;
        }

        if (MenuThemeLightIcon != null && MenuThemeLightText != null)
        {
            MenuThemeLightIcon.Foreground = isDarkMode ? normalBrush : highlightColor;
            MenuThemeLightText.Foreground = isDarkMode ? normalBrush : highlightColor;
        }
    }

    /// <summary>
    /// AI 분석 별 색상 및 메뉴 아이콘 색상 업데이트 (테마에 따라)
    /// </summary>
    private void UpdateAISyncStarColors(bool isDarkMode)
    {
        if (isDarkMode)
        {
            // 다크모드: 밝은 골드/노랑 계열
            AISyncStar1.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFD700"));
            AISyncStar2.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFDF00"));
            AISyncStar3.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFC125"));

            // AI 메뉴 아이콘 색상 (다크모드)
            MenuAISyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFD700")); // 분석 중지: 노랑
            MenuAISyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888888")); // 분석 시작: 회색

            // 메일 동기화 메뉴 아이콘 색상 (다크모드)
            MenuMailSyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3")); // 동기화 중지: 파랑
            MenuMailSyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888888")); // 동기화 시작: 회색
        }
        else
        {
            // 라이트모드: 진한 주황/갈색 계열 (더 잘 보임)
            AISyncStar1.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E69500"));
            AISyncStar2.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D98C00"));
            AISyncStar3.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CC7A00"));

            // AI 메뉴 아이콘 색상 (라이트모드: 진한 색상)
            MenuAISyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E69500")); // 분석 중지: 진한 주황
            MenuAISyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555555")); // 분석 시작: 진한 회색

            // 메일 동기화 메뉴 아이콘 색상 (라이트모드: 진한 색상)
            MenuMailSyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1565C0")); // 동기화 중지: 진한 파랑
            MenuMailSyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555555")); // 동기화 시작: 진한 회색
        }
    }

    /// <summary>
    /// 고급 검색 버튼 클릭
    /// </summary>
    private void AdvancedSearchButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 고급 검색 토글");
        AdvancedSearchPopup.IsOpen = !AdvancedSearchPopup.IsOpen;
    }

    /// <summary>
    /// 고급 검색 실행
    /// </summary>
    private void AdvancedSearch_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("고급 검색 실행");
        AdvancedSearchPopup.IsOpen = false;
        _viewModel.ExecuteAdvancedSearchCommand.Execute(null);
    }

    /// <summary>
    /// 고급 검색 초기화
    /// </summary>
    private void AdvancedSearchClear_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("고급 검색 초기화");
        _viewModel.ClearAdvancedSearchCommand.Execute(null);
    }

    /// <summary>
    /// DatePicker 로드 시 날짜 형식 설정 (yyyy년 MM월 dd일)
    /// </summary>
    private void DatePicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker datePicker)
        {
            // DatePicker 내부의 TextBox를 찾아서 형식 적용
            datePicker.SelectedDateChanged += (s, args) =>
            {
                ApplyDateFormat(datePicker);
            };
            ApplyDateFormat(datePicker);
        }
    }

    /// <summary>
    /// DatePicker에 커스텀 날짜 형식 적용
    /// </summary>
    private void ApplyDateFormat(DatePicker datePicker)
    {
        if (datePicker.SelectedDate.HasValue)
        {
            var textBox = FindChild<System.Windows.Controls.Primitives.DatePickerTextBox>(datePicker);
            if (textBox != null)
            {
                textBox.Text = datePicker.SelectedDate.Value.ToString("yyyy년 MM월 dd일");
            }
        }
    }

    /// <summary>
    /// 시각적 트리에서 자식 요소 찾기
    /// </summary>
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// 재로그인 메뉴 클릭
    /// </summary>
    private void MenuRelogin_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 재로그인");
        // 로그아웃 후 다시 로그인
        MenuLogout_Click(sender, e);
    }

    /// <summary>
    /// 종료 메뉴 클릭
    /// </summary>
    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 종료");
        Application.Current.Shutdown();
    }

    #endregion

    #region 최근 검색어 관리

    /// <summary>
    /// 최근 검색어 파일 경로
    /// </summary>
    private static string RecentSearchesFilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mailX", "recent_searches.json");

    /// <summary>
    /// 최근 검색어 로드 (JSON 파일에서)
    /// </summary>
    private void LoadRecentSearches()
    {
        try
        {
            if (System.IO.File.Exists(RecentSearchesFilePath))
            {
                var json = System.IO.File.ReadAllText(RecentSearchesFilePath);
                var searches = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (searches != null)
                {
                    _recentSearches.Clear();
                    foreach (var search in searches.Take(MaxRecentSearches))
                    {
                        _recentSearches.Add(search);
                    }
                }
            }
            Log4.Debug($"최근 검색어 로드: {_recentSearches.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"최근 검색어 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 최근 검색어 저장
    /// </summary>
    private void SaveRecentSearches()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(RecentSearchesFilePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            var json = System.Text.Json.JsonSerializer.Serialize(_recentSearches.ToList());
            System.IO.File.WriteAllText(RecentSearchesFilePath, json);
            Log4.Debug($"최근 검색어 저장: {_recentSearches.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"최근 검색어 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 검색어 추가 (중복 제거, 최신 항목 맨 위)
    /// </summary>
    private void AddRecentSearch(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return;

        var trimmed = searchText.Trim();

        // 중복 제거
        if (_recentSearches.Contains(trimmed))
        {
            _recentSearches.Remove(trimmed);
        }

        // 맨 앞에 추가
        _recentSearches.Insert(0, trimmed);

        // 최대 개수 초과 시 뒤에서 제거
        while (_recentSearches.Count > MaxRecentSearches)
        {
            _recentSearches.RemoveAt(_recentSearches.Count - 1);
        }

        SaveRecentSearches();
    }

    /// <summary>
    /// 최근 검색어 항목 클릭
    /// </summary>
    private void RecentSearchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string searchText)
        {
            TitleBarSearchBox.Text = searchText;
            _viewModel.SearchKeyword = searchText;
            SearchAutocompletePopup.IsOpen = false;
            _viewModel.SearchCommand.Execute(null);
            AddRecentSearch(searchText);
        }
    }

    /// <summary>
    /// 최근 검색어 삭제 버튼 클릭
    /// </summary>
    private void RemoveRecentSearch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string searchText)
        {
            _recentSearches.Remove(searchText);
            SaveRecentSearches();
            Log4.Debug($"검색어 삭제: {searchText}");
            e.Handled = true; // 부모 Border의 클릭 이벤트 전파 방지
        }
    }

    #endregion
}
