using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Serilog;

namespace mAIx.Services.Graph;

/// <summary>
/// Microsoft Graph 통화/프레즌스 연동 서비스
/// </summary>
public class GraphCallService
{
    private readonly GraphAuthService _authService;
    private readonly ILogger _logger;

    public GraphCallService(GraphAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = Log.ForContext<GraphCallService>();
    }

    #region 프레즌스 (사용자 상태)

    /// <summary>
    /// 현재 로그인한 사용자의 프레즌스 조회
    /// </summary>
    public async Task<Presence?> GetMyPresenceAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var presence = await client.Me.Presence.GetAsync();
            _logger.Debug("내 프레즌스 조회: {Availability}", presence?.Availability);
            return presence;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "내 프레즌스 조회 실패");
            return null;
        }
    }

    /// <summary>
    /// 특정 사용자의 프레즌스 조회
    /// </summary>
    public async Task<Presence?> GetUserPresenceAsync(string userId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var presence = await client.Users[userId].Presence.GetAsync();
            _logger.Debug("사용자 {UserId} 프레즌스 조회: {Availability}", userId, presence?.Availability);
            return presence;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "사용자 {UserId} 프레즌스 조회 실패", userId);
            return null;
        }
    }

    /// <summary>
    /// 여러 사용자의 프레즌스 일괄 조회
    /// </summary>
    public async Task<IEnumerable<Presence>> GetPresencesByUserIdsAsync(IEnumerable<string> userIds)
    {
        try
        {
            var client = _authService.GetGraphClient();

            // Graph API는 presences를 한 번에 최대 650명까지 조회 가능
            var ids = userIds.Take(650).ToList();

            var requestBody = new Microsoft.Graph.Communications.GetPresencesByUserId.GetPresencesByUserIdPostRequestBody
            {
                Ids = ids
            };

            var response = await client.Communications.GetPresencesByUserId.PostAsGetPresencesByUserIdPostResponseAsync(requestBody);

            _logger.Debug("{Count}명의 프레즌스 일괄 조회 완료", response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Presence>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "프레즌스 일괄 조회 실패");
            return new List<Presence>();
        }
    }

    /// <summary>
    /// 내 프레즌스 상태 설정
    /// </summary>
    public async Task<bool> SetMyPresenceAsync(string availability, string activity, TimeSpan? duration = null)
    {
        try
        {
            var client = _authService.GetGraphClient();

            var requestBody = new Microsoft.Graph.Me.Presence.SetPresence.SetPresencePostRequestBody
            {
                SessionId = Guid.NewGuid().ToString(), // 앱 세션 ID
                Availability = availability, // Available, Busy, DoNotDisturb, Away, Offline
                Activity = activity, // Available, Busy, InACall, InAMeeting, Away, Offline 등
                ExpirationDuration = duration ?? TimeSpan.FromMinutes(5)
            };

            await client.Me.Presence.SetPresence.PostAsync(requestBody);
            _logger.Information("프레즌스 설정: {Availability}/{Activity}", availability, activity);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "프레즌스 설정 실패");
            return false;
        }
    }

    #endregion

    #region 사용자 검색

    /// <summary>
    /// 사용자 검색 (이름/이메일로)
    /// </summary>
    public async Task<IEnumerable<User>> SearchUsersAsync(string query, int top = 20)
    {
        try
        {
            var client = _authService.GetGraphClient();

            var response = await client.Users.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"startswith(displayName,'{query}') or startswith(mail,'{query}') or startswith(userPrincipalName,'{query}')";
                config.QueryParameters.Top = top;
                config.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "jobTitle", "department", "mobilePhone", "businessPhones" };
            });

            _logger.Debug("사용자 검색 '{Query}': {Count}명", query, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<User>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "사용자 검색 실패: {Query}", query);
            return new List<User>();
        }
    }

    /// <summary>
    /// 자주 연락하는 사용자 목록 조회
    /// </summary>
    public async Task<IEnumerable<Person>> GetFrequentContactsAsync(int top = 20)
    {
        try
        {
            var client = _authService.GetGraphClient();

            var response = await client.Me.People.GetAsync(config =>
            {
                config.QueryParameters.Top = top;
            });

            _logger.Debug("자주 연락하는 사용자 {Count}명 조회", response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Person>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "자주 연락하는 사용자 조회 실패");
            return new List<Person>();
        }
    }

    #endregion

    #region 통화 기록 (참고: 대부분 애플리케이션 권한 필요)

    /// <summary>
    /// 통화 기록 조회 (참고: CallRecords.Read.All 애플리케이션 권한 필요)
    /// 개인 계정에서는 사용 불가할 수 있음
    /// </summary>
    public async Task<IEnumerable<CallRecord>> GetCallRecordsAsync(int days = 7)
    {
        try
        {
            // CallRecords API는 애플리케이션 권한이 필요하여 대부분의 경우 사용 불가
            // 여기서는 빈 목록 반환
            _logger.Warning("통화 기록 API는 애플리케이션 권한이 필요합니다. 개인 계정에서는 사용 불가할 수 있습니다.");
            return new List<CallRecord>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "통화 기록 조회 실패");
            return new List<CallRecord>();
        }
    }

    #endregion
}

/// <summary>
/// 통화 기록 모델 (자체 정의 - Graph API CallRecord 대체용)
/// </summary>
public class CallRecord
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // incoming, outgoing, missed
    public string CallerName { get; set; } = string.Empty;
    public string CallerEmail { get; set; } = string.Empty;
    public string CallerPhone { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsMissed { get; set; }
    public bool IsVideoCall { get; set; }
}
