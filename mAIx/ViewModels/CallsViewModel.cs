using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Services;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.ViewModels;

/// <summary>
/// 통화/프레즌스 ViewModel — 통화 이력, 연락처 연동, 즐겨찾기
/// </summary>
public partial class CallsViewModel : ObservableObject
{
    private readonly ILogger _log = Log.ForContext<CallsViewModel>();
    private readonly GraphCallService _callService;
    private CrossTabIntegrationService? _crossTabService;

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
    private ObservableCollection<CallRecordViewModel> _filteredCallHistory = new();

    [ObservableProperty]
    private string _currentTab = "history"; // history, dialpad, voicemail, contacts

    [ObservableProperty]
    private string _currentCallFilter = "all"; // all, missed, incoming, outgoing

    [ObservableProperty]
    private int _missedCallCount;

    #endregion

    #region 다이얼 패드

    [ObservableProperty]
    private string _dialNumber = string.Empty;

    #endregion

    #region 즐겨찾기

    [ObservableProperty]
    private ObservableCollection<ContactItemViewModel> _favorites = new();

    #endregion

    public bool HasSelectedContact => SelectedContact != null;
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasCallHistory => CallHistory.Count > 0;

    public CallsViewModel(GraphCallService callService)
    {
        _callService = callService ?? throw new ArgumentNullException(nameof(callService));
    }

    /// <summary>
    /// 크로스탭 서비스 설정 (MainWindow에서 주입)
    /// </summary>
    public void SetCrossTabService(CrossTabIntegrationService service)
    {
        _crossTabService = service;
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
            _log.Error(ex, errorMessage);
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

            // 통화 이력 로드
            await LoadCallHistoryAsync();

            _log.Information("통화 뷰 초기화 완료: 연락처 {ContactCount}명, 통화이력 {HistoryCount}건", FrequentContacts.Count, CallHistory.Count);
        }, "통화 뷰 초기화 실패");
    }

    /// <summary>
    /// 통화 이력 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadCallHistoryAsync()
    {
        try
        {
            var records = await _callService.GetCallRecordsAsync(7);
            CallHistory.Clear();
            foreach (var record in records)
            {
                CallHistory.Add(new CallRecordViewModel
                {
                    Id = record.Id,
                    Type = record.Type,
                    CallerName = record.CallerName,
                    CallerEmail = record.CallerEmail,
                    CallerPhone = record.CallerPhone,
                    StartTime = record.StartTime,
                    Duration = record.Duration,
                    IsMissed = record.IsMissed,
                    IsVideoCall = record.IsVideoCall
                });
            }

            MissedCallCount = CallHistory.Count(r => r.IsMissed);
            FilterCallHistory();
            _log.Debug("통화 이력 로드: {Count}건 (부재중: {MissedCount}건)", CallHistory.Count, MissedCallCount);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "통화 이력 로드 실패");
        }
    }

    /// <summary>
    /// 통화 이력 필터링
    /// </summary>
    [RelayCommand]
    public void FilterCalls(string filterType)
    {
        CurrentCallFilter = filterType;
        FilterCallHistory();
    }

    private void FilterCallHistory()
    {
        IEnumerable<CallRecordViewModel> filtered = CurrentCallFilter switch
        {
            "missed" => CallHistory.Where(r => r.IsMissed),
            "incoming" => CallHistory.Where(r => r.Type == "incoming"),
            "outgoing" => CallHistory.Where(r => r.Type == "outgoing"),
            _ => CallHistory
        };

        FilteredCallHistory = new ObservableCollection<CallRecordViewModel>(
            filtered.OrderByDescending(r => r.StartTime));
    }

    /// <summary>
    /// 연락처에 연결
    /// </summary>
    [RelayCommand]
    public void LinkToContact(CallRecordViewModel record)
    {
        if (record == null) return;

        // 통화 기록의 이메일로 연락처 매칭
        var matched = FrequentContacts.FirstOrDefault(c =>
            c.Email.Equals(record.CallerEmail, StringComparison.OrdinalIgnoreCase));

        if (matched != null)
        {
            SelectedContact = matched;
            OnPropertyChanged(nameof(HasSelectedContact));
            _log.Debug("통화 기록 → 연락처 연결: {DisplayName}", matched.DisplayName);
        }
    }

    /// <summary>
    /// 즐겨찾기 추가/제거
    /// </summary>
    [RelayCommand]
    public void ToggleFavorite(ContactItemViewModel contact)
    {
        if (contact == null) return;

        var existing = Favorites.FirstOrDefault(f => f.Id == contact.Id);
        if (existing != null)
        {
            Favorites.Remove(existing);
            _log.Debug("즐겨찾기 제거: {DisplayName}", contact.DisplayName);
        }
        else
        {
            Favorites.Add(contact);
            _log.Debug("즐겨찾기 추가: {DisplayName}", contact.DisplayName);
        }
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

            _log.Debug("사용자 검색 '{Query}': {Count}명", SearchQuery, SearchResults.Count);
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
    /// 전화 걸기
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

        _log.Information("전화 걸기 시도: {Target}", target);

        if (_crossTabService != null)
        {
            _ = _crossTabService.StartCallWithContactAsync(
                SelectedContact?.Email ?? string.Empty,
                SelectedContact?.Phone ?? DialNumber);
        }

        ErrorMessage = $"통화 기능은 Azure Communication Services 연동이 필요합니다. 대상: {target}";
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

        _log.Information("영상 통화 시도: {Target}", target);
        ErrorMessage = $"영상 통화 기능은 Azure Communication Services 연동이 필요합니다. 대상: {target}";
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
                _log.Information("내 상태 변경: {Availability}", availability);
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

    /// <summary>
    /// 연락처에서 Teams 채팅 시작 (크로스탭)
    /// </summary>
    public async Task StartTeamsChatAsync(ContactItemViewModel contact)
    {
        if (contact == null || _crossTabService == null) return;

        var chatId = await _crossTabService.StartTeamsChatWithContactAsync(contact.Email, contact.DisplayName);
        if (string.IsNullOrEmpty(chatId))
        {
            ErrorMessage = "Teams 채팅 시작에 실패했습니다.";
        }
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

    [ObservableProperty]
    private bool _isFavorite;

    public string AvailabilityColor => Availability switch
    {
        "Available" => "#107C10",
        "Busy" or "InACall" or "InAMeeting" => "#D13438",
        "DoNotDisturb" => "#D13438",
        "Away" or "BeRightBack" => "#FFAA44",
        "Offline" => "#8A8886",
        _ => "#8A8886"
    };

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

    public string TypeIcon => Type switch
    {
        "incoming" => IsMissed ? "CallMissed24" : "CallInbound24",
        "outgoing" => "CallOutbound24",
        _ => "Call24"
    };

    public string DurationText => Duration.TotalSeconds > 0
        ? $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}"
        : "응답 없음";

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

    public string TypeColor => IsMissed ? "#D13438" : "#808080";
}
