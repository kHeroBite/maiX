using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using mAIx.Models.Settings;

namespace mAIx.Services.Notification;

/// <summary>
/// Windows 네이티브 토스트 알림 서비스
/// PowerShell BurntToast / New-BurntToastNotification 없이
/// PowerShell Windows.UI.Notifications WinRT Interop으로 토스트 발송
/// net10.0-windows TFM 호환 (추가 NuGet 패키지 불필요)
/// </summary>
public class ToastNotificationService : IDisposable
{
    private readonly ILogger _logger;
    private readonly NotificationXmlSettings _settings;
    private bool _disposed;

    public ToastNotificationService(NotificationXmlSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = Log.ForContext<ToastNotificationService>();
        _logger.Debug("ToastNotificationService 초기화 완료 (ToastEnabled={ToastEnabled})", settings.ToastEnabled);
    }

    /// <summary>
    /// 새 메일 수신 토스트 알림 표시
    /// </summary>
    public void ShowNewMailNotification(string sender, string subject, string preview)
    {
        if (!IsEnabled()) return;

        var body = string.IsNullOrWhiteSpace(preview)
            ? subject
            : (preview.Length > 80 ? preview[..80] + "..." : preview);

        Task.Run(() => ShowToastViaPowerShell($"새 메일: {sender}", $"{subject}\n{body}"));
        _logger.Debug("새 메일 토스트 알림 표시: sender={Sender}, subject={Subject}", sender, subject);
    }

    /// <summary>
    /// 중요 메일 수신 토스트 알림 표시 (높은 우선순위)
    /// </summary>
    public void ShowImportantMailNotification(string sender, string subject)
    {
        if (!IsEnabled()) return;

        Task.Run(() => ShowToastViaPowerShell($"중요 메일: {sender}", subject));
        _logger.Debug("중요 메일 토스트 알림 표시: sender={Sender}, subject={Subject}", sender, subject);
    }

    private void ShowToastViaPowerShell(string title, string message)
    {
        try
        {
            var safeTitle = title.Replace("'", "''").Replace("\"", "`\"");
            var safeMsg = message.Replace("'", "''").Replace("\"", "`\"").Replace("\n", " ");

            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime] | Out-Null
$xml = [Windows.Data.Xml.Dom.XmlDocument]::new()
$xml.LoadXml('<toast><visual><binding template=""ToastGeneric""><text>{safeTitle}</text><text>{safeMsg}</text></binding></visual></toast>')
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('mAIx').Show($toast)
";
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            proc.Start();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                _logger.Warning("토스트 PowerShell 오류: {Error}", err);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "토스트 알림 PowerShell 실행 실패");
        }
    }

    /// <summary>
    /// 토스트 알림 활성화 여부 확인
    /// </summary>
    private bool IsEnabled()
    {
        if (!_settings.ToastEnabled)
        {
            _logger.Debug("토스트 알림 비활성화 상태 — 알림 건너뜀");
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.Debug("ToastNotificationService Dispose 완료");
    }
}
