using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using mAIx.Services.Graph;
using NLog;

namespace mAIx.ViewModels;

/// <summary>
/// 채널 게시물 탭 Sub-ViewModel — Hub(TeamsViewModel)에서 채널별 Lazy 생성
/// </summary>
public partial class ChannelPostsViewModel : ObservableObject
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly GraphTeamsService _teamsService;
    private readonly string _teamId;
    private readonly string _channelId;

    /// <summary>
    /// 채널 메시지 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChannelMessageViewModel> _messages = new();

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
    public ChannelPostsViewModel(GraphTeamsService teamsService, string teamId, string channelId)
    {
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _teamId = teamId ?? throw new ArgumentNullException(nameof(teamId));
        _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
    }

    /// <summary>
    /// 채널 메시지 로드 (기존 GetChannelMessagesAsync 재사용)
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
            var messages = await _teamsService.GetChannelMessagesAsync(_teamId, _channelId, 50);

            Messages.Clear();
            foreach (var msg in messages.OrderBy(m => m.CreatedDateTime))
            {
                var bodyContent = msg.Body?.Content ?? string.Empty;
                var plainText = StripHtml(bodyContent) ?? string.Empty;

                Messages.Add(new ChannelMessageViewModel
                {
                    Id = msg.Id ?? string.Empty,
                    Content = plainText,
                    HtmlContent = bodyContent,
                    FromUser = msg.From?.User?.DisplayName ?? "알 수 없음",
                    CreatedDateTime = msg.CreatedDateTime?.ToLocalTime().DateTime ?? DateTime.Now,
                    ReplyCount = 0
                });
            }

            IsOffline = false;
            _log.Debug("채널 게시물 로드 완료: teamId={TeamId}, channelId={ChannelId}, count={Count}",
                _teamId, _channelId, Messages.Count);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = "게시물을 불러오지 못했습니다.";
            IsOffline = ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
                     || ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase);
            _log.Error(ex, "채널 게시물 로드 실패: teamId={TeamId}, channelId={ChannelId}", _teamId, _channelId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// HTML 태그 제거 (간단한 처리)
    /// </summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }
}
