using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using mAIx.Services.Graph;
using NLog;

namespace mAIx.ViewModels;

/// <summary>
/// 채널 파일 탭 Sub-ViewModel — Hub(TeamsViewModel)에서 채널별 Lazy 생성
/// </summary>
public partial class ChannelFilesViewModel : ObservableObject
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly GraphTeamsService _teamsService;
    private readonly string _teamId;
    private readonly string _channelId;

    /// <summary>
    /// 채널 파일 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChannelFileViewModel> _files = new();

    /// <summary>
    /// 로딩 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// 오프라인(연결 실패) 여부
    /// </summary>
    [ObservableProperty]
    private bool _isOffline;

    /// <summary>
    /// 오류 발생 여부
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// 오류 메시지
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 생성자
    /// </summary>
    /// <param name="teamsService">Graph Teams 서비스</param>
    /// <param name="teamId">팀 ID</param>
    /// <param name="channelId">채널 ID</param>
    public ChannelFilesViewModel(GraphTeamsService teamsService, string teamId, string channelId)
    {
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _teamId = teamId ?? throw new ArgumentNullException(nameof(teamId));
        _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
    }

    /// <summary>
    /// 채널 파일 로드 (기존 GetChannelFilesAsync 재사용)
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            var driveItems = await _teamsService.GetChannelFilesAsync(_teamId, _channelId);

            Files.Clear();
            foreach (var item in driveItems)
            {
                Files.Add(new ChannelFileViewModel
                {
                    Id = item.Id ?? string.Empty,
                    Name = item.Name ?? "(파일명 없음)",
                    Size = item.Size ?? 0,
                    LastModified = item.LastModifiedDateTime?.DateTime ?? DateTime.Now,
                    WebUrl = item.WebUrl ?? string.Empty,
                    IsFolder = item.Folder != null
                });
            }

            IsOffline = false;
            _log.Debug("채널 파일 로드 완료: teamId={TeamId}, channelId={ChannelId}, count={Count}",
                _teamId, _channelId, Files.Count);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = "파일 목록을 불러오지 못했습니다.";
            IsOffline = ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
                     || ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase);
            _log.Error(ex, "채널 파일 로드 실패: teamId={TeamId}, channelId={ChannelId}", _teamId, _channelId);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
