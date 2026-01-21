using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mailX.Services.Graph;
using Serilog;

namespace mailX.ViewModels;

/// <summary>
/// 통화/프레즌스 ViewModel
/// </summary>
public partial class CallsViewModel : ObservableObject
{
    private readonly GraphCallService _callService;
    private readonly ILogger _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    #region 프레즌스

    [ObservableProperty]
    private string _myAvailability = "Unknown";

    [ObservableProperty]
    private string _myActivity = string.Empty;

    #endregion

    #region 연락처

    [ObservableProperty]
    private ObservableCollection<ContactItemViewModel> _frequentContacts = new();

    [ObservableProperty]
    private ObservableCollection<ContactItemViewModel> _searchResults = new();

    [ObservableProperty]
    private ContactItemViewModel? _selectedContact;

    #endregion

    #region 통화 기록

    [ObservableProperty]
    private ObservableCollection<CallRecordViewModel> _callHistory = new();

    [ObservableProperty]
    private string _currentTab = "history"; // history, dialpad, voicemail, contacts

    #endregion

    #region 다이얼 패드

    [ObservableProperty]
    private string _dialNumber = string.Empty;

    #endregion

    public bool HasSelectedContact => SelectedContact != null;
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasCallHistory => CallHistory.Count > 0;

    public CallsViewModel(GraphCallService callService)
    {
        _callService = callService ?? throw new ArgumentNullException(nameof(callService));
        _logger = Log.ForContext<CallsViewModel>();
    }

    private async Task ExecuteAsync(Func<Task> action, string errorMessage)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, errorMessage);
            ErrorMessage = $"{errorMessage}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 통화 뷰 초기화
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        await ExecuteAsync(async () =>
        {
            // 내 프레즌스 조회
            var myPresence = await _callService.GetMyPresenceAsync();
            if (myPresence != null)
            {
                MyAvailability = myPresence.Availability ?? "Unknown";
                MyActivity = myPresence.Activity ?? string.Empty;
            }

            // 자주 연락하는 사람 조회
            var contacts = await _callService.GetFrequentContactsAsync(10);
            FrequentContacts.Clear();
            foreach (var contact in contacts)
            {
                FrequentContacts.Add(new ContactItemViewModel
                {
                    Id = contact.Id ?? string.Empty,
                    DisplayName = contact.DisplayName ?? "(이름 없음)",
                    Email = contact.ScoredEmailAddresses?.FirstOrDefault()?.Address ?? string.Empty,
                    JobTitle = contact.JobTitle ?? string.Empty,
                    Department = contact.Department ?? string.Empty,
                    Availability = "Unknown"
                });
            }

            // 프레즌스 조회 (자주 연락하는 사람들)
            if (FrequentContacts.Any())
            {
                var userIds = FrequentContacts.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (userIds.Any())
                {
                    var presences = await _callService.GetPresencesByUserIdsAsync(userIds);
                    foreach (var presence in presences)
                    {
                        var contact = FrequentContacts.FirstOrDefault(c => c.Id == presence.Id);
                        if (contact != null)
                        {
                            contact.Availability = presence.Availability ?? "Unknown";
                        }
                    }
                }
            }

            _logger.Information("통화 뷰 초기화 완료: 연락처 {Count}명", FrequentContacts.Count);
        }, "통화 뷰 초기화 실패");
    }

    /// <summary>
    /// 사용자 검색
    /// </summary>
    [RelayCommand]
    public async Task SearchUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SearchQuery.Length < 2)
        {
            SearchResults.Clear();
            return;
        }

        await ExecuteAsync(async () =>
        {
            var users = await _callService.SearchUsersAsync(SearchQuery);

            SearchResults.Clear();
            foreach (var user in users)
            {
                SearchResults.Add(new ContactItemViewModel
                {
                    Id = user.Id ?? string.Empty,
                    DisplayName = user.DisplayName ?? "(이름 없음)",
                    Email = user.Mail ?? user.UserPrincipalName ?? string.Empty,
                    JobTitle = user.JobTitle ?? string.Empty,
                    Department = user.Department ?? string.Empty,
                    Phone = user.MobilePhone ?? user.BusinessPhones?.FirstOrDefault() ?? string.Empty,
                    Availability = "Unknown"
                });
            }

            // 검색 결과에 대한 프레즌스 조회
            if (SearchResults.Any())
            {
                var userIds = SearchResults.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (userIds.Any())
                {
                    var presences = await _callService.GetPresencesByUserIdsAsync(userIds);
                    foreach (var presence in presences)
                    {
                        var contact = SearchResults.FirstOrDefault(c => c.Id == presence.Id);
                        if (contact != null)
                        {
                            contact.Availability = presence.Availability ?? "Unknown";
                        }
                    }
                }
            }

            _logger.Debug("사용자 검색 '{Query}': {Count}명", SearchQuery, SearchResults.Count);
        }, "사용자 검색 실패");
    }

    /// <summary>
    /// 탭 전환
    /// </summary>
    [RelayCommand]
    public void SwitchTab(string tab)
    {
        CurrentTab = tab;
        OnPropertyChanged(nameof(CurrentTab));
    }

    /// <summary>
    /// 연락처 선택
    /// </summary>
    [RelayCommand]
    public void SelectContact(ContactItemViewModel? contact)
    {
        SelectedContact = contact;
        OnPropertyChanged(nameof(HasSelectedContact));
    }

    /// <summary>
    /// 다이얼 패드 숫자 입력
    /// </summary>
    [RelayCommand]
    public void DialDigit(string digit)
    {
        DialNumber += digit;
    }

    /// <summary>
    /// 다이얼 패드 지우기
    /// </summary>
    [RelayCommand]
    public void ClearDial()
    {
        DialNumber = string.Empty;
    }

    /// <summary>
    /// 다이얼 패드 백스페이스
    /// </summary>
    [RelayCommand]
    public void BackspaceDial()
    {
        if (!string.IsNullOrEmpty(DialNumber))
        {
            DialNumber = DialNumber[..^1];
        }
    }

    /// <summary>
    /// 전화 걸기 (실제 통화는 Azure Communication Services 필요)
    /// </summary>
    [RelayCommand]
    public void MakeCall()
    {
        var target = SelectedContact?.Phone ?? SelectedContact?.Email ?? DialNumber;
        if (string.IsNullOrEmpty(target))
        {
            ErrorMessage = "전화번호나 연락처를 선택해주세요.";
            return;
        }

        // 실제 통화 기능은 Azure Communication Services 연동 필요
        // 여기서는 알림만 표시
        _logger.Information("전화 걸기 시도: {Target}", target);
        ErrorMessage = $"실제 통화 기능은 Azure Communication Services 연동이 필요합니다. 대상: {target}";
    }

    /// <summary>
    /// 영상 통화 걸기
    /// </summary>
    [RelayCommand]
    public void MakeVideoCall()
    {
        var target = SelectedContact?.Email ?? string.Empty;
        if (string.IsNullOrEmpty(target))
        {
            ErrorMessage = "연락처를 선택해주세요.";
            return;
        }

        _logger.Information("영상 통화 시도: {Target}", target);
        ErrorMessage = $"실제 영상 통화 기능은 Azure Communication Services 연동이 필요합니다. 대상: {target}";
    }

    /// <summary>
    /// 내 상태 변경
    /// </summary>
    [RelayCommand]
    public async Task SetMyStatusAsync(string availability)
    {
        await ExecuteAsync(async () =>
        {
            var activity = availability switch
            {
                "Available" => "Available",
                "Busy" => "Busy",
                "DoNotDisturb" => "DoNotDisturb",
                "Away" => "Away",
                "Offline" => "Offline",
                _ => "Available"
            };

            var success = await _callService.SetMyPresenceAsync(availability, activity, TimeSpan.FromMinutes(60));
            if (success)
            {
                MyAvailability = availability;
                MyActivity = activity;
                _logger.Information("내 상태 변경: {Availability}", availability);
            }
        }, "상태 변경 실패");
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await InitializeAsync();
    }
}

