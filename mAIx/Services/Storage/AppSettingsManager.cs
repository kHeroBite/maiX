using System;
using System.Security.Cryptography;
using System.Text;
using mAIx.Models;
using mAIx.Models.Settings;
using mAIx.Utils;

namespace mAIx.Services.Storage;

/// <summary>
/// 모든 XML 설정 파일을 관리하는 통합 매니저
/// </summary>
public class AppSettingsManager
{
    // 설정 서비스 인스턴스
    private readonly XmlSettingsService<LoginSettings> _loginService;
    private readonly XmlSettingsService<AIProvidersSettings> _aiProvidersService;
    private readonly XmlSettingsService<NotificationXmlSettings> _notificationService;
    private readonly XmlSettingsService<SyncSettings> _syncService;
    private readonly XmlSettingsService<DatabaseSettings> _databaseService;
    private readonly XmlSettingsService<LoggingSettings> _loggingService;
    private readonly XmlSettingsService<UserPreferencesSettings> _userPreferencesService;
    private readonly XmlSettingsService<SignatureSettings> _signatureService;

    /// <summary>
    /// 로그인 + Azure AD 설정
    /// </summary>
    public LoginSettings Login { get; private set; } = new();

    /// <summary>
    /// AI Provider 설정 (API 키, 모델)
    /// </summary>
    public AIProvidersSettings AIProviders { get; private set; } = new();

    /// <summary>
    /// 알림 설정 (ntfy)
    /// </summary>
    public NotificationXmlSettings Notification { get; private set; } = new();

    /// <summary>
    /// 동기화 설정
    /// </summary>
    public SyncSettings Sync { get; private set; } = new();

    /// <summary>
    /// 데이터베이스 설정
    /// </summary>
    public DatabaseSettings Database { get; private set; } = new();

    /// <summary>
    /// 로깅 설정
    /// </summary>
    public LoggingSettings Logging { get; private set; } = new();

    /// <summary>
    /// 사용자 환경 설정 (테마 등)
    /// </summary>
    public UserPreferencesSettings UserPreferences { get; private set; } = new();

    /// <summary>
    /// 이메일 서명 설정
    /// </summary>
    public SignatureSettings Signature { get; private set; } = new();

    public AppSettingsManager()
    {
        _loginService = new XmlSettingsService<LoginSettings>("autologin.xml");
        _aiProvidersService = new XmlSettingsService<AIProvidersSettings>("apikeys.xml");
        _notificationService = new XmlSettingsService<NotificationXmlSettings>("notification.xml");
        _syncService = new XmlSettingsService<SyncSettings>("sync.xml");
        _databaseService = new XmlSettingsService<DatabaseSettings>("database.xml");
        _loggingService = new XmlSettingsService<LoggingSettings>("logging.xml");
        _userPreferencesService = new XmlSettingsService<UserPreferencesSettings>("preferences.xml");
        _signatureService = new XmlSettingsService<SignatureSettings>("signature.xml");
    }

