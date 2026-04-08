using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mAIx.Models;
using mAIx.Services.Graph;
using mAIx.Services.Search;
using mAIx.Utils;
using Serilog;

namespace mAIx.ViewModels;

/// <summary>
/// 연락처 탭 ViewModel — 목록/검색/상세/CRUD
/// </summary>
public partial class ContactsViewModel : ViewModelBase
{
    private readonly GraphContactService _contactService;
    private readonly ContactSearchService _searchService;
    private readonly ILogger _logger;

    // 디바운싱용
    private CancellationTokenSource? _searchCts;

    #region Observable 속성

    [ObservableProperty]
    private ObservableCollection<ContactItemModel> _contacts = new();

    [ObservableProperty]
    private ObservableCollection<ContactItemModel> _filteredContacts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedContact))]
    private ContactItemModel? _selectedContact;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedGroup = "모든 연락처";

    [ObservableProperty]
    private ObservableCollection<string> _groups = new()
    {
        "모든 연락처", "VIP", "최근 연락", "회사", "개인"
    };

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isInitialized;

    #endregion

    public bool HasSelectedContact => SelectedContact != null;

    public ContactsViewModel(
        GraphContactService contactService,
        ContactSearchService searchService)
    {
        _contactService = contactService ?? throw new ArgumentNullException(nameof(contactService));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _logger = Log.ForContext<ContactsViewModel>();
    }

    /// <summary>
    /// 연락처 초기 로드
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (IsInitialized) return;

        await ExecuteAsync(async () =>
        {
            await LoadContactsInternalAsync();
            IsInitialized = true;
            _logger.Information("연락처 뷰 초기화 완료: {Count}명", TotalCount);
        }, "연락처 초기화 실패");
    }

    /// <summary>
    /// 연락처 새로고침
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await ExecuteAsync(async () =>
        {
            IsInitialized = false;
            await LoadContactsInternalAsync();
            IsInitialized = true;
            _logger.Information("연락처 새로고침 완료: {Count}명", TotalCount);
        }, "연락처 새로고침 실패");
    }

    /// <summary>
    /// 검색 텍스트 변경 시 (디바운싱 300ms)
    /// </summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(300, _searchCts.Token);
            ApplyFilter();
        }
        catch (TaskCanceledException)
        {
            // 정상 취소
        }
    }

    /// <summary>
    /// 그룹 선택 변경
    /// </summary>
    [RelayCommand]
    public void SelectGroup(string group)
    {
        SelectedGroup = group;
        ApplyFilter();
    }

    /// <summary>
    /// 연락처 선택
    /// </summary>
    [RelayCommand]
    public void SelectContact(ContactItemModel? contact)
    {
        SelectedContact = contact;
    }

    /// <summary>
    /// 연락처 생성
    /// </summary>
    [RelayCommand]
    public async Task CreateContactAsync()
    {
        await ExecuteAsync(async () =>
        {
            var newContact = new Contact
            {
                GivenName = "",
                Surname = "",
                EmailAddresses = new List<EmailAddress>(),
                BusinessPhones = new List<string>()
            };

            var created = await _contactService.CreateContactAsync(newContact);
            if (created != null)
            {
                var item = MapToContactItem(created);
                Contacts.Add(item);
                SelectedContact = item;
                ApplyFilter();
                _logger.Information("연락처 생성: {Id}", created.Id);
            }
        }, "연락처 생성 실패");
    }

    /// <summary>
    /// 연락처 삭제
    /// </summary>
    [RelayCommand]
    public async Task DeleteContactAsync(ContactItemModel? contact)
    {
        if (contact == null || string.IsNullOrEmpty(contact.Id)) return;

        await ExecuteAsync(async () =>
        {
            await _contactService.DeleteContactAsync(contact.Id);
            Contacts.Remove(contact);
            if (SelectedContact == contact) SelectedContact = null;
            ApplyFilter();
            _logger.Information("연락처 삭제: {Id}", contact.Id);
        }, "연락처 삭제 실패");
    }

    /// <summary>
    /// 이메일 보내기 (메일 작성 탭 연동)
    /// </summary>
    [RelayCommand]
    public void ComposeEmailToContact(ContactItemModel? contact)
    {
        if (contact == null || string.IsNullOrEmpty(contact.Email)) return;
        // MainWindow에서 이벤트를 수신하여 메일 작성 탭으로 이동
        ComposeEmailRequested?.Invoke(this, contact.Email);
    }

    /// <summary>
    /// 이메일 작성 요청 이벤트
    /// </summary>
    public event EventHandler<string>? ComposeEmailRequested;

    #region 내부 메서드

    private async Task LoadContactsInternalAsync()
    {
        var graphContacts = await _contactService.GetContactsAsync(500);
        var items = graphContacts.Select(MapToContactItem).ToList();

        Contacts.Clear();
        foreach (var item in items.OrderBy(c => c.DisplayName))
        {
            Contacts.Add(item);
        }

        TotalCount = Contacts.Count;

        // 카테고리에서 그룹 추출
        var categories = items
            .SelectMany(c => c.Categories)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        Groups.Clear();
        Groups.Add("모든 연락처");
        Groups.Add("VIP");
        foreach (var cat in categories.Where(c => c != "VIP"))
        {
            Groups.Add(cat);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim().ToLower() ?? "";
        var group = SelectedGroup;

        IEnumerable<ContactItemModel> filtered = Contacts;

        // 그룹 필터
        if (group != "모든 연락처")
        {
            filtered = filtered.Where(c =>
                c.Categories.Any(cat => cat.Equals(group, StringComparison.OrdinalIgnoreCase)));
        }

        // 검색 필터
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(c =>
                (c.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.CompanyName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Phone?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        FilteredContacts.Clear();
        foreach (var item in filtered)
        {
            FilteredContacts.Add(item);
        }
    }

    private static ContactItemModel MapToContactItem(Contact c)
    {
        var primaryEmail = c.EmailAddresses?.FirstOrDefault()?.Address ?? "";
        var phone = c.MobilePhone ?? c.BusinessPhones?.FirstOrDefault() ?? "";
        var categories = c.Categories?.ToList() ?? new List<string>();

        return new ContactItemModel
        {
            Id = c.Id ?? "",
            DisplayName = c.DisplayName ?? "(이름 없음)",
            Email = primaryEmail,
            Phone = phone,
            CompanyName = c.CompanyName ?? "",
            Department = c.Department ?? "",
            JobTitle = c.JobTitle ?? "",
            PersonalNotes = c.PersonalNotes ?? "",
            Categories = categories,
            IsVip = categories.Any(cat =>
                cat.Equals("VIP", StringComparison.OrdinalIgnoreCase) ||
                cat.Equals("중요", StringComparison.OrdinalIgnoreCase))
        };
    }

    #endregion
}

/// <summary>
/// 연락처 아이템 모델 (UI 바인딩용)
/// </summary>
public partial class ContactItemModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _phone = "";

    [ObservableProperty]
    private string _companyName = "";

    [ObservableProperty]
    private string _department = "";

    [ObservableProperty]
    private string _jobTitle = "";

    [ObservableProperty]
    private string _personalNotes = "";

    [ObservableProperty]
    private bool _isVip;

    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// 이니셜 (아바타용)
    /// </summary>
    public string Initials
    {
        get
        {
            if (string.IsNullOrEmpty(DisplayName)) return "?";
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
            return DisplayName.Length >= 2
                ? DisplayName[..2].ToUpper()
                : DisplayName.ToUpper();
        }
    }

    /// <summary>
    /// 아바타 배경색 (이름 해시 기반)
    /// </summary>
    public string AvatarColor
    {
        get
        {
            var colors = new[]
            {
                "#4A90D9", "#7B68EE", "#E91E63", "#FF5722",
                "#009688", "#4CAF50", "#FF9800", "#795548",
                "#607D8B", "#9C27B0", "#00BCD4", "#8BC34A"
            };
            var hash = Math.Abs((DisplayName ?? "").GetHashCode());
            return colors[hash % colors.Length];
        }
    }

    /// <summary>
    /// 직함 문자열
    /// </summary>
    public string? PositionString
    {
        get
        {
            if (!string.IsNullOrEmpty(Department) && !string.IsNullOrEmpty(JobTitle))
                return $"{Department} - {JobTitle}";
            return Department ?? JobTitle;
        }
    }
}