/// <summary>
/// 연락처 아이템 ViewModel
/// </summary>
public partial class ContactItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _jobTitle = string.Empty;

    [ObservableProperty]
    private string _department = string.Empty;

    [ObservableProperty]
    private string _availability = "Unknown";

    /// <summary>
    /// 프레즌스 상태 색상
    /// </summary>
    public string AvailabilityColor => Availability switch
    {
        "Available" => "#107C10",  // 녹색
        "Busy" or "InACall" or "InAMeeting" => "#D13438",  // 빨강
        "DoNotDisturb" => "#D13438",  // 빨강
        "Away" or "BeRightBack" => "#FFAA44",  // 주황
        "Offline" => "#8A8886",  // 회색
        _ => "#8A8886"  // 기본 회색
    };

    /// <summary>
    /// 프레즌스 상태 텍스트
    /// </summary>
    public string AvailabilityText => Availability switch
    {
        "Available" => "대화 가능",
        "Busy" => "다른 용무 중",
        "InACall" => "통화 중",
        "InAMeeting" => "회의 중",
        "DoNotDisturb" => "방해 금지",
        "Away" => "자리 비움",
        "BeRightBack" => "곧 돌아옴",
        "Offline" => "오프라인",
        _ => "알 수 없음"
    };

    /// <summary>
    /// 표시 이니셜 (아바타용)
    /// </summary>
    public string Initials
    {
        get
        {
            if (string.IsNullOrEmpty(DisplayName)) return "?";
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            }
            return DisplayName[0].ToString().ToUpper();
        }
    }
}

/// <summary>
/// 통화 기록 ViewModel
/// </summary>
public partial class CallRecordViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _type = "incoming"; // incoming, outgoing, missed

    [ObservableProperty]
    private string _callerName = string.Empty;

    [ObservableProperty]
    private string _callerEmail = string.Empty;

    [ObservableProperty]
    private string _callerPhone = string.Empty;

    [ObservableProperty]
    private DateTime _startTime;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private bool _isMissed;

    [ObservableProperty]
    private bool _isVideoCall;

    /// <summary>
    /// 통화 유형 아이콘
    /// </summary>
    public string TypeIcon => Type switch
    {
        "incoming" => IsMissed ? "CallMissed24" : "CallInbound24",
        "outgoing" => "CallOutbound24",
        _ => "Call24"
    };

    /// <summary>
    /// 통화 시간 표시 (예: 3:45)
    /// </summary>
    public string DurationText => Duration.TotalSeconds > 0
        ? $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}"
        : "응답 없음";

    /// <summary>
    /// 통화 시간 표시 (상대적)
    /// </summary>
    public string TimeText
    {
        get
        {
            var diff = DateTime.Now - StartTime;
            if (diff.TotalMinutes < 1) return "방금 전";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}분 전";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}시간 전";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}일 전";
            return StartTime.ToString("MM/dd HH:mm");
        }
    }
}
