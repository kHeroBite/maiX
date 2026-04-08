using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using mAIx.Services.Graph;
using mAIx.Utils;

namespace mAIx.Services;

/// <summary>
/// 이메일 내보내기 서비스 — EML/PDF 형식 지원
/// </summary>
public class ExportService
{
    private readonly GraphAuthService _authService;

    public ExportService(GraphAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// EML 형식으로 내보내기 — Graph API $value 엔드포인트 사용
    /// </summary>
    /// <param name="messageId">메일 메시지 ID (Graph EntryId)</param>
    /// <param name="savePath">저장 파일 경로</param>
    public async Task ExportAsEmlAsync(string messageId, string savePath)
    {
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentNullException(nameof(messageId));
        if (string.IsNullOrEmpty(savePath))
            throw new ArgumentNullException(nameof(savePath));

        Log4.Debug($"[ExportService] EML 내보내기 시작: {messageId}");

        using var httpClient = await _authService.GetHttpClientAsync();
        var url = $"https://graph.microsoft.com/v1.0/me/messages/{messageId}/$value";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync();
        await File.WriteAllBytesAsync(savePath, content);

        Log4.Info($"[ExportService] EML 내보내기 완료: {savePath} ({content.Length} bytes)");
    }

    /// <summary>
    /// PDF 형식으로 내보내기 — WebView2 PrintToPdfAsync 활용
    /// 호출자가 WebView2 CoreWebView2를 전달해야 함
    /// </summary>
    /// <param name="coreWebView2">WebView2의 CoreWebView2 인스턴스</param>
    /// <param name="savePath">저장 파일 경로</param>
    public async Task ExportAsPdfAsync(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2, string savePath)
    {
        if (coreWebView2 == null)
            throw new ArgumentNullException(nameof(coreWebView2));
        if (string.IsNullOrEmpty(savePath))
            throw new ArgumentNullException(nameof(savePath));

        Log4.Debug($"[ExportService] PDF 내보내기 시작");

        var printSettings = coreWebView2.Environment.CreatePrintSettings();
        printSettings.ShouldPrintBackgrounds = true;
        printSettings.ShouldPrintHeaderAndFooter = false;

        var success = await coreWebView2.PrintToPdfAsync(savePath, printSettings);

        if (success)
        {
            Log4.Info($"[ExportService] PDF 내보내기 완료: {savePath}");
        }
        else
        {
            Log4.Error($"[ExportService] PDF 내보내기 실패: {savePath}");
            throw new InvalidOperationException("PDF 내보내기에 실패했습니다.");
        }
    }

    /// <summary>
    /// WebView2를 통한 인쇄
    /// </summary>
    /// <param name="coreWebView2">WebView2의 CoreWebView2 인스턴스</param>
    public async Task PrintAsync(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2)
    {
        if (coreWebView2 == null)
            throw new ArgumentNullException(nameof(coreWebView2));

        Log4.Debug("[ExportService] 인쇄 시작");
        await coreWebView2.ExecuteScriptAsync("window.print();");
        Log4.Info("[ExportService] 인쇄 대화상자 표시됨");
    }
}
