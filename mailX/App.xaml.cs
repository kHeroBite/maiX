using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using mailX.Data;
using mailX.Models.Settings;
using mailX.Services.AI;
using mailX.Services.Analysis;
using mailX.Services.Cloud;
using mailX.Services.Converter;
using mailX.Services.Graph;
using mailX.Services.Notification;
using mailX.Services.Search;
using mailX.Services.Storage;
using mailX.Services.Sync;
using mailX.ViewModels;
using mailX.Services.Api;
using mailX.Utils;
using mailX.Views;

namespace mailX;

/// <summary>
/// 애플리케이션 진입점 - DI 컨테이너 구성 및 서비스 등록
/// XML 설정 파일 기반 구성 사용
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private RestApiServer? _restApiServer;

    /// <summary>
    /// 애플리케이션 데이터 폴더 경로
    /// </summary>
    public static string AppDataPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mailX");

    /// <summary>
    /// 로그 폴더 경로
    /// </summary>
    public static string LogPath { get; } = Path.Combine(AppDataPath, "logs");

    /// <summary>
    /// SQLite 데이터베이스 경로
    /// </summary>
    public static string DatabasePath { get; } = Path.Combine(AppDataPath, "mailX.db");

    /// <summary>
    /// 통합 설정 매니저 (XML 파일 기반)
    /// </summary>
    public static AppSettingsManager Settings { get; private set; } = new();

    public App()
    {
        // 앱 데이터 폴더 생성
        EnsureDirectoriesExist();

        // XML 설정 파일 로드
        Settings.LoadAll();
        Settings.EnsureDefaultSettings();

        // Log4 초기화 (MARS 스타일 로깅)
        Log4.Initialize();

        // Serilog 설정
        ConfigureSerilog();

        // Host 빌더 구성 (IConfiguration 없이)
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
            })
            .Build();
    }

    /// <summary>
    /// 필수 디렉토리 생성
    /// </summary>
    private static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(LogPath);
        Directory.CreateDirectory(Path.Combine(AppDataPath, "conf"));
    }

    /// <summary>
    /// Serilog 로깅 구성
    /// </summary>
    private static void ConfigureSerilog()
    {
        var logLevel = Settings.Logging.MinimumLevel.ToLower() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Debug
        };

        var logPath = Settings.Logging.LogPath ?? LogPath;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.File(
                path: Path.Combine(logPath, "mailX-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Settings.Logging.RetainedFileCountLimit,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("mailX 애플리케이션 시작");
    }

    /// <summary>
    /// 서비스 등록 (DI 컨테이너 구성)
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // XML 설정 매니저 등록
        services.AddSingleton(Settings);
        services.AddSingleton(Settings.AIProviders);
        services.AddSingleton(Settings.Notification);
        services.AddSingleton(Settings.Sync);
        services.AddSingleton(Settings.Database);
        services.AddSingleton(Settings.Logging);

        // DbContext 등록 (SQLite)
        services.AddDbContext<MailXDbContext>(options =>
        {
            options.UseSqlite($"Data Source={DatabasePath}");
        });

        // Graph 서비스 등록
        services.AddSingleton<GraphAuthService>();
        services.AddScoped<GraphMailService>();

        // AI Provider 등록 (Singleton - 상태 유지)
        services.AddSingleton<ClaudeProvider>();
        services.AddSingleton<OpenAIProvider>();
        services.AddSingleton<GeminiProvider>();
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<LMStudioProvider>();

        // AI 서비스 등록 (Singleton - Provider 관리)
        services.AddSingleton<AIService>(sp =>
        {
            var aiService = new AIService();
            var aiConfig = Settings.AIProviders;

            // Claude 설정
            if (!string.IsNullOrEmpty(aiConfig.Claude.ApiKey))
            {
                aiService.ConfigureProvider("Claude", aiConfig.Claude.ApiKey, null, aiConfig.Claude.Model);
            }

            // OpenAI 설정
            if (!string.IsNullOrEmpty(aiConfig.OpenAI.ApiKey))
            {
                aiService.ConfigureProvider("OpenAI", aiConfig.OpenAI.ApiKey, null, aiConfig.OpenAI.Model);
            }

            // Gemini 설정
            if (!string.IsNullOrEmpty(aiConfig.Gemini.ApiKey))
            {
                aiService.ConfigureProvider("Gemini", aiConfig.Gemini.ApiKey, null, aiConfig.Gemini.Model);
            }

            // Ollama 설정 (로컬 - API 키 불필요)
            if (!string.IsNullOrEmpty(aiConfig.Ollama.BaseUrl))
            {
                aiService.ConfigureProvider("Ollama", "", aiConfig.Ollama.BaseUrl, aiConfig.Ollama.Model);
            }

            // LMStudio 설정 (로컬 - API 키 불필요)
            if (!string.IsNullOrEmpty(aiConfig.LMStudio.BaseUrl))
            {
                aiService.ConfigureProvider("LMStudio", "", aiConfig.LMStudio.BaseUrl, aiConfig.LMStudio.Model);
            }

            // 기본 Provider 설정
            aiService.SetCurrentProvider(aiConfig.DefaultProvider);

            return aiService;
        });

        // Prompt 서비스 등록
        services.AddScoped<PromptService>();

        // 분석 서비스 등록
        services.AddSingleton<PriorityCalculator>();
        services.AddSingleton<ContractExtractor>();
        services.AddSingleton<TodoExtractor>();
        services.AddScoped<EmailAnalyzer>();

        // 문서 변환 서비스 등록
        services.AddSingleton<HwpConverter>();
        services.AddSingleton<PandocConverter>();
        services.AddSingleton<OcrConverter>();
        services.AddSingleton<AttachmentProcessor>();
        services.AddSingleton<CloudLinkDownloader>();

        // 검색 서비스 등록
        services.AddScoped<EmailSearchService>();

        // 알림 서비스 등록
        services.AddSingleton(sp =>
        {
            var notifyConfig = Settings.Notification;
            return new NotificationSettings
            {
                NtfyServerUrl = notifyConfig.NtfyServerUrl,
                NtfyTopic = notifyConfig.NtfyTopic,
                NtfyAuthToken = notifyConfig.NtfyAuthToken,
                EnableNewMailNotification = notifyConfig.EnableNewMailNotification,
                EnableImportantMailNotification = notifyConfig.EnableImportantMailNotification,
                EnableDeadlineReminder = notifyConfig.EnableDeadlineReminder,
                MinPriorityForNotification = notifyConfig.MinPriorityForNotification,
                DeadlineReminderDays = notifyConfig.DeadlineReminderDays,
                QuietHoursStart = TimeSpan.TryParse(notifyConfig.QuietHoursStart, out var start) ? start : new TimeSpan(22, 0, 0),
                QuietHoursEnd = TimeSpan.TryParse(notifyConfig.QuietHoursEnd, out var end) ? end : new TimeSpan(7, 0, 0),
                EnableQuietHours = notifyConfig.EnableQuietHours,
                BatchIntervalMinutes = notifyConfig.BatchIntervalMinutes,
                MaxBatchSize = notifyConfig.MaxBatchSize,
                ExcludeNonBusinessMail = notifyConfig.ExcludeNonBusinessMail
            };
        });
        services.AddSingleton<NotificationService>();

        // 백그라운드 동기화 서비스 등록 (IHostedService)
        services.AddHostedService<BackgroundSyncService>();
        services.AddSingleton<BackgroundSyncService>(sp =>
            sp.GetServices<IHostedService>().OfType<BackgroundSyncService>().First());

        // ViewModels 등록
        services.AddTransient<MainViewModel>();
        services.AddTransient<LoginViewModel>();

        // Views 등록
        services.AddTransient<MainWindow>();
        services.AddTransient<LoginWindow>();
    }

    /// <summary>
    /// 애플리케이션 시작 시 호출
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        Log4.Debug("OnStartup 시작");
        await _host.StartAsync();
        Log4.Debug("Host 시작 완료");

        try
        {
            // 데이터베이스 마이그레이션 실행
            Log4.Debug("DB 마이그레이션 시작");
            using var scope = _host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MailXDbContext>();

            Log.Information("데이터베이스 마이그레이션 시작");
            await dbContext.Database.MigrateAsync();
            Log.Information("데이터베이스 마이그레이션 완료");
            Log4.Debug("DB 마이그레이션 완료");

            // 로그인 창 먼저 표시
            Log4.Debug("LoginWindow 생성 시작");
            var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            Log4.Debug("LoginWindow 생성 완료");

            Log4.Debug("LoginWindow.ShowDialog() 호출 직전");
            var loginResult = loginWindow.ShowDialog();
            Log4.Debug($"LoginWindow.ShowDialog() 완료 - result: {loginResult}");

            if (loginResult == true)
            {
                // 로그인 직후 폴더 먼저 동기화
                Log4.Debug("로그인 후 폴더 동기화 시작");
                var syncService = _host.Services.GetRequiredService<BackgroundSyncService>();
                await syncService.SyncFoldersAsync();
                Log4.Debug("로그인 후 폴더 동기화 완료");

                // 로그인 성공 시 메인 윈도우 표시
                Log4.Debug("MainWindow 생성 시작");
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                Log4.Debug("MainWindow 생성 완료");

                Log4.Debug("MainWindow.Show() 호출 직전");
                mainWindow.Show();
                Log4.Debug("MainWindow.Show() 완료");
                Log.Information("메인 윈도우 표시 완료");

                // REST API 서버 시작
                try
                {
                    Log4.Debug("REST API 서버 시작");
                    _restApiServer = new RestApiServer(5858);
                    _restApiServer.MainWindow = mainWindow;
                    _restApiServer.Start();
                }
                catch (Exception apiEx)
                {
                    Log4.Error($"[RestAPI] 서버 시작 실패: {apiEx.Message}");
                }
            }
            else
            {
                // 로그인 취소/실패 시 앱 종료
                Log4.Debug("로그인 취소/실패 - 앱 종료");
                Log.Information("로그인 취소 - 앱 종료");
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            Log4.Fatal(ex);
            Log.Fatal(ex, "애플리케이션 시작 중 치명적 오류 발생");
            MessageBox.Show(
                $"애플리케이션을 시작할 수 없습니다.\n\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }

        Log4.Debug("OnStartup 완료");
        base.OnStartup(e);
    }

    /// <summary>
    /// 애플리케이션 종료 시 호출
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        Log4.Info("mailX 애플리케이션 종료");
        Log.Information("mailX 애플리케이션 종료");

        // REST API 서버 종료
        _restApiServer?.Stop();

        await _host.StopAsync();
        _host.Dispose();

        Log.CloseAndFlush();

        base.OnExit(e);
    }
}
