using Microsoft.EntityFrameworkCore;
using mAIx.Data;

namespace mAIx.ViewModels;

/// <summary>
/// Teams 채널 Wiki 탭 ViewModel (Phase 1 placeholder — kdev-4 Phase에서 구현)
/// </summary>
public partial class ChannelWikiViewModel : ViewModelBase
{
    private readonly IDbContextFactory<mAIxDbContext> _dbContextFactory;

    public ChannelWikiViewModel(IDbContextFactory<mAIxDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }
}
