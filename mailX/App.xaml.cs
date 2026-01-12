using System;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using mailX.Data;
using mailX.ViewModels;
using mailX.Views;

namespace mailX;

/// <summary>
/// 애플리케이션 진입점 - DI 컨테이너 구성 및 서비스 등록
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

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

    public App()
    {
        // 앱 데이터 폴더 생성
        EnsureDirectoriesExist();

        // Serilog 설정
        ConfigureSerilog();

        // Host 빌더 구성
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
    private static void ConfigureServices(IServiceCollection services)
    {
        // DbContext 등록 (SQLite)
        services.AddDbContext<MailXDbContext>(options =>
        {
            options.UseSqlite($"Data Source={DatabasePath}");
        });

        // ViewModels 등록
        services.AddTransient<MainViewModel>();
        services.AddTransient<LoginViewModel>();

        // Views 등록
        services.AddTransient<MainWindow>();
        services.AddTransient<LoginWindow>();

        // TODO: 서비스 등록 (GraphService, AIService 등)
        // services.AddSingleton<IGraphService, GraphService>();
        // services.AddSingleton<IAIService, AIService>();
    }

    /// <summary>
    /// 애플리케이션 시작 시 호출
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        try
        {
            // 데이터베이스 마이그레이션 실행
            using var scope = _host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MailXDbContext>();

            Log.Information("데이터베이스 마이그레이션 시작");
            await dbContext.Database.MigrateAsync();
            Log.Information("데이터베이스 마이그레이션 완료");

            // 메인 윈도우 표시
            // TODO: 로그인 상태에 따라 LoginWindow 또는 MainWindow 표시
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            Log.Information("메인 윈도우 표시 완료");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "애플리케이션 시작 중 치명적 오류 발생");
            MessageBox.Show(
                $"애플리케이션을 시작할 수 없습니다.\n\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }

        base.OnStartup(e);
    }

    /// <summary>
    /// 애플리케이션 종료 시 호출
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("mailX 애플리케이션 종료");

        await _host.StopAsync();
        _host.Dispose();

        Log.CloseAndFlush();

        base.OnExit(e);
    }
}
