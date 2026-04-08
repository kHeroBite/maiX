using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.Services.Graph;
using mAIx.Utils;
using mAIx.ViewModels;
namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — ToDo(할일) 관련 핸들러
    /// </summary>
    public partial class MainWindow
    {
        private TodoViewModel? _todoViewModel;

        private TodoViewModel? GetTodoViewModel()
        {
            if (_todoViewModel == null)
            {
                _todoViewModel = ((App)Application.Current).GetService<TodoViewModel>();
            }
            return _todoViewModel;
        }

        /// <summary>
        /// ToDo 뷰 표시
        /// </summary>
        private async void ShowTodoView()
        {
            HideAllViews();

            if (TodoViewBorder != null) TodoViewBorder.Visibility = Visibility.Visible;

            _viewModel.StatusMessage = "할일";
            Services.Theme.ThemeService.Instance.ApplyFeatureTheme("todo");

            // ViewModel 로드
            var vm = GetTodoViewModel();
            if (vm != null)
            {
                TodoMainLoadingPanel.Visibility = Visibility.Visible;
                TodoEmptyPanel.Visibility = Visibility.Collapsed;

                try
                {
                    await vm.LoadDataCommand.ExecuteAsync(null);

                    // UI 업데이트
                    UpdateTodoListUI(vm);
                }
                catch (Exception ex)
                {
                    Log4.Error($"[MainWindow.Todo] 데이터 로드 실패: {ex.Message}");
                }
                finally
                {
                    TodoMainLoadingPanel.Visibility = Visibility.Collapsed;
                }
            }

            Log4.Info("ToDo 뷰 표시 완료");
        }

        /// <summary>
        /// Todo UI 전체 업데이트
        /// </summary>
        private void UpdateTodoListUI(TodoViewModel vm)
        {
            // 사용자 목록 업데이트
            if (TodoUserListsControl != null)
            {
                TodoUserListsControl.ItemsSource = vm.TaskLists;
            }

            // 작업 목록 업데이트
            if (TodoMainItemsControl != null)
            {
                TodoMainItemsControl.ItemsSource = vm.Tasks;
            }

            // 제목 업데이트
            if (TodoListTitle != null)
            {
                TodoListTitle.Text = vm.CurrentListTitle;
            }

            // 빈 상태 표시
            UpdateTodoEmptyState(vm);
        }

        /// <summary>
        /// 빈 상태 표시 업데이트
        /// </summary>
        private void UpdateTodoEmptyState(TodoViewModel vm)
        {
            if (TodoEmptyPanel != null)
            {
                TodoEmptyPanel.Visibility = vm.HasNoTasks ? Visibility.Visible : Visibility.Collapsed;
            }
            if (TodoTaskScrollViewer != null)
            {
                TodoTaskScrollViewer.Visibility = vm.HasNoTasks ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// 스마트 목록 클릭
        /// </summary>
        private void TodoSmartList_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetTodoViewModel();
            if (vm == null) return;

            if (sender is FrameworkElement fe && fe.Tag is string listType)
            {
                vm.SelectSmartListCommand.Execute(listType);
                UpdateTodoListUI(vm);
            }
        }

        /// <summary>
        /// 사용자 목록 클릭
        /// </summary>
        private void TodoUserList_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetTodoViewModel();
            if (vm == null) return;

            if (sender is FrameworkElement fe && fe.Tag is string listId)
            {
                var list = vm.TaskLists.FirstOrDefault(l => l.Id == listId);
                if (list != null)
                {
                    vm.SelectListCommand.Execute(list);
                    UpdateTodoListUI(vm);
                }
            }
        }

        /// <summary>
        /// 작업 추가 버튼 클릭
        /// </summary>
        private async void TodoAddTask_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetTodoViewModel();
            if (vm == null || TodoNewTaskInput == null) return;

            vm.NewTaskTitle = TodoNewTaskInput.Text;
            await vm.AddTaskCommand.ExecuteAsync(null);

            TodoNewTaskInput.Text = "";
            UpdateTodoListUI(vm);
        }

        /// <summary>
        /// 입력창 엔터 키 처리
        /// </summary>
        private async void TodoNewTaskInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var vm = GetTodoViewModel();
            if (vm == null || TodoNewTaskInput == null) return;

            vm.NewTaskTitle = TodoNewTaskInput.Text;
            await vm.AddTaskCommand.ExecuteAsync(null);

            TodoNewTaskInput.Text = "";
            UpdateTodoListUI(vm);
        }

        /// <summary>
        /// 작업 체크박스 로드 (완료 토글 바인딩)
        /// </summary>
        private void TodoMainItemCheckBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                checkBox.Checked -= TodoMainItemCheckBox_Changed;
                checkBox.Unchecked -= TodoMainItemCheckBox_Changed;
                checkBox.Checked += TodoMainItemCheckBox_Changed;
                checkBox.Unchecked += TodoMainItemCheckBox_Changed;
            }
        }

        private async void TodoMainItemCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is TodoTaskItem task)
            {
                var vm = GetTodoViewModel();
                if (vm == null) return;

                await vm.ToggleTaskCompleteCommand.ExecuteAsync(task);
                UpdateTodoListUI(vm);
            }
        }

        /// <summary>
        /// My Day 토글
        /// </summary>
        private void TodoToggleMyDay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoTaskItem task)
            {
                var vm = GetTodoViewModel();
                vm?.ToggleMyDayCommand.Execute(task);
                if (vm != null) UpdateTodoListUI(vm);
            }
        }

        /// <summary>
        /// 중요도 토글
        /// </summary>
        private async void TodoToggleImportant_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoTaskItem task)
            {
                var vm = GetTodoViewModel();
                if (vm == null) return;

                await vm.ToggleImportantCommand.ExecuteAsync(task);
                UpdateTodoListUI(vm);
            }
        }

        /// <summary>
        /// 작업 삭제
        /// </summary>
        private async void TodoDeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoTaskItem task)
            {
                var vm = GetTodoViewModel();
                if (vm == null) return;

                // 삭제 확인
                var result = System.Windows.MessageBox.Show(
                    $"'{task.Title}' 작업을 삭제하시겠습니까?",
                    "작업 삭제",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await vm.DeleteTaskCommand.ExecuteAsync(task);
                    UpdateTodoListUI(vm);
                }
            }
        }

        /// <summary>
        /// 새로고침
        /// </summary>
        private async void TodoRefresh_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetTodoViewModel();
            if (vm == null) return;

            TodoMainLoadingPanel.Visibility = Visibility.Visible;
            await vm.RefreshCommand.ExecuteAsync(null);
            UpdateTodoListUI(vm);
            TodoMainLoadingPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 플래그 이메일 동기화
        /// </summary>
        private async void TodoSyncFlagged_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetTodoViewModel();
            if (vm == null) return;

            try
            {
                var todoService = ((App)Application.Current).GetService<GraphToDoService>();
                if (todoService != null)
                {
                    TodoMainLoadingPanel.Visibility = Visibility.Visible;
                    await todoService.SyncFlaggedEmailsAsync();
                    await vm.RefreshCommand.ExecuteAsync(null);
                    UpdateTodoListUI(vm);
                    TodoMainLoadingPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"[MainWindow.Todo] 플래그 이메일 동기화 실패: {ex.Message}");
                TodoMainLoadingPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
