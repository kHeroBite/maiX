using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Graph.Models;
using Serilog;
using mAIx.Models;

namespace mAIx.Services.Notification;

/// <summary>
/// 알림 서비스 - ntfy를 통한 푸시 알림 발송
/// Channel 기반 비동기 큐 및 배치 처리 지원
/// </summary>
public class NotificationService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly NotificationSettings _settings;
    private readonly ILogger _logger;

    // 알림 큐 (Channel 기반)
    private readonly Channel<NotificationMessage> _notificationQueue;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;

    // 배치 처리용 버퍼
    private readonly Dictionary<string, List<NotificationMessage>> _batchBuffer = new();
    private DateTime _lastBatchFlush = DateTime.UtcNow;

    // 통계
    private int _sentCount;
    private int _failedCount;

    public NotificationService(NotificationSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = Log.ForContext<NotificationService>();

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // ntfy 인증 토큰 설정
        if (!string.IsNullOrEmpty(_settings.NtfyAuthToken))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.NtfyAuthToken}");
        }

        // 알림 큐 생성
        _notificationQueue = System.Threading.Channels.Channel.CreateBounded<NotificationMessage>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true
            });

        _cts = new CancellationTokenSource();

        // 백그라운드 처리 시작
        _processingTask = Task.Run(ProcessNotificationsAsync);
    }

    /// <summary>
    /// 새 메일 알림 발송
    /// </summary>
    /// <param name="email">새 이메일</param>
    public async Task NotifyNewEmailAsync(Email email)
    {
        if (!_settings.EnableNewMailNotification)
            return;

        if (_settings.ExcludeNonBusinessMail && email.IsNonBusiness)
            return;

        if (_settings.IsQuietHoursNow())
        {
            _logger.Debug("방해 금지 시간 - 알림 생략: {Subject}", email.Subject);
            return;
        }

        var message = new NotificationMessage
        {
            Title = $"새 메일: {TruncateText(email.Subject, 50)}",
            Message = $"발신자: {email.From}\n" +
                     $"{TruncateText(email.SummaryOneline ?? StripHtml(email.Body), 100)}",
            Priority = NotificationPriority.Default,
            Tags = new List<string> { "email", "incoming_envelope" },
            EmailId = email.Id,
            Sender = email.From
        };

        await EnqueueAsync(message);
    }

    /// <summary>
    /// 중요 메일 알림 발송
    /// </summary>
    /// <param name="email">중요 이메일</param>
    public async Task NotifyImportantEmailAsync(Email email)
    {
        if (!_settings.EnableImportantMailNotification)
            return;

        if (email.PriorityScore < _settings.MinPriorityForNotification)
            return;

        if (_settings.IsQuietHoursNow())
        {
            _logger.Debug("방해 금지 시간 - 중요 알림 생략: {Subject}", email.Subject);
            return;
        }

        var priority = email.PriorityLevel switch
        {
            "critical" => NotificationPriority.Urgent,
            "high" => NotificationPriority.High,
            _ => NotificationPriority.Default
        };

        var tags = new List<string> { "email" };
        if (email.PriorityLevel == "critical")
            tags.Add("rotating_light");
        else if (email.PriorityLevel == "high")
            tags.Add("warning");
        else
            tags.Add("star");

        var message = new NotificationMessage
        {
            Title = $"[{email.PriorityLevel?.ToUpper()}] {TruncateText(email.Subject, 40)}",
            Message = $"발신자: {email.From}\n" +
                     $"우선순위: {email.PriorityScore}점\n" +
                     $"{TruncateText(email.SummaryOneline ?? "", 80)}",
            Priority = priority,
            Tags = tags,
            EmailId = email.Id,
            Sender = email.From
        };

        // 중요 메일은 배치 처리 없이 즉시 발송
        await SendNotificationAsync(message);
    }

    /// <summary>
    /// 마감일 임박 알림 발송
    /// </summary>
    /// <param name="email">마감일이 있는 이메일</param>
    public async Task NotifyDeadlineReminderAsync(Email email)
    {
        if (!_settings.EnableDeadlineReminder)
            return;

        if (!email.Deadline.HasValue)
            return;

        var daysUntilDeadline = (email.Deadline.Value - DateTime.UtcNow).Days;

        if (daysUntilDeadline > _settings.DeadlineReminderDays || daysUntilDeadline < 0)
            return;

        if (_settings.IsQuietHoursNow())
        {
            _logger.Debug("방해 금지 시간 - 마감일 알림 생략: {Subject}", email.Subject);
            return;
        }

        var priority = daysUntilDeadline switch
        {
            0 => NotificationPriority.Urgent,
            1 => NotificationPriority.High,
            _ => NotificationPriority.Default
        };

        var deadlineText = daysUntilDeadline switch
        {
            0 => "오늘 마감!",
            1 => "내일 마감",
            _ => $"{daysUntilDeadline}일 후 마감"
        };

        var message = new NotificationMessage
        {
            Title = $"마감 임박: {TruncateText(email.Subject, 40)}",
            Message = $"{deadlineText}\n마감일: {email.Deadline:yyyy-MM-dd}\n발신자: {email.From}",
            Priority = priority,
            Tags = new List<string> { "email", "alarm_clock", "deadline" },
            EmailId = email.Id
        };

        await SendNotificationAsync(message);
    }

    /// <summary>
    /// 일반 알림 발송 (커스텀)
    /// </summary>
    /// <param name="title">제목</param>
    /// <param name="message">본문</param>
    /// <param name="priority">우선순위</param>
    /// <param name="tags">태그 목록</param>
    public async Task NotifyAsync(
        string title,
        string message,
        NotificationPriority priority = NotificationPriority.Default,
        List<string>? tags = null)
    {
        if (_settings.IsQuietHoursNow() && priority < NotificationPriority.High)
            return;

        var notification = new NotificationMessage
        {
            Title = title,
            Message = message,
            Priority = priority,
            Tags = tags ?? new List<string>()
        };

        await SendNotificationAsync(notification);
    }

    /// <summary>
    /// 캘린더 일정 임박 알림 발송
    /// </summary>
    /// <param name="calendarEvent">캘린더 이벤트</param>
    /// <param name="minutesBefore">몇 분 전 알림인지</param>
    public async Task NotifyUpcomingEventAsync(Event calendarEvent, int minutesBefore)
    {
        if (!_settings.EnableCalendarReminder)
            return;

        if (calendarEvent == null || string.IsNullOrEmpty(calendarEvent.Subject))
            return;

        // 방해 금지 시간에는 긴급 알림만 허용
        if (_settings.IsQuietHoursNow() && minutesBefore > 5)
        {
            _logger.Debug("방해 금지 시간 - 일정 알림 생략: {Subject}", calendarEvent.Subject);
            return;
        }

        var priority = minutesBefore switch
        {
            <= 5 => NotificationPriority.Urgent,
            <= 15 => NotificationPriority.High,
            _ => NotificationPriority.Default
        };

        var timeText = minutesBefore switch
        {
            0 => "지금 시작!",
            1 => "1분 후 시작",
            < 60 => $"{minutesBefore}분 후 시작",
            60 => "1시간 후 시작",
            _ => $"{minutesBefore / 60}시간 후 시작"
        };

        // 시작 시간 파싱
        string startTimeText = "";
        if (calendarEvent.Start?.DateTime != null)
        {
            var startDt = DateTime.Parse(calendarEvent.Start.DateTime);
            startTimeText = startDt.ToString("HH:mm");
        }

        // 장소 정보
        var locationText = !string.IsNullOrEmpty(calendarEvent.Location?.DisplayName)
            ? $"\n장소: {calendarEvent.Location.DisplayName}"
            : "";

        // 온라인 회의 정보
        var onlineMeetingText = (calendarEvent.IsOnlineMeeting ?? false)
            ? "\n🔗 Teams 온라인 회의"
            : "";

        var message = new NotificationMessage
        {
            Title = $"📅 {timeText}: {TruncateText(calendarEvent.Subject, 40)}",
            Message = $"시작: {startTimeText}{locationText}{onlineMeetingText}",
            Priority = priority,
            Tags = new List<string> { "calendar", "bell" }
        };

        // 온라인 회의 링크가 있으면 클릭 URL로 설정
        if (calendarEvent.IsOnlineMeeting ?? false)
        {
            message.Tags.Add("video_camera");
            if (!string.IsNullOrEmpty(calendarEvent.OnlineMeeting?.JoinUrl))
            {
                message.ClickUrl = calendarEvent.OnlineMeeting.JoinUrl;
            }
        }

        await SendNotificationAsync(message);
        _logger.Information("일정 알림 발송: {Subject} ({Minutes}분 전)", calendarEvent.Subject, minutesBefore);
    }

    /// <summary>
    /// 여러 일정에 대한 일괄 알림 확인 및 발송
    /// </summary>
    /// <param name="events">확인할 일정 목록</param>
    /// <param name="reminderMinutes">알림 시간 목록 (분 단위, 예: [15, 60])</param>
    public async Task CheckAndNotifyUpcomingEventsAsync(IEnumerable<Event> events, List<int>? reminderMinutes = null)
    {
        if (!_settings.EnableCalendarReminder)
            return;

        reminderMinutes ??= _settings.ReminderMinutesBefore ?? new List<int> { 15, 60 };
        var now = DateTime.Now;

        foreach (var evt in events)
        {
            if (evt.Start?.DateTime == null)
                continue;

            var startTime = DateTime.Parse(evt.Start.DateTime);
            var minutesUntilStart = (int)(startTime - now).TotalMinutes;

            // 이미 시작한 일정은 제외
            if (minutesUntilStart < 0)
                continue;

            // 설정된 알림 시간과 매칭되는지 확인 (±2분 허용)
            foreach (var reminderMin in reminderMinutes)
            {
                if (Math.Abs(minutesUntilStart - reminderMin) <= 2)
                {
                    await NotifyUpcomingEventAsync(evt, minutesUntilStart);
                    break; // 하나의 알림 시간에만 발송
                }
            }
        }
    }

    /// <summary>
    /// 알림 큐에 추가 (배치 처리용)
    /// </summary>
    private async Task EnqueueAsync(NotificationMessage message)
    {
        try
        {
            await _notificationQueue.Writer.WriteAsync(message, _cts.Token);
            _logger.Debug("알림 큐에 추가됨: {Title}", message.Title);
        }
        catch (ChannelClosedException)
        {
            _logger.Warning("알림 큐가 닫힘 - 메시지 드롭: {Title}", message.Title);
        }
    }

    /// <summary>
    /// 백그라운드 알림 처리
    /// </summary>
    private async Task ProcessNotificationsAsync()
    {
        _logger.Information("알림 처리 백그라운드 작업 시작");

        try
        {
            await foreach (var message in _notificationQueue.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    // 배치 처리 로직
                    if (!string.IsNullOrEmpty(message.Sender))
                    {
                        AddToBatch(message);

                        // 배치 플러시 조건 확인
                        if (ShouldFlushBatch())
                        {
                            await FlushBatchAsync();
                        }
                    }
                    else
                    {
                        await SendNotificationAsync(message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "알림 처리 실패: {Title}", message.Title);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Information("알림 처리 취소됨");
        }

        // 남은 배치 플러시
        await FlushBatchAsync();
        _logger.Information("알림 처리 백그라운드 작업 종료");
    }

    /// <summary>
    /// 배치에 메시지 추가
    /// </summary>
    private void AddToBatch(NotificationMessage message)
    {
        var key = message.Sender ?? "unknown";

        if (!_batchBuffer.ContainsKey(key))
        {
            _batchBuffer[key] = new List<NotificationMessage>();
        }

        _batchBuffer[key].Add(message);
    }

    /// <summary>
    /// 배치 플러시 조건 확인
    /// </summary>
    private bool ShouldFlushBatch()
    {
        // 최대 크기 도달
        var totalCount = _batchBuffer.Values.Sum(list => list.Count);
        if (totalCount >= _settings.MaxBatchSize)
            return true;

        // 시간 간격 초과
        var elapsed = DateTime.UtcNow - _lastBatchFlush;
        if (elapsed.TotalMinutes >= _settings.BatchIntervalMinutes && totalCount > 0)
            return true;

        return false;
    }

    /// <summary>
    /// 배치 플러시
    /// </summary>
    private async Task FlushBatchAsync()
    {
        if (_batchBuffer.Count == 0)
            return;

        foreach (var (sender, messages) in _batchBuffer)
        {
            if (messages.Count == 0)
                continue;

            NotificationMessage notification;

            if (messages.Count == 1)
            {
                notification = messages[0];
            }
            else
            {
                // 여러 메시지를 하나로 묶음
                notification = new NotificationMessage
                {
                    Title = $"{sender}에서 {messages.Count}개의 새 메일",
                    Message = string.Join("\n", messages.Select(m => $"- {TruncateText(m.Title, 40)}")),
                    Priority = messages.Max(m => m.Priority),
                    Tags = new List<string> { "email", "incoming_envelope", "package" }
                };
            }

            await SendNotificationAsync(notification);
        }

        _batchBuffer.Clear();
        _lastBatchFlush = DateTime.UtcNow;
    }

    /// <summary>
    /// ntfy로 알림 발송
    /// </summary>
    private async Task SendNotificationAsync(NotificationMessage message)
    {
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["topic"] = _settings.NtfyTopic,
                ["title"] = message.Title,
                ["message"] = message.Message,
                ["priority"] = (int)message.Priority
            };

            if (message.Tags.Count > 0)
            {
                payload["tags"] = message.Tags;
            }

            if (!string.IsNullOrEmpty(message.ClickUrl))
            {
                payload["click"] = message.ClickUrl;
            }

            if (!string.IsNullOrEmpty(message.AttachmentUrl))
            {
                payload["attach"] = message.AttachmentUrl;
            }

            if (message.Actions.Count > 0)
            {
                payload["actions"] = message.Actions.Select(a => new Dictionary<string, object?>
                {
                    ["action"] = a.Action,
                    ["label"] = a.Label,
                    ["url"] = a.Url,
                    ["method"] = a.Method,
                    ["body"] = a.Body
                }.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value));
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_settings.NtfyServerUrl, content);

            if (response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _sentCount);
                _logger.Information("알림 발송 성공: {Title}", message.Title);
            }
            else
            {
                Interlocked.Increment(ref _failedCount);
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.Warning("알림 발송 실패: {Status} - {Error}",
                    response.StatusCode, errorBody);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedCount);
            _logger.Error(ex, "알림 발송 예외: {Title}", message.Title);
        }
    }

    /// <summary>
    /// 통계 조회
    /// </summary>
    public (int sent, int failed, int pending) GetStats()
    {
        var pending = _batchBuffer.Values.Sum(list => list.Count);
        return (_sentCount, _failedCount, pending);
    }

    /// <summary>
    /// 텍스트 자르기
    /// </summary>
    private static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        text = text.Replace("\r", "").Replace("\n", " ").Trim();

        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    /// HTML 태그 제거
    /// </summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        return System.Text.RegularExpressions.Regex
            .Replace(html, "<[^>]*>", " ")
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Trim();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _notificationQueue.Writer.Complete();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException) { }

        _cts.Dispose();
        _httpClient.Dispose();
    }
}