    /// <summary>
    /// 모든 설정 파일 로드
    /// </summary>
    public void LoadAll()
    {
        try
        {
            Log4.Debug("[AppSettingsManager] 모든 설정 로드 시작");

            Login = _loginService.Load();
            AIProviders = _aiProvidersService.Load();
            DecryptAllApiKeys(AIProviders);
            Notification = _notificationService.Load();
            Sync = _syncService.Load();
            Database = _databaseService.Load();
            Logging = _loggingService.Load();
            UserPreferences = _userPreferencesService.Load();
            Signature = _signatureService.Load();

            Log4.Info("[AppSettingsManager] 모든 설정 로드 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"[AppSettingsManager] 설정 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 모든 설정 파일 저장
    /// </summary>
    public void SaveAll()
    {
        try
        {
            Log4.Debug("[AppSettingsManager] 모든 설정 저장 시작");

            _loginService.Save(Login);
            _aiProvidersService.Save(CreateEncryptedCopy(AIProviders));
            _notificationService.Save(Notification);
            _syncService.Save(Sync);
            _databaseService.Save(Database);
            _loggingService.Save(Logging);
            _userPreferencesService.Save(UserPreferences);
            _signatureService.Save(Signature);

            Log4.Info("[AppSettingsManager] 모든 설정 저장 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"[AppSettingsManager] 설정 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 로그인 설정만 저장
    /// </summary>
    public void SaveLogin()
    {
        _loginService.Save(Login);
    }

    /// <summary>
    /// 로그인 설정 업데이트 및 저장
    /// </summary>
    public void UpdateLogin(LoginSettings settings)
    {
        Login = settings;
        _loginService.Save(Login);
    }

    /// <summary>
    /// AI Provider 설정만 저장
    /// </summary>
    public void SaveAIProviders()
    {
        _aiProvidersService.Save(CreateEncryptedCopy(AIProviders));
    }

    /// <summary>
    /// 알림 설정만 저장
    /// </summary>
    public void SaveNotification()
    {
        _notificationService.Save(Notification);
    }

    /// <summary>
    /// 동기화 설정만 저장
    /// </summary>
    public void SaveSync()
    {
        _syncService.Save(Sync);
    }

    /// <summary>
    /// 사용자 환경 설정만 저장
    /// </summary>
    public void SaveUserPreferences()
    {
        _userPreferencesService.Save(UserPreferences);
    }

    /// <summary>
    /// 서명 설정만 저장
    /// </summary>
    public void SaveSignature()
    {
        _signatureService.Save(Signature);
    }

    /// <summary>
    /// 로그인 설정 초기화 (파일 삭제)
    /// </summary>
    public void ClearLogin()
    {
        _loginService.Delete();
        Login = new LoginSettings();
    }

    /// <summary>
    /// API 키를 DPAPI로 암호화 (CurrentUser 범위)
    /// </summary>
    private static string EncryptApiKey(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// DPAPI로 암호화된 API 키 복호화. 기존 평문 키는 그대로 반환 (하위 호환)
    /// </summary>
    private static string DecryptApiKey(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return encryptedText;
        try
        {
            var bytes = Convert.FromBase64String(encryptedText);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // 기존 평문 키 하위 호환: 복호화 실패 시 원본 반환 (다음 저장 시 암호화됨)
            return encryptedText;
        }
    }

    /// <summary>
    /// AIProvidersSettings 내 모든 API 키를 복호화
    /// </summary>
    private static void DecryptAllApiKeys(AIProvidersSettings settings)
    {
        if (settings.Claude != null) settings.Claude.ApiKey = DecryptApiKey(settings.Claude.ApiKey);
        if (settings.OpenAI != null) settings.OpenAI.ApiKey = DecryptApiKey(settings.OpenAI.ApiKey);
        if (settings.Gemini != null) settings.Gemini.ApiKey = DecryptApiKey(settings.Gemini.ApiKey);
        if (settings.Ollama != null) settings.Ollama.ApiKey = DecryptApiKey(settings.Ollama.ApiKey);
        if (settings.LMStudio != null) settings.LMStudio.ApiKey = DecryptApiKey(settings.LMStudio.ApiKey);
        if (settings.TinyMCE != null) settings.TinyMCE.ApiKey = DecryptApiKey(settings.TinyMCE.ApiKey);
    }

    /// <summary>
    /// AIProvidersSettings 내 모든 API 키를 암호화한 복사본 반환 (원본 유지)
    /// </summary>
    private static AIProvidersSettings CreateEncryptedCopy(AIProvidersSettings settings)
    {
        return new AIProvidersSettings
        {
            DefaultProvider = settings.DefaultProvider,
            Claude = CloneWithEncryptedKey(settings.Claude),
            OpenAI = CloneWithEncryptedKey(settings.OpenAI),
            Gemini = CloneWithEncryptedKey(settings.Gemini),
            Ollama = CloneWithEncryptedKey(settings.Ollama),
            LMStudio = CloneWithEncryptedKey(settings.LMStudio),
            TinyMCE = CloneWithEncryptedTinyMCE(settings.TinyMCE)
        };
    }

    private static AIProviderConfig CloneWithEncryptedKey(AIProviderConfig config)
    {
        if (config == null) return null;
        return new AIProviderConfig
        {
            ApiKey = EncryptApiKey(config.ApiKey),
            Model = config.Model,
            BaseUrl = config.BaseUrl
        };
    }

    private static TinyMCEConfig CloneWithEncryptedTinyMCE(TinyMCEConfig config)
    {
        if (config == null) return null;
        return new TinyMCEConfig
        {
            ApiKey = EncryptApiKey(config.ApiKey)
        };
    }

    /// <summary>
    /// 기본 설정 파일 생성 (파일이 없는 경우에만)
    /// </summary>
    public void EnsureDefaultSettings()
    {
        // 각 설정 파일이 없으면 기본값으로 생성
        if (!_aiProvidersService.Exists)
        {
            _aiProvidersService.Save(new AIProvidersSettings());
            Log4.Debug("[AppSettingsManager] 기본 AI Provider 설정 생성");
        }

        if (!_notificationService.Exists)
        {
            _notificationService.Save(new NotificationXmlSettings());
            Log4.Debug("[AppSettingsManager] 기본 알림 설정 생성");
        }

        if (!_syncService.Exists)
        {
            _syncService.Save(new SyncSettings());
            Log4.Debug("[AppSettingsManager] 기본 동기화 설정 생성");
        }

        if (!_databaseService.Exists)
        {
            _databaseService.Save(new DatabaseSettings());
            Log4.Debug("[AppSettingsManager] 기본 데이터베이스 설정 생성");
        }

        if (!_loggingService.Exists)
        {
            _loggingService.Save(new LoggingSettings());
            Log4.Debug("[AppSettingsManager] 기본 로깅 설정 생성");
        }

        if (!_userPreferencesService.Exists)
        {
            _userPreferencesService.Save(new UserPreferencesSettings());
            Log4.Debug("[AppSettingsManager] 기본 사용자 환경 설정 생성");
        }

        if (!_signatureService.Exists)
        {
            _signatureService.Save(new SignatureSettings());
            Log4.Debug("[AppSettingsManager] 기본 서명 설정 생성");
        }
    }
}
