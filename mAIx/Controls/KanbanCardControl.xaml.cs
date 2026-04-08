using System;
using System.Windows;
using System.Windows.Controls;
using mAIx.ViewModels;
using Serilog;

namespace mAIx.Controls
{
    /// <summary>
    /// 칸반 카드 컨트롤 — 작업 카드 (배정자, 라벨, 체크리스트, 우선순위)
    /// </summary>
    public partial class KanbanCardControl : UserControl
    {
        private static readonly ILogger _log = Log.ForContext<KanbanCardControl>();

        /// <summary>
        /// 작업 열기 요청
        /// </summary>
        public event Action<TaskItemViewModel>? TaskOpenRequested;

        /// <summary>
        /// 작업 완료 토글 요청
        /// </summary>
        public event Action<TaskItemViewModel>? TaskCompleteToggled;

        /// <summary>
        /// 작업 삭제 요청
        /// </summary>
        public event Action<TaskItemViewModel>? TaskDeleteRequested;

        /// <summary>
        /// 우선순위 변경 요청 (task, newPriority)
        /// </summary>
        public event Action<TaskItemViewModel, int>? PriorityChangeRequested;

        public KanbanCardControl()
        {
            InitializeComponent();
        }

        private TaskItemViewModel? GetTask()
        {
            return DataContext as TaskItemViewModel;
        }

        private void OpenTask_Click(object sender, RoutedEventArgs e)
        {
            var task = GetTask();
            if (task != null)
            {
                TaskOpenRequested?.Invoke(task);
            }
        }

        private void CompleteTask_Click(object sender, RoutedEventArgs e)
        {
            var task = GetTask();
            if (task != null)
            {
                TaskCompleteToggled?.Invoke(task);
            }
        }

        private void CompleteCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var task = GetTask();
            if (task != null)
            {
                TaskCompleteToggled?.Invoke(task);
            }
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            var task = GetTask();
            if (task != null)
            {
                TaskDeleteRequested?.Invoke(task);
            }
        }

        private void SetPriority_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string priorityStr)
            {
                var task = GetTask();
                if (task != null && int.TryParse(priorityStr, out int priority))
                {
                    PriorityChangeRequested?.Invoke(task, priority);
                }
            }
        }
    }
}
