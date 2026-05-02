using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using mAIx.ViewModels;
using mAIx.Utils;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — Planner 칸반 보드 강화 핸들러 (Phase 6)
    /// 기존 핸들러는 MainWindow.xaml.cs에 유지, 여기는 Phase 6 신규 기능만
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 칸반 뷰 버튼 클릭
        /// </summary>
        private void PlannerKanbanViewButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchPlannerToKanbanView();
        }

        /// <summary>
        /// 타임라인 뷰 버튼 클릭
        /// </summary>
        private void PlannerTimelineViewButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchPlannerToTimelineView();
        }

        /// <summary>
        /// Planner 뷰를 칸반 보드로 전환
        /// </summary>
        private void SwitchPlannerToKanbanView()
        {
            if (_plannerViewModel == null) return;
            _plannerViewModel.SwitchToKanban();
            Log4.Debug("[Planner] 칸반 뷰로 전환");
        }

        /// <summary>
        /// Planner 뷰를 타임라인으로 전환
        /// </summary>
        private void SwitchPlannerToTimelineView()
        {
            if (_plannerViewModel == null) return;
            _plannerViewModel.SwitchToTimeline();
            Log4.Debug("[Planner] 타임라인 뷰로 전환");
        }

        /// <summary>
        /// 버킷 삭제 확인 후 실행
        /// </summary>
        private async void DeletePlannerBucketWithConfirmation(BucketViewModel bucket)
        {
            try
            {
                if (bucket == null || _plannerViewModel == null) return;

                var result = MessageBox.Show(
                    $"'{bucket.Name}' 버킷을 삭제하시겠습니까?\n버킷 내 작업도 함께 삭제됩니다.",
                    "버킷 삭제",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                try
                {
                    if (!string.IsNullOrEmpty(bucket.ETag))
                    {
                        var service = ((App)Application.Current).GetService<Services.Graph.GraphPlannerService>();
                        if (service != null)
                        {
                            var success = await service.DeleteBucketAsync(bucket.Id, bucket.ETag);
                            if (success)
                            {
                                _plannerViewModel.Buckets.Remove(bucket);
                                Log4.Info($"[Planner] 버킷 삭제 완료: {bucket.Name}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log4.Error($"[Planner] 버킷 삭제 실패: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"[MainWindow] DeletePlannerBucketWithConfirmation 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 작업 카드 상세 다이얼로그 표시
        /// </summary>
        private async void ShowPlannerTaskDetail(TaskItemViewModel task)
        {
            try
            {
                if (task == null || _plannerViewModel == null) return;

                try
                {
                    var service = ((App)Application.Current).GetService<Services.Graph.GraphPlannerService>();
                    if (service != null)
                    {
                        var details = await service.GetTaskDetailsAsync(task.Id);
                        if (details != null)
                        {
                            task.Notes = details.Description;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[Planner] 작업 상세 로드 실패: {ex.Message}");
                }

                Log4.Debug($"[Planner] 작업 상세: {task.Title}");
            }
            catch (Exception ex)
            {
                Log4.Error($"[MainWindow] ShowPlannerTaskDetail 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
