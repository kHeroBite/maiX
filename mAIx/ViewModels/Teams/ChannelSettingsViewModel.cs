using Microsoft.EntityFrameworkCore;
using mAIx.Data;
using mAIx.Services.Graph;

namespace mAIx.ViewModels;

/// <summary>
/// Teams 채널 설정 탭 ViewModel (Phase 1 placeholder — Phase 4에서 구현)
/// </summary>
public partial class ChannelSettingsViewModel : ViewModelBase
{
    private readonly GraphTeamsService _teamsService;
    private readonly IDbContextFactory<mAIxDbContext> _dbContextFactory;

    public ChannelSettingsViewModel(
        GraphTeamsService teamsService,
        IDbContextFactory<mAIxDbContext> dbContextFactory)
    {
        _teamsService = teamsService;
        _dbContextFactory = dbContextFactory;
    }
}
