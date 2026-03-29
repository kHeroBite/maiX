using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mAIx.Models;
using mAIx.Services.Graph;
using mAIx.Services.Sync;
using mAIx.Utils;

namespace mAIx.ViewModels;

/// <summary>
/// 메일 작성 모드
/// </summary>
public enum ComposeMode
{
    New,
    Reply,
    ReplyAll,
    Forward,
    EditDraft  // 임시보관함 메일 편집
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
    /// 임시보관함 메일 ID (EditDraft 모드에서 사용)
    /// </summary>
    private readonly string? _draftMessageId;

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
    /// 중요도 (high, normal, low)
    /// </summary>
    [ObservableProperty]
    private string _importance = "normal";

    /// <summary>
    /// 예약 발송 활성화 여부
    /// </summary>
    [ObservableProperty]
    private bool _isScheduledSend = false;

    /// <summary>
    /// 예약 발송 시간
    /// </summary>
    [ObservableProperty]
    private DateTime _scheduledSendTime = DateTime.Now.AddHours(1);

    /// <summary>
    /// 발송 처리 중 (카운트다운 포함)
    /// </summary>
    [ObservableProperty]
    private bool _isSending = false;

    /// <summary>
    /// 발송 취소용 CancellationTokenSource (View에서 접근 가능)
    /// </summary>
    public CancellationTokenSource? SendCts
    {
        get => _sendCts;
        set => _sendCts = value;
    }
    private CancellationTokenSource? _sendCts;

    /// <summary>
    /// 첨부파일 유무
    /// </summary>
    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>
    /// 창 제목
    /// </summary>
    public string WindowTitle => _mode switch
    {
        ComposeMode.New => "새 메일",
        ComposeMode.Reply => "답장",
        ComposeMode.ReplyAll => "전체 답장",
        ComposeMode.Forward => "전달",
        ComposeMode.EditDraft => "임시 보관함",
        _ => "메일 작성"
    };

    /// <summary>
    /// 편집 중인 임시보관함 메일 여부
    /// </summary>
    public bool IsEditingDraft => _mode == ComposeMode.EditDraft;

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

        // EditDraft 모드에서는 원본 메일의 EntryId를 Draft 메시지 ID로 저장
        if (mode == ComposeMode.EditDraft && originalEmail != null)
        {
            _draftMessageId = originalEmail.EntryId;
        }

