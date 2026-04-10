using mAIx.Services.Graph;

namespace mAIx.ViewModels;

/// <summary>
/// Teams 채널 Planner 탭 ViewModel (Phase 1 placeholder — Phase 3에서 구현)
/// </summary>
public partial class ChannelPlannerViewModel : ViewModelBase
{
    private readonly GraphPlannerService _plannerService;

    public ChannelPlannerViewModel(GraphPlannerService plannerService)
    {
        _plannerService = plannerService;
    }
}
