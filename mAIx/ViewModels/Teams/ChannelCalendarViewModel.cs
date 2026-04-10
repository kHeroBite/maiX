using mAIx.Services.Graph;

namespace mAIx.ViewModels;

/// <summary>
/// Teams 채널 일정 탭 ViewModel (Phase 1 placeholder — Phase 3에서 구현)
/// </summary>
public partial class ChannelCalendarViewModel : ViewModelBase
{
    private readonly GraphCalendarService _calendarService;

    public ChannelCalendarViewModel(GraphCalendarService calendarService)
    {
        _calendarService = calendarService;
    }
}
