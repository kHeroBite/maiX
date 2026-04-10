using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.Graph;
using NLog;

namespace mAIx.ViewModels;

/// <summary>
/// Teams 채널 설정 탭 ViewModel — 채널 기본 정보, 멤버 목록, 알림 설정, 즐겨찾기 관리
/// </summary>
public partial class ChannelSettingsViewModel : ViewModelBase
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly GraphTeamsService _teamsService;
    private readonly IDbContextFactory<mAIxDbContext> _dbContextFactory;

    private string _teamId = string.Empty;
    private string _channelId = string.Empty;

    // ─── 채널 기본 정보 ───────────────────────────────────────

    /// <summary>채널 이름</summary>
    [ObservableProperty]
    private string _channelName = string.Empty;

    /// <summary>채널 설명</summary>
    [ObservableProperty]
    private string _channelDescription = string.Empty;

    /// <summary>채널 유형 (Standard / Private / Shared)</summary>
    [ObservableProperty]
    private string _channelType = "Standard";

    // ─── 멤버 목록 ────────────────────────────────────────────

    /// <summary>채널 멤버 목록</summary>
    [ObservableProperty]
    private ObservableCollection<ChannelMemberViewModel> _memberList = new();

    /// <summary>멤버 수</summary>
    public int MemberCount => MemberList.Count;

    // ─── 알림 설정 ────────────────────────────────────────────

    /// <summary>알림 설정 값: "mention_only" | "all_activity" | "off"</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotifyMentionOnly))]
    [NotifyPropertyChangedFor(nameof(IsNotifyAllActivity))]
    [NotifyPropertyChangedFor(nameof(IsNotifyOff))]
    private string _notificationSetting = "mention_only";

    public bool IsNotifyMentionOnly
    {
        get => NotificationSetting == "mention_only";
        set { if (value) NotificationSetting = "mention_only"; }
    }

    public bool IsNotifyAllActivity
    {
        get => NotificationSetting == "all_activity";
        set { if (value) NotificationSetting = "all_activity"; }
    }

    public bool IsNotifyOff
    {
        get => NotificationSetting == "off";
        set { if (value) NotificationSetting = "off"; }
    }

    // ─── 즐겨찾기 ─────────────────────────────────────────────

    /// <summary>채널 즐겨찾기 여부</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    [NotifyPropertyChangedFor(nameof(FavoriteLabel))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "StarOff24" : "Star24";
    public string FavoriteLabel => IsFavorite ? "즐겨찾기 해제" : "즐겨찾기 추가";

    // ─── 상태 ─────────────────────────────────────────────────

    /// <summary>저장 완료 알림 표시 여부</summary>
    [ObservableProperty]
    private bool _isSaved;

    public ChannelSettingsViewModel(
        GraphTeamsService teamsService,
        IDbContextFactory<mAIxDbContext> dbContextFactory)
    {
        _teamsService = teamsService;
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// 채널 설정 로드 — 채널 기본 정보 + 멤버 목록 + 로컬 DB 알림/즐겨찾기 설정
    /// </summary>
    public async Task LoadSettingsAsync(ChannelItemViewModel channel)
    {
        _teamId = channel.TeamId;
        _channelId = channel.Id;
        ChannelName = channel.DisplayName;
        ChannelDescription = channel.Description;
        ChannelType = MapMembershipType(channel.MembershipType);

        await Task.WhenAll(
            LoadMembersAsync(),
            LoadLocalSettingsAsync()
        );
    }

    private async Task LoadMembersAsync()
    {
        try
        {
            var members = await _teamsService.GetTeamMembersAsync(_teamId);
            MemberList.Clear();
            foreach (var m in members)
            {
                var displayName = m.DisplayName ?? "알 수 없음";
                var roles = m.Roles ?? new System.Collections.Generic.List<string>();
                var role = roles.Contains("owner") ? "소유자" : "구성원";

                MemberList.Add(new ChannelMemberViewModel
                {
                    DisplayName = displayName,
                    Role = role
                });
            }
            OnPropertyChanged(nameof(MemberCount));
            _log.Debug("채널 멤버 로드 완료: channelId={ChannelId}, count={Count}", _channelId, MemberList.Count);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "채널 멤버 로드 실패: channelId={ChannelId}", _channelId);
        }
    }

    private async Task LoadLocalSettingsAsync()
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var setting = await db.ChannelNotificationSettings
                .FirstOrDefaultAsync(s => s.TeamId == _teamId && s.ChannelId == _channelId);

            if (setting != null)
            {
                if (setting.IsMuted)
                    NotificationSetting = "off";
                else if (setting.NotifyOnNewPost)
                    NotificationSetting = "all_activity";
                else
                    NotificationSetting = "mention_only";

                IsFavorite = setting.IsFavorite;
            }
            else
            {
                NotificationSetting = "mention_only";
                IsFavorite = false;
            }

            _log.Debug("채널 로컬 설정 로드 완료: channelId={ChannelId}, notification={Setting}, favorite={Fav}",
                _channelId, NotificationSetting, IsFavorite);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "채널 로컬 설정 로드 실패: channelId={ChannelId}", _channelId);
        }
    }

    /// <summary>알림 설정 저장 (로컬 DB)</summary>
    [RelayCommand]
    private async Task SaveNotificationSettingAsync()
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var setting = await db.ChannelNotificationSettings
                .FirstOrDefaultAsync(s => s.TeamId == _teamId && s.ChannelId == _channelId);

            if (setting == null)
            {
                setting = new ChannelNotificationSetting
                {
                    TeamId = _teamId,
                    ChannelId = _channelId
                };
                db.ChannelNotificationSettings.Add(setting);
            }

            setting.IsMuted = NotificationSetting == "off";
            setting.NotifyOnNewPost = NotificationSetting == "all_activity";
            setting.NotifyOnMention = NotificationSetting != "off";

            await db.SaveChangesAsync();

            IsSaved = true;
            await Task.Delay(2000);
            IsSaved = false;

            _log.Info("채널 알림 설정 저장: channelId={ChannelId}, setting={Setting}", _channelId, NotificationSetting);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "채널 알림 설정 저장 실패: channelId={ChannelId}", _channelId);
            ErrorMessage = "설정 저장에 실패했습니다.";
        }
    }

    /// <summary>채널 즐겨찾기 토글</summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var setting = await db.ChannelNotificationSettings
                .FirstOrDefaultAsync(s => s.TeamId == _teamId && s.ChannelId == _channelId);

            if (setting == null)
            {
                setting = new ChannelNotificationSetting
                {
                    TeamId = _teamId,
                    ChannelId = _channelId,
                    NotifyOnMention = true
                };
                db.ChannelNotificationSettings.Add(setting);
            }

            setting.IsFavorite = !setting.IsFavorite;
            IsFavorite = setting.IsFavorite;

            await db.SaveChangesAsync();

            _log.Info("채널 즐겨찾기 토글: channelId={ChannelId}, isFavorite={IsFavorite}", _channelId, IsFavorite);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "채널 즐겨찾기 토글 실패: channelId={ChannelId}", _channelId);
            ErrorMessage = "즐겨찾기 변경에 실패했습니다.";
        }
    }

    private static string MapMembershipType(string membershipType) => membershipType switch
    {
        "private" => "Private",
        "shared" => "Shared",
        _ => "Standard"
    };
}

/// <summary>
/// 채널 멤버 표시용 ViewModel
/// </summary>
public partial class ChannelMemberViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _role = "구성원";

    /// <summary>아바타 이니셜 (첫 글자)</summary>
    public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[0].ToString().ToUpper();
}
