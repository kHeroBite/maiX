using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mailX.Models;
using mailX.Services.Graph;
using mailX.Services.Sync;
using mailX.Utils;

namespace mailX.ViewModels;

/// <summary>
/// 메일 작성 모드
/// </summary>
public enum ComposeMode
{
    New,
    Reply,
    ReplyAll,
    Forward
}

/// <summary>
/// 첨부파일 모델
/// </summary>
public partial class AttachmentItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private long _fileSize;
}

/// <summary>
/// 메일 작성 ViewModel
/// </summary>
public partial class ComposeViewModel : ViewModelBase
{
    private readonly GraphMailService _graphMailService;
    private readonly BackgroundSyncService? _syncService;
    private readonly ComposeMode _mode;
    private readonly Models.Email? _originalEmail;

    /// <summary>
    /// 받는 사람
    /// </summary>
    [ObservableProperty]
    private string _to = "";

    /// <summary>
    /// 참조
    /// </summary>
    [ObservableProperty]
    private string _cc = "";

    /// <summary>
    /// 숨은 참조
    /// </summary>
    [ObservableProperty]
    private string _bcc = "";

    /// <summary>
    /// 제목
    /// </summary>
    [ObservableProperty]
    private string _subject = "";

    /// <summary>
    /// 본문 (HTML)
    /// </summary>
    [ObservableProperty]
    private string _body = "";

    /// <summary>
    /// 초기 본문 (답장/전달 시)
    /// </summary>
    public string InitialBody { get; private set; } = "";

    /// <summary>
    /// 첨부파일 목록
    /// </summary>
    public ObservableCollection<AttachmentItem> Attachments { get; } = new();

    /// <summary>
    /// 창 제목
    /// </summary>
    public string WindowTitle => _mode switch
    {
        ComposeMode.New => "새 메일",
        ComposeMode.Reply => "답장",
        ComposeMode.ReplyAll => "전체 답장",
        ComposeMode.Forward => "전달",
        _ => "메일 작성"
    };

    /// <summary>
    /// 생성자
    /// </summary>
    public ComposeViewModel(
        GraphMailService graphMailService, 
        BackgroundSyncService? syncService = null,
        ComposeMode mode = ComposeMode.New, 
        Models.Email? originalEmail = null)
    {
        _graphMailService = graphMailService;
        _syncService = syncService;
        _mode = mode;
        _originalEmail = originalEmail;

        InitializeFromOriginalEmail();
    }

