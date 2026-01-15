using System;
using mailX.Models;
using mailX.Models.Settings;
using mailX.Utils;

namespace mailX.Services.Storage;

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

    public AppSettingsManager()
    {
        _loginService = new XmlSettingsService<LoginSettings>("autologin.xml");
        _aiProvidersService = new XmlSettingsService<AIProvidersSettings>("apikeys.xml");
        _notificationService = new XmlSettingsService<NotificationXmlSettings>("notification.xml");
        _syncService = new XmlSettingsService<SyncSettings>("sync.xml");
        _databaseService = new XmlSettingsService<DatabaseSettings>("database.xml");
        _loggingService = new XmlSettingsService<LoggingSettings>("logging.xml");
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
            Notification = _notificationService.Load();
            Sync = _syncService.Load();
            Database = _databaseService.Load();
            Logging = _loggingService.Load();

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
            _aiProvidersService.Save(AIProviders);
            _notificationService.Save(Notification);
            _syncService.Save(Sync);
            _databaseService.Save(Database);
            _loggingService.Save(Logging);

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
        _aiProvidersService.Save(AIProviders);
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
    /// 로그인 설정 초기화 (파일 삭제)
    /// </summary>
    public void ClearLogin()
    {
        _loginService.Delete();
        Login = new LoginSettings();
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
    }
}
