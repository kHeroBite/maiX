using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using mailX.Data;
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
    /// 설정 파일 경로
    /// </summary>
    public static string SettingsPath { get; } = Path.Combine(AppDataPath, "appsettings.json");

    public App()
    {
        // 앱 데이터 폴더 생성
        EnsureDirectoriesExist();

        // 기본 설정 파일 생성
        EnsureSettingsExist();

        // Log4 초기화 (MARS 스타일 로깅)
        Log4.Initialize();

        // Serilog 설정
        ConfigureSerilog();

        // Host 빌더 구성
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile(SettingsPath, optional: true, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services, context.Configuration);
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
    }

    /// <summary>
    /// 기본 설정 파일 생성
    /// </summary>
    private static void EnsureSettingsExist()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaultSettings = new
            {
                ConnectionStrings = new
                {
                    DefaultConnection = $"Data Source={DatabasePath}"
                },
                AzureAd = new
                {
                    ClientId = "YOUR_CLIENT_ID_HERE",
                    TenantId = "common",
                    Scopes = new[] { "User.Read", "Mail.Read", "Mail.Send", "Mail.ReadWrite", "Files.Read.All", "Sites.Read.All" }
                },
                AIProviders = new
                {
                    DefaultProvider = "Claude",
                    Claude = new
                    {
                        ApiKey = "",
                        Model = "claude-sonnet-4-20250514",
                        BaseUrl = "https://api.anthropic.com"
                    },
                    OpenAI = new
                    {
                        ApiKey = "",
                        Model = "gpt-4o",
                        BaseUrl = "https://api.openai.com/v1"
                    },
                    Gemini = new
                    {
                        ApiKey = "",
                        Model = "gemini-2.0-flash-exp",
                        BaseUrl = "https://generativelanguage.googleapis.com/v1beta"
                    },
                    Ollama = new
                    {
                        Model = "llama3.3",
                        BaseUrl = "http://localhost:11434"
                    },
                    LMStudio = new
                    {
                        Model = "local-model",
                        BaseUrl = "http://localhost:1234/v1"
                    }
                },
                Notification = new
                {
                    NtfyServerUrl = "https://ntfy.sh",
                    NtfyTopic = "mailX",
                    NtfyAuthToken = "",
                    EnableNewMailNotification = true,
                    EnableImportantMailNotification = true,
                    EnableDeadlineReminder = true,
                    MinPriorityForNotification = 70,
                    DeadlineReminderDays = 3,
                    QuietHoursStart = "22:00",
                    QuietHoursEnd = "07:00",
                    EnableQuietHours = true,
                    BatchIntervalMinutes = 5,
                    MaxBatchSize = 10,
                    ExcludeNonBusinessMail = true
                },
                Sync = new
                {
                    IntervalMinutes = 5,
                    MaxMessagesPerSync = 100
                }
            };

            var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
    }

    /// <summary>
    /// Serilog 로깅 구성
    /// </summary>
    private static void ConfigureSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(LogPath, "mailX-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("mailX 애플리케이션 시작");
    }

    /// <summary>
    /// 서비스 등록 (DI 컨테이너 구성)
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 설정 바인딩
        services.AddSingleton(configuration);

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

            // 설정에서 API 키 로드 및 구성
            var aiConfig = configuration.GetSection("AIProviders");
            var defaultProvider = aiConfig.GetValue<string>("DefaultProvider") ?? "Claude";

            // Claude 설정
            var claudeKey = aiConfig.GetValue<string>("Claude:ApiKey");
            var claudeModel = aiConfig.GetValue<string>("Claude:Model");
            if (!string.IsNullOrEmpty(claudeKey))
            {
                aiService.ConfigureProvider("Claude", claudeKey, null, claudeModel);
            }

            // OpenAI 설정
            var openAiKey = aiConfig.GetValue<string>("OpenAI:ApiKey");
            var openAiModel = aiConfig.GetValue<string>("OpenAI:Model");
            if (!string.IsNullOrEmpty(openAiKey))
            {
                aiService.ConfigureProvider("OpenAI", openAiKey, null, openAiModel);
            }

            // Gemini 설정
            var geminiKey = aiConfig.GetValue<string>("Gemini:ApiKey");
            var geminiModel = aiConfig.GetValue<string>("Gemini:Model");
            if (!string.IsNullOrEmpty(geminiKey))
            {
                aiService.ConfigureProvider("Gemini", geminiKey, null, geminiModel);
            }

            // Ollama 설정 (로컬 - API 키 불필요)
            var ollamaUrl = aiConfig.GetValue<string>("Ollama:BaseUrl");
            var ollamaModel = aiConfig.GetValue<string>("Ollama:Model");
            if (!string.IsNullOrEmpty(ollamaUrl))
            {
                aiService.ConfigureProvider("Ollama", "", ollamaUrl, ollamaModel);
            }

            // LMStudio 설정 (로컬 - API 키 불필요)
            var lmStudioUrl = aiConfig.GetValue<string>("LMStudio:BaseUrl");
            var lmStudioModel = aiConfig.GetValue<string>("LMStudio:Model");
            if (!string.IsNullOrEmpty(lmStudioUrl))
            {
                aiService.ConfigureProvider("LMStudio", "", lmStudioUrl, lmStudioModel);
            }

            // 기본 Provider 설정
            aiService.SetCurrentProvider(defaultProvider);

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
            var notifyConfig = configuration.GetSection("Notification");
            return new NotificationSettings
            {
                NtfyServerUrl = notifyConfig.GetValue<string>("NtfyServerUrl") ?? "https://ntfy.sh",
                NtfyTopic = notifyConfig.GetValue<string>("NtfyTopic") ?? "mailX",
                NtfyAuthToken = notifyConfig.GetValue<string>("NtfyAuthToken"),
                EnableNewMailNotification = notifyConfig.GetValue<bool>("EnableNewMailNotification", true),
                EnableImportantMailNotification = notifyConfig.GetValue<bool>("EnableImportantMailNotification", true),
                EnableDeadlineReminder = notifyConfig.GetValue<bool>("EnableDeadlineReminder", true),
                MinPriorityForNotification = notifyConfig.GetValue<int>("MinPriorityForNotification", 70),
                DeadlineReminderDays = notifyConfig.GetValue<int>("DeadlineReminderDays", 3),
                QuietHoursStart = TimeSpan.TryParse(notifyConfig.GetValue<string>("QuietHoursStart"), out var start) ? start : new TimeSpan(22, 0, 0),
                QuietHoursEnd = TimeSpan.TryParse(notifyConfig.GetValue<string>("QuietHoursEnd"), out var end) ? end : new TimeSpan(7, 0, 0),
                EnableQuietHours = notifyConfig.GetValue<bool>("EnableQuietHours", true),
                BatchIntervalMinutes = notifyConfig.GetValue<int>("BatchIntervalMinutes", 5),
                MaxBatchSize = notifyConfig.GetValue<int>("MaxBatchSize", 10),
                ExcludeNonBusinessMail = notifyConfig.GetValue<bool>("ExcludeNonBusinessMail", true)
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