    /// <summary>
    /// 원본 메일에서 초기화 (답장/전달)
    /// </summary>
    private void InitializeFromOriginalEmail()
    {
        if (_originalEmail == null) return;

        switch (_mode)
        {
            case ComposeMode.Reply:
                To = _originalEmail.From ?? "";
                Subject = $"RE: {RemoveRePrefix(_originalEmail.Subject ?? "")}";
                InitialBody = BuildReplyBody(_originalEmail);
                break;

            case ComposeMode.ReplyAll:
                To = _originalEmail.From ?? "";
                // 원본 수신자들을 CC에 추가 (자신 제외)
                if (!string.IsNullOrEmpty(_originalEmail.To))
                {
                    var toList = _originalEmail.To.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(e => e.Trim())
                        .Where(e => !e.Equals(To, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    Cc = string.Join("; ", toList);
                }
                Subject = $"RE: {RemoveRePrefix(_originalEmail.Subject ?? "")}";
                InitialBody = BuildReplyBody(_originalEmail);
                break;

            case ComposeMode.Forward:
                Subject = $"FW: {RemoveFwPrefix(_originalEmail.Subject ?? "")}";
                InitialBody = BuildForwardBody(_originalEmail);
                break;
        }
    }

    /// <summary>
    /// RE: 접두사 제거
    /// </summary>
    private static string RemoveRePrefix(string subject)
    {
        while (subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) ||
               subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
        {
            subject = subject[3..].TrimStart();
        }
        return subject;
    }

    /// <summary>
    /// FW: 접두사 제거
    /// </summary>
    private static string RemoveFwPrefix(string subject)
    {
        while (subject.StartsWith("FW:", StringComparison.OrdinalIgnoreCase) ||
               subject.StartsWith("Fw:", StringComparison.OrdinalIgnoreCase))
        {
            subject = subject[3..].TrimStart();
        }
        return subject;
    }

    /// <summary>
    /// 답장 본문 생성
    /// </summary>
    private static string BuildReplyBody(Models.Email original)
    {
        var dateStr = original.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "";
        return $@"<br><br>
<div style='border-left: 2px solid #0078d4; padding-left: 12px; margin-left: 8px; color: #666;'>
<p><b>보낸 사람:</b> {System.Net.WebUtility.HtmlEncode(original.From ?? "")}<br>
<b>보낸 날짜:</b> {dateStr}<br>
<b>받는 사람:</b> {System.Net.WebUtility.HtmlEncode(original.To ?? "")}<br>
<b>제목:</b> {System.Net.WebUtility.HtmlEncode(original.Subject ?? "")}</p>
{original.Body}
</div>";
    }

    /// <summary>
    /// 전달 본문 생성
    /// </summary>
    private static string BuildForwardBody(Models.Email original)
    {
        var dateStr = original.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "";
        return $@"<br><br>
<div style='border-top: 1px solid #ccc; padding-top: 12px;'>
<p style='color: #666;'>---------- 전달된 메일 ----------<br>
<b>보낸 사람:</b> {System.Net.WebUtility.HtmlEncode(original.From ?? "")}<br>
<b>보낸 날짜:</b> {dateStr}<br>
<b>받는 사람:</b> {System.Net.WebUtility.HtmlEncode(original.To ?? "")}<br>
<b>제목:</b> {System.Net.WebUtility.HtmlEncode(original.Subject ?? "")}</p>
{original.Body}
</div>";
    }

    /// <summary>
    /// 첨부파일 추가
    /// </summary>
    public void AddAttachment(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;

        var fileInfo = new System.IO.FileInfo(filePath);
        Attachments.Add(new AttachmentItem
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length
        });
    }

    /// <summary>
    /// 첨부파일 제거
    /// </summary>
    [RelayCommand]
    private void RemoveAttachment(AttachmentItem? attachment)
    {
        if (attachment != null)
        {
            Attachments.Remove(attachment);
        }
    }

    /// <summary>
    /// 메일 발송
    /// </summary>
    public async Task<bool> SendMailAsync()
    {
        try
        {
            // 받는 사람 파싱
            var toRecipients = ParseEmailAddresses(To);
            if (toRecipients.Count == 0)
            {
                throw new InvalidOperationException("받는 사람이 없습니다.");
            }

            // 참조 파싱
            var ccRecipients = ParseEmailAddresses(Cc);

            // 숨은 참조 파싱
            var bccRecipients = ParseEmailAddresses(Bcc);

            // Message 객체 생성
            var message = new Message
            {
                Subject = Subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = Body
                },
                ToRecipients = toRecipients,
                CcRecipients = ccRecipients,
                BccRecipients = bccRecipients
            };

            // 첨부파일 추가
            if (Attachments.Count > 0)
            {
                message.Attachments = new List<Microsoft.Graph.Models.Attachment>();
                foreach (var attachment in Attachments)
                {
                    var bytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);
                    message.Attachments.Add(new FileAttachment
                    {
                        Name = attachment.FileName,
                        ContentBytes = bytes,
                        ContentType = GetMimeType(attachment.FileName)
                    });
                }
            }

            // Graph API로 발송
            await _graphMailService.SendMessageAsync(message);

            Log4.Info($"메일 발송 성공: To={To}, Subject={Subject}");

            // 보낸편지함 즉시 동기화 (비동기로 백그라운드 실행)
            if (_syncService != null && _originalEmail != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000); // 서버 반영 대기
                        await _syncService.SyncSentItemsAsync(_originalEmail.AccountEmail);
                    }
                    catch (Exception ex)
                    {
                        Log4.Warn($"보낸편지함 동기화 실패: {ex.Message}");
                    }
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 발송 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 이메일 주소 파싱
    /// </summary>
    private static List<Recipient> ParseEmailAddresses(string input)
    {
        var result = new List<Recipient>();
        if (string.IsNullOrWhiteSpace(input)) return result;

        var addresses = input.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var addr in addresses)
        {
            var trimmed = addr.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string? name = null;
            string? email = null;

            // "이름 <주소>" 형식 파싱
            var bracketStart = trimmed.IndexOf('<');
            var bracketEnd = trimmed.IndexOf('>');

            if (bracketStart > 0 && bracketEnd > bracketStart)
            {
                // "이름 <주소>" 형식
                name = trimmed.Substring(0, bracketStart).Trim();
                email = trimmed.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
            }
            else if (trimmed.Contains('@'))
            {
                // 이메일 주소만 있는 경우
                email = trimmed;
            }
            else
            {
                // 이메일 형식이 아닌 경우 (이름만 있는 경우) - 스킵
                Log4.Warn($"유효하지 않은 이메일 주소 형식: {trimmed}");
                continue;
            }

            if (!string.IsNullOrEmpty(email))
            {
                result.Add(new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = email,
                        Name = name
                    }
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 파일 확장자에서 MIME 타입 가져오기
    /// </summary>
    private static string GetMimeType(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".html" => "text/html",
            _ => "application/octet-stream"
        };
    }
}
