using System;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Serilog;
using mAIx.Services.Graph;

namespace mAIx.Services
{
    /// <summary>
    /// 부재중 자동응답 설정 모델
    /// </summary>
    public class AutoReplySetting
    {
        /// <summary>
        /// 자동응답 상태: disabled, alwaysEnabled, scheduled
        /// </summary>
        public string Status { get; set; } = "disabled";

        /// <summary>
        /// 내부 수신자 응답 메시지 (HTML)
        /// </summary>
        public string InternalReplyMessage { get; set; } = "";

        /// <summary>
        /// 외부 수신자 응답 메시지 (HTML)
        /// </summary>
        public string ExternalReplyMessage { get; set; } = "";

        /// <summary>
        /// 예약 시작 시간
        /// </summary>
        public DateTimeOffset? ScheduledStartDateTime { get; set; }

        /// <summary>
        /// 예약 종료 시간
        /// </summary>
        public DateTimeOffset? ScheduledEndDateTime { get; set; }

        /// <summary>
        /// 외부 수신자 범위: all, contactsOnly, none
        /// </summary>
        public string ExternalAudience { get; set; } = "all";

        /// <summary>
        /// 활성화 여부
        /// </summary>
        public bool IsEnabled => Status != "disabled";
    }

    /// <summary>
    /// Microsoft Graph API를 통한 부재중 자동응답 서비스
    /// 필요 스코프: MailboxSettings.ReadWrite
    /// </summary>
    public class AutoReplyService
    {
        private readonly GraphAuthService _authService;
        private readonly ILogger _logger;

        public AutoReplyService(GraphAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _logger = Log.ForContext<AutoReplyService>();
        }

        /// <summary>
        /// 현재 자동응답 설정 조회
        /// </summary>
        public async Task<AutoReplySetting> GetAutoReplyStatusAsync()
        {
            try
            {
                var client = _authService.GetGraphClient();
                var settings = await client.Me.MailboxSettings.GetAsync();

                if (settings?.AutomaticRepliesSetting == null)
                {
                    return new AutoReplySetting();
                }

                var ars = settings.AutomaticRepliesSetting;
                return new AutoReplySetting
                {
                    Status = ars.Status switch
                    {
                        AutomaticRepliesStatus.AlwaysEnabled => "alwaysEnabled",
                        AutomaticRepliesStatus.Scheduled => "scheduled",
                        _ => "disabled"
                    },
                    InternalReplyMessage = ars.InternalReplyMessage ?? "",
                    ExternalReplyMessage = ars.ExternalReplyMessage ?? "",
                    ScheduledStartDateTime = ars.ScheduledStartDateTime?.DateTime != null
                        ? DateTimeOffset.Parse(ars.ScheduledStartDateTime.DateTime)
                        : null,
                    ScheduledEndDateTime = ars.ScheduledEndDateTime?.DateTime != null
                        ? DateTimeOffset.Parse(ars.ScheduledEndDateTime.DateTime)
                        : null,
                    ExternalAudience = ars.ExternalAudience switch
                    {
                        ExternalAudienceScope.ContactsOnly => "contactsOnly",
                        ExternalAudienceScope.None => "none",
                        _ => "all"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "자동응답 설정 조회 실패");
                throw;
            }
        }

        /// <summary>
        /// 자동응답 설정 적용
        /// </summary>
        public async Task SetAutoReplyAsync(AutoReplySetting setting)
        {
            try
            {
                var client = _authService.GetGraphClient();

                var status = setting.Status switch
                {
                    "alwaysEnabled" => AutomaticRepliesStatus.AlwaysEnabled,
                    "scheduled" => AutomaticRepliesStatus.Scheduled,
                    _ => AutomaticRepliesStatus.Disabled
                };

                var externalAudience = setting.ExternalAudience switch
                {
                    "contactsOnly" => ExternalAudienceScope.ContactsOnly,
                    "none" => ExternalAudienceScope.None,
                    _ => ExternalAudienceScope.All
                };

                var automaticReplies = new AutomaticRepliesSetting
                {
                    Status = status,
                    InternalReplyMessage = setting.InternalReplyMessage,
                    ExternalReplyMessage = setting.ExternalReplyMessage,
                    ExternalAudience = externalAudience
                };

                // 예약 모드인 경우 시간 설정
                if (status == AutomaticRepliesStatus.Scheduled)
                {
                    if (setting.ScheduledStartDateTime.HasValue)
                    {
                        automaticReplies.ScheduledStartDateTime = new DateTimeTimeZone
                        {
                            DateTime = setting.ScheduledStartDateTime.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                            TimeZone = TimeZoneInfo.Local.Id
                        };
                    }
                    if (setting.ScheduledEndDateTime.HasValue)
                    {
                        automaticReplies.ScheduledEndDateTime = new DateTimeTimeZone
                        {
                            DateTime = setting.ScheduledEndDateTime.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                            TimeZone = TimeZoneInfo.Local.Id
                        };
                    }
                }

                await client.Me.MailboxSettings.PatchAsync(new MailboxSettings
                {
                    AutomaticRepliesSetting = automaticReplies
                });

                _logger.Information("자동응답 설정 적용 완료: {Status}", setting.Status);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "자동응답 설정 적용 실패");
                throw;
            }
        }

        /// <summary>
        /// 자동응답 비활성화
        /// </summary>
        public async Task DisableAutoReplyAsync()
        {
            await SetAutoReplyAsync(new AutoReplySetting { Status = "disabled" });
        }
    }
}
