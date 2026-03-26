using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Models;
using mAIx.Models.Settings;
using mAIx.Services.Graph;
using mAIx.Services.Storage;
using mAIx.Utils;

namespace mAIx.ViewModels;

/// <summary>
/// 로그인 화면 ViewModel - Microsoft 365 MSAL 인증 관리
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly GraphAuthService _graphAuthService;
    private readonly LoginSettingsService _loginSettingsService;

    public LoginViewModel(GraphAuthService graphAuthService)
    {
        _graphAuthService = graphAuthService;
        _loginSettingsService = new LoginSettingsService();

        // IsLoading 변경 시 CanLogin 알림
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsLoading))
            {
                OnPropertyChanged(nameof(CanLoginWithSaved));
                OnPropertyChanged(nameof(CanLogin));
            }
        };
    }

    /// <summary>
    /// 저장된 로그인 설정
    /// </summary>
    [ObservableProperty]
    private LoginSettings? _savedSettings;

    /// <summary>
    /// 에러 발생 여부
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// 입력된 ClientId (새 로그인 시)
    /// </summary>
    [ObservableProperty]
    private string? _clientId;

    /// <summary>
    /// 입력된 TenantId (새 로그인 시, 단일 테넌트 앱은 실제 테넌트 ID 필요)
    /// </summary>
    [ObservableProperty]
    private string _tenantId = "";

    /// <summary>
    /// 설정 저장 여부
    /// </summary>
    [ObservableProperty]
    private bool _saveSettings = true;

    /// <summary>
    /// 자동 로그인 여부
    /// </summary>
    [ObservableProperty]
    private bool _autoLogin = true;

    /// <summary>
    /// 저장된 계정이 있는지 여부
    /// </summary>
    public bool HasSavedAccount => SavedSettings != null && !string.IsNullOrEmpty(SavedSettings.Email);

    /// <summary>
    /// 저장된 ClientId가 있는지 여부
    /// </summary>
    public bool HasSavedClientId => SavedSettings?.AzureAd?.IsConfigured == true;

    /// <summary>
    /// ClientId 입력 필드 표시 여부 (저장된 ClientId가 없을 때)
    /// </summary>
    public bool ShowClientIdInput => !HasSavedClientId;

    /// <summary>
    /// 저장된 계정으로 로그인 가능 여부
    /// </summary>
    public bool CanLoginWithSaved => HasSavedAccount && HasSavedClientId && !IsLoading;

    /// <summary>
    /// 로그인 가능 여부 (ClientId와 TenantId가 있거나 입력됨)
    /// </summary>
    public bool CanLogin => !IsLoading && (HasSavedClientId || (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(TenantId)));

    /// <summary>
    /// 로그인 성공 이벤트 (View에서 DialogResult 설정용)
    /// </summary>
    public event Action? LoginSucceeded;

    /// <summary>
    /// SavedSettings 변경 시 computed 속성 알림
    /// </summary>
    partial void OnSavedSettingsChanged(LoginSettings? value)
    {
        OnPropertyChanged(nameof(HasSavedAccount));
        OnPropertyChanged(nameof(HasSavedClientId));
        OnPropertyChanged(nameof(ShowClientIdInput));
        OnPropertyChanged(nameof(CanLoginWithSaved));
        OnPropertyChanged(nameof(CanLogin));

        // 저장된 설정이 있으면 입력 필드에 표시
        if (value?.AzureAd != null)
        {
            if (!string.IsNullOrEmpty(value.AzureAd.ClientId))
            {
                ClientId = value.AzureAd.ClientId;
            }
            if (!string.IsNullOrEmpty(value.AzureAd.TenantId))
            {
                TenantId = value.AzureAd.TenantId;
            }
        }

        // 저장된 자동 로그인 설정 반영
        if (value != null)
        {
            AutoLogin = value.AutoLogin;
        }
    }

    /// <summary>
    /// 자동 로그인이 활성화되어 있고 저장된 계정이 있는지 여부
    /// </summary>
    public bool ShouldAutoLogin => HasSavedAccount && HasSavedClientId && SavedSettings?.AutoLogin == true;

    /// <summary>
    /// ClientId 변경 시 로그인 버튼 상태 업데이트
    /// </summary>
    partial void OnClientIdChanged(string? value)
    {
        OnPropertyChanged(nameof(CanLogin));
    }

    /// <summary>
    /// TenantId 변경 시 로그인 버튼 상태 업데이트
    /// </summary>
    partial void OnTenantIdChanged(string value)
    {
        OnPropertyChanged(nameof(CanLogin));
    }

    /// <summary>
    /// 저장된 로그인 설정 로드
    /// </summary>
    public void LoadSavedSettings()
    {
        try
        {
            SavedSettings = _loginSettingsService.Load();
            Log4.Debug($"[LoginViewModel] 저장된 설정 로드 - Email: {SavedSettings?.Email ?? "(없음)"}, ClientId: {(SavedSettings?.AzureAd?.IsConfigured == true ? "설정됨" : "없음")}");

            // 저장된 ClientId가 있으면 GraphAuthService 초기화
            if (SavedSettings?.AzureAd?.IsConfigured == true)
            {
                _graphAuthService.Initialize(SavedSettings.AzureAd);
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[LoginViewModel] 설정 로드 실패: {ex.Message}");
            SavedSettings = null;
        }
    }

    /// <summary>
    /// 대화형 로그인 (Microsoft 로그인 창 표시)
    /// </summary>
    [RelayCommand]
    private async Task LoginAsync()
    {
        Log4.Debug("[LoginViewModel] LoginAsync 시작");
        await ExecuteAsync(async () =>
        {
            HasError = false;

            // ClientId가 입력되었으면 GraphAuthService 초기화
            if (!string.IsNullOrWhiteSpace(ClientId) && !_graphAuthService.IsConfigured)
            {
                Log4.Debug($"[LoginViewModel] 입력된 ClientId로 GraphAuthService 초기화: {ClientId.Substring(0, Math.Min(8, ClientId.Length))}...");
                Log4.Debug($"[LoginViewModel] TenantId: {TenantId}");
                _graphAuthService.Initialize(ClientId, TenantId);
            }

            // ClientId, TenantId 확인
            if (!_graphAuthService.IsConfigured)
            {
                if (string.IsNullOrWhiteSpace(ClientId))
                    throw new Exception("Azure AD Client ID를 입력해주세요.");
                if (string.IsNullOrWhiteSpace(TenantId))
                    throw new Exception("Azure AD Tenant ID를 입력해주세요. (Azure Portal에서 디렉터리(테넌트) ID 확인)");
                throw new Exception("Azure AD 설정이 올바르지 않습니다.");
            }

            // MSAL 대화형 로그인 실행
            Log4.Debug("[LoginViewModel] MSAL 대화형 로그인 시작");
            var success = await _graphAuthService.LoginInteractiveAsync();

            if (!success)
            {
                throw new Exception("Microsoft 365 로그인이 취소되었거나 실패했습니다.");
            }

            Log4.Debug($"[LoginViewModel] MSAL 로그인 성공 - Email: {_graphAuthService.CurrentUserEmail}");

            // 설정 저장 (체크박스 선택 시)
            if (SaveSettings)
            {
                var settings = SavedSettings ?? new LoginSettings();
                settings.Email = _graphAuthService.CurrentUserEmail;
                settings.DisplayName = _graphAuthService.CurrentUserEmail?.Split('@')[0] ?? "User";
                settings.AutoLogin = AutoLogin;  // 자동 로그인 체크박스 값 반영
                settings.LastLoginAt = DateTime.Now;
                settings.AzureAd = new AzureAdSettings
                {
                    ClientId = ClientId ?? _graphAuthService.ClientId,
                    TenantId = TenantId
                };

                _loginSettingsService.Save(settings);
                SavedSettings = settings;
                Log4.Debug($"[LoginViewModel] 로그인 설정 저장 완료 (AutoLogin: {AutoLogin})");
            }

            // 로그인 성공 이벤트 발생
            LoginSucceeded?.Invoke();
            Log4.Debug("[LoginViewModel] LoginSucceeded 이벤트 발생");

        }, "로그인 실패");

        if (ErrorMessage != null)
        {
            Log4.Debug($"[LoginViewModel] 로그인 에러: {ErrorMessage}");
            HasError = true;
        }

        Log4.Debug("[LoginViewModel] LoginAsync 종료");
    }

    /// <summary>
    /// 저장된 계정으로 자동 로그인 (MSAL 토큰 캐시 사용)
    /// Silent 로그인 실패 시 자동으로 대화형 로그인 시도
    /// </summary>
    [RelayCommand]
    private async Task LoginWithSavedAccountAsync()
    {
        if (SavedSettings == null || string.IsNullOrEmpty(SavedSettings.Email))
        {
            Log4.Debug("[LoginViewModel] 저장된 계정 없음");
            return;
        }

        Log4.Debug($"[LoginViewModel] 저장된 계정으로 로그인 시도 - Email: {SavedSettings.Email}");

        await ExecuteAsync(async () =>
        {
            HasError = false;

            // 저장된 ClientId로 GraphAuthService 초기화
            if (SavedSettings.AzureAd?.IsConfigured == true && !_graphAuthService.IsConfigured)
            {
                _graphAuthService.Initialize(SavedSettings.AzureAd);
            }

            if (!_graphAuthService.IsConfigured)
            {
                throw new Exception("저장된 ClientId가 없습니다. ClientId를 입력해주세요.");
            }

            // MSAL 토큰 캐시에서 자동 로그인 시도
            Log4.Debug("[LoginViewModel] MSAL Silent 로그인 시도");
            var success = await _graphAuthService.LoginSilentAsync(SavedSettings.Email);

            if (!success)
            {
                // 캐시된 토큰이 없거나 만료된 경우 대화형 로그인 자동 시도
                Log4.Debug("[LoginViewModel] Silent 로그인 실패 - 대화형 로그인으로 전환");
                success = await _graphAuthService.LoginInteractiveAsync();

                if (!success)
                {
                    throw new Exception("Microsoft 365 로그인이 취소되었거나 실패했습니다.");
                }
            }

            Log4.Debug($"[LoginViewModel] 로그인 성공 - Email: {_graphAuthService.CurrentUserEmail}");

            // 마지막 로그인 시간 업데이트
            SavedSettings.LastLoginAt = DateTime.Now;
            _loginSettingsService.Save(SavedSettings);

            // 로그인 성공 이벤트 발생
            LoginSucceeded?.Invoke();
            Log4.Debug("[LoginViewModel] LoginSucceeded 이벤트 발생");

        }, "자동 로그인 실패");

        if (ErrorMessage != null)
        {
            Log4.Debug($"[LoginViewModel] 자동 로그인 에러: {ErrorMessage}");
            HasError = true;
        }
    }

    /// <summary>
    /// 저장된 계정 삭제
    /// </summary>
    [RelayCommand]
    private async Task RemoveSavedAccountAsync()
    {
        Log4.Debug("[LoginViewModel] 저장된 계정 삭제");

        // 기존 ClientId, TenantId 백업
        var savedClientId = SavedSettings?.AzureAd?.ClientId;
        var savedTenantId = SavedSettings?.AzureAd?.TenantId;

        // MSAL 캐시 초기화
        await _graphAuthService.LogoutAsync();

        // 로그인 설정 파일 삭제
        _loginSettingsService.Clear();
        SavedSettings = null;

        // ClientId, TenantId는 TextBox에 유지 (재로그인 편의성)
        if (!string.IsNullOrEmpty(savedClientId))
        {
            ClientId = savedClientId;
            Log4.Debug($"[LoginViewModel] ClientId 유지: {savedClientId.Substring(0, Math.Min(8, savedClientId.Length))}...");
        }
        if (!string.IsNullOrEmpty(savedTenantId))
        {
            TenantId = savedTenantId;
            Log4.Debug($"[LoginViewModel] TenantId 유지: {savedTenantId}");
        }

        Log4.Debug("[LoginViewModel] 계정 삭제 완료 (ClientId/TenantId 유지)");
    }
}