        // 첨부파일 변경 시 HasAttachments 속성 갱신
        Attachments.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasAttachments));

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

            case ComposeMode.EditDraft:
                // 임시보관함 메일 편집 - 기존 내용 그대로 로드
                To = ParseJsonArrayToString(_originalEmail.To);
                Cc = ParseJsonArrayToString(_originalEmail.Cc);
                Bcc = ParseJsonArrayToString(_originalEmail.Bcc);
                Subject = _originalEmail.Subject ?? "";
                InitialBody = _originalEmail.Body ?? "";
                break;
        }
    }

    /// <summary>
    /// JSON 배열 문자열을 세미콜론 구분 문자열로 변환
    /// </summary>
    private static string ParseJsonArrayToString(string? jsonArray)
    {
        if (string.IsNullOrWhiteSpace(jsonArray))
            return "";

        // JSON 배열이 아니면 그대로 반환
        if (!jsonArray.StartsWith("["))
            return jsonArray;

        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<string[]>(jsonArray);
            if (items == null || items.Length == 0)
                return "";

            return string.Join("; ", items);
        }
        catch
        {
            return jsonArray;
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

            // 대용량 파일 여부 판단 (3MB 기준)
            const long LargeFileThreshold = 3 * 1024 * 1024;
            var hasLargeFile = Attachments.Any(a => a.FileSize > LargeFileThreshold);

            if (hasLargeFile && Attachments.Count > 0)
            {
                // 대용량 경로: Draft 생성 → 소형 inline + 대용량 UploadSession → Send
                Log4.Debug2($"대용량 첨부 경로: {Attachments.Count}개 파일");

                // 소형 파일만 inline 첨부
                message.Attachments = new List<Microsoft.Graph.Models.Attachment>();
                foreach (var attachment in Attachments.Where(a => a.FileSize <= LargeFileThreshold))
                {
                    var bytes = await System.IO.File.ReadAllBytesAsync(attachment.FilePath);
                    message.Attachments.Add(new FileAttachment
                    {
                        Name = attachment.FileName,
                        ContentBytes = bytes,
                        ContentType = GetMimeType(attachment.FileName)
                    });
                }

                // Draft 생성
                var draft = await _graphMailService.SaveDraftAsync(message);
                if (draft?.Id == null)
                    throw new InvalidOperationException("Draft 메시지 생성 실패");

                Log4.Debug2($"Draft 생성 완료: {draft.Id}");

                // 대용량 파일 UploadSession 업로드
                foreach (var attachment in Attachments.Where(a => a.FileSize > LargeFileThreshold))
                {
                    Log4.Debug2($"대용량 첨부 업로드: {attachment.FileName} ({attachment.FileSize / 1024 / 1024}MB)");
                    await _graphMailService.UploadLargeAttachmentAsync(draft.Id, attachment.FilePath, attachment.FileName);
                }

                // Draft 발송
                await _graphMailService.SendDraftMessageAsync(draft.Id);
            }
            else if (Attachments.Count > 0)
            {
                // 소형 경로: 기존 inline 방식
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

                await _graphMailService.SendMessageAsync(message);
            }
            else
            {
                // 첨부파일 없음
                await _graphMailService.SendMessageAsync(message);
            }

            Log4.Info($"메일 발송 성공: To={To}, Subject={Subject}");

            // EditDraft 모드에서는 기존 Draft 삭제
            if (_mode == ComposeMode.EditDraft && !string.IsNullOrEmpty(_draftMessageId))
            {
                try
                {
                    await _graphMailService.DeleteMessageAsync(_draftMessageId);
                    Log4.Debug($"임시보관함 메일 삭제 완료: {_draftMessageId}");
                }
                catch (Exception ex)
                {
                    Log4.Warn($"임시보관함 메일 삭제 실패 (무시): {ex.Message}");
                }
            }

            // 보낸편지함 즉시 동기화 (비동기로 백그라운드 실행)
            var accountEmail = _originalEmail?.AccountEmail ?? _graphMailService.CurrentUserEmail;
            if (_syncService != null && !string.IsNullOrEmpty(accountEmail))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000); // 서버 반영 대기
                        await _syncService.SyncSentItemsAsync(accountEmail);
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
    /// 발송 취소 커맨드 — 카운트다운 중 취소
    /// </summary>
    [RelayCommand]
    public void CancelSend()
    {
        _sendCts?.Cancel();
        Log4.Info("메일 발송 취소됨");
    }

    /// <summary>
    /// 예약 발송 DB 저장 (실제 발송은 BackgroundSync가 처리)
    /// </summary>
    public async Task<bool> ScheduleMailAsync(mAIx.Data.mAIxDbContext dbContext)
    {
        try
        {
            var toRecipients = ParseEmailAddresses(To);
            if (toRecipients.Count == 0)
                throw new InvalidOperationException("받는 사람이 없습니다.");

            // 예약 시간이 과거면 즉시 발송으로 처리
            var sendTime = ScheduledSendTime < DateTime.Now ? DateTime.Now.AddSeconds(5) : ScheduledSendTime;

            // DB에 예약 메일 저장 (발송 상태: scheduled)
            var email = new mAIx.Models.Email
            {
                Subject = Subject,
                Body = Body,
                To = To,
                Cc = Cc,
                Bcc = Bcc,
                From = _graphMailService.CurrentUserEmail ?? "",
                AccountEmail = _graphMailService.CurrentUserEmail ?? "",
                ScheduledSendTime = sendTime,
                ReceivedDateTime = DateTime.Now,
                AnalysisStatus = "scheduled",
                IsRead = true
            };

            dbContext.Emails.Add(email);
            await dbContext.SaveChangesAsync();

            Log4.Info($"예약 발송 등록: {sendTime:yyyy-MM-dd HH:mm}, Subject={Subject}");
            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"예약 발송 등록 실패: {ex.Message}");
            throw;
        }
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
