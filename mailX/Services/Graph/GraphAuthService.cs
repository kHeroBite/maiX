using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using mailX.Models.Settings;
using mailX.Utils;

namespace mailX.Services.Graph
{
    /// <summary>
    /// MSAL 기반 Microsoft Graph OAuth 2.0 인증 서비스
    /// </summary>
    public class GraphAuthService
    {
        private string _clientId = "";
        private string _tenantId = "";
        private string[] _scopes = DefaultScopes;

        private IPublicClientApplication? _msalClient;
        private AuthenticationResult? _authResult;

        /// <summary>
        /// 기본 권한 범위
        /// </summary>
        private static readonly string[] DefaultScopes = new[]
        {
            "User.Read",
            "Mail.Read",
            "Mail.Send",
            "Mail.ReadWrite",
            "Files.Read.All",
            "Sites.Read.All",
            "Calendars.Read",
            "Calendars.ReadWrite"
        };

        /// <summary>
        /// 현재 로그인된 사용자 이메일
        /// </summary>
        public string? CurrentUserEmail => _authResult?.Account?.Username;

        /// <summary>
        /// 현재 로그인된 사용자 표시 이름
        /// </summary>
        public string? CurrentUserDisplayName => _authResult?.Account?.Username?.Split('@').FirstOrDefault();

        /// <summary>
        /// 로그인 여부
        /// </summary>
        public bool IsLoggedIn => _authResult != null && !string.IsNullOrEmpty(_authResult.AccessToken);

        /// <summary>
        /// MSAL 클라이언트가 구성되었는지 여부
        /// </summary>
        public bool IsConfigured => !string.IsNullOrEmpty(_clientId) && _msalClient != null;

        /// <summary>
        /// 현재 설정된 ClientId
        /// </summary>
        public string ClientId => _clientId;

        public GraphAuthService()
        {
            // 기본 생성자 - Initialize 호출 전까지 미구성 상태
            Log4.Debug("[GraphAuthService] 인스턴스 생성 (미구성 상태)");
        }

        /// <summary>
        /// Azure AD 설정으로 MSAL 클라이언트 초기화
        /// </summary>
        /// <param name="settings">Azure AD 설정</param>
        public void Initialize(AzureAdSettings settings)
        {
            if (settings == null)
            {
                Log4.Error("[GraphAuthService] AzureAdSettings가 null입니다");
                return;
            }

            _clientId = settings.ClientId ?? "";
            _tenantId = settings.TenantId;
            _scopes = settings.EffectiveScopes.ToArray();

            Log4.Debug($"[GraphAuthService] Initialize - ClientId: {(string.IsNullOrEmpty(_clientId) ? "(미설정)" : _clientId.Substring(0, Math.Min(8, _clientId.Length)) + "...")}");
            Log4.Debug($"[GraphAuthService] Initialize - TenantId: {_tenantId}");
            Log4.Debug($"[GraphAuthService] Initialize - Scopes: {string.Join(", ", _scopes)}");

            if (string.IsNullOrEmpty(_clientId))
            {
                Log4.Debug("[GraphAuthService] ClientId가 설정되지 않아 MSAL 클라이언트 생성 건너뜀");
                _msalClient = null;
                return;
            }

            if (string.IsNullOrEmpty(_tenantId))
            {
                Log4.Error("[GraphAuthService] TenantId가 설정되지 않았습니다. Azure Portal에서 테넌트 ID를 확인하세요.");
                _msalClient = null;
                return;
            }

            try
            {
                // MSAL.NET 데스크톱 앱은 localhost만 지원
                const string redirectUri = "http://localhost";

                _msalClient = PublicClientApplicationBuilder
                    .Create(_clientId)
                    .WithAuthority(AzureCloudInstance.AzurePublic, _tenantId)
                    .WithRedirectUri(redirectUri)
                    .Build();

                Log4.Debug($"[GraphAuthService] Redirect URI: {redirectUri}");

                // 토큰 캐시 활성화
                TokenCacheHelper.EnableSerialization(_msalClient.UserTokenCache);

                Log4.Info("[GraphAuthService] MSAL 클라이언트 초기화 완료");
            }
            catch (Exception ex)
            {
                Log4.Error($"[GraphAuthService] MSAL 클라이언트 초기화 실패: {ex.Message}");
                _msalClient = null;
            }
        }

        /// <summary>
        /// ClientId로 직접 초기화 (UI에서 입력받은 경우)
        /// </summary>
        /// <param name="clientId">Azure AD 애플리케이션 ID</param>
        /// <param name="tenantId">테넌트 ID (단일 테넌트 앱은 실제 테넌트 ID 필요)</param>
        /// <param name="scopes">권한 범위 (기본값 사용 가능)</param>
        public void Initialize(string clientId, string? tenantId = null, string[]? scopes = null)
        {
            var settings = new AzureAdSettings
            {
                ClientId = clientId,
                TenantId = tenantId ?? "",
                Scopes = scopes?.ToList() ?? DefaultScopes.ToList()
            };
            Initialize(settings);
        }

        /// <summary>
        /// 대화형 로그인 (브라우저 팝업)
        /// </summary>
        /// <returns>로그인 성공 여부</returns>
        public async Task<bool> LoginInteractiveAsync()
        {
            try
            {
                Log4.Debug("[GraphAuthService] 대화형 로그인 시작");

                if (!IsConfigured)
                {
                    Log4.Error("[GraphAuthService] ClientId가 설정되지 않았습니다. Initialize를 먼저 호출하세요.");
                    throw new InvalidOperationException("Azure AD ClientId가 설정되지 않았습니다. ClientId를 입력해주세요.");
                }

                _authResult = await _msalClient!
                    .AcquireTokenInteractive(_scopes)
                    .ExecuteAsync();

                Log4.Info($"[GraphAuthService] 로그인 성공 - {_authResult.Account?.Username}");
                return IsLoggedIn;
            }
            catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
            {
                Log4.Debug("[GraphAuthService] 사용자가 로그인을 취소했습니다.");
                return false;
            }
            catch (MsalException ex)
            {
                Log4.Error($"[GraphAuthService] MSAL 로그인 실패: {ex.ErrorCode} - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log4.Error($"[GraphAuthService] 로그인 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 자동 로그인 (캐시된 토큰 사용)
        /// </summary>
        /// <param name="email">사용자 이메일 (선택)</param>
        /// <returns>로그인 성공 여부</returns>
        public async Task<bool> LoginSilentAsync(string? email = null)
        {
            try
            {
                Log4.Debug($"[GraphAuthService] Silent 로그인 시도 - Email: {email ?? "(자동)"}");

                if (!IsConfigured)
                {
                    Log4.Debug("[GraphAuthService] MSAL 클라이언트 미구성 - Silent 로그인 불가");
                    return false;
                }

                var accounts = await _msalClient!.GetAccountsAsync();
                IAccount? account = null;

                if (!string.IsNullOrEmpty(email))
                {
                    account = accounts.FirstOrDefault(a =>
                        string.Equals(a.Username, email, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    account = accounts.FirstOrDefault();
                }

                if (account == null)
                {
                    Log4.Debug("[GraphAuthService] 캐시된 계정 없음");
                    return false;
                }

                _authResult = await _msalClient
                    .AcquireTokenSilent(_scopes, account)
                    .ExecuteAsync();

                Log4.Info($"[GraphAuthService] Silent 로그인 성공 - {_authResult.Account?.Username}");
                return IsLoggedIn;
            }
            catch (MsalUiRequiredException)
            {
                // 대화형 로그인 필요
                Log4.Debug("[GraphAuthService] 토큰 만료 - 대화형 로그인 필요");
                return false;
            }
            catch (MsalException ex)
            {
                Log4.Error($"[GraphAuthService] Silent 로그인 실패: {ex.ErrorCode} - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 로그아웃
        /// </summary>
        public async Task LogoutAsync()
        {
            Log4.Debug("[GraphAuthService] 로그아웃 시작");

            if (_msalClient != null)
            {
                var accounts = await _msalClient.GetAccountsAsync();
                foreach (var account in accounts)
                {
                    await _msalClient.RemoveAsync(account);
                }
            }

            // 캐시 파일 삭제
            TokenCacheHelper.ClearCache();

            // MSAL 클라이언트 완전 초기화 (재사용 방지)
            _msalClient = null;
            _authResult = null;
            _clientId = "";

            Log4.Info("[GraphAuthService] 로그아웃 완료 (MSAL 클라이언트 초기화됨)");
        }

        /// <summary>
        /// GraphServiceClient 생성
        /// </summary>
        /// <returns>인증된 GraphServiceClient</returns>
        public GraphServiceClient GetGraphClient()
        {
            if (!IsLoggedIn)
            {
                throw new InvalidOperationException("로그인이 필요합니다.");
            }

            var authProvider = new DelegateAuthenticationProvider(GetAccessTokenAsync);
            return new GraphServiceClient(authProvider);
        }

        /// <summary>
        /// 현재 액세스 토큰 반환 (자동 갱신)
        /// </summary>
        private async Task<string> GetAccessTokenAsync()
        {
            if (_authResult == null)
            {
                throw new InvalidOperationException("로그인이 필요합니다.");
            }

            // 토큰 만료 5분 전이면 자동 갱신
            if (_authResult.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                Log4.Debug("[GraphAuthService] 토큰 자동 갱신 시도");
                await LoginSilentAsync(_authResult.Account?.Username);
            }

            return _authResult.AccessToken;
        }
    }

    /// <summary>
    /// Kiota용 위임 인증 공급자
    /// </summary>
    public class DelegateAuthenticationProvider : IAuthenticationProvider
    {
        private readonly Func<Task<string>> _getAccessToken;

        public DelegateAuthenticationProvider(Func<Task<string>> getAccessToken)
        {
            _getAccessToken = getAccessToken ?? throw new ArgumentNullException(nameof(getAccessToken));
        }

        public async Task AuthenticateRequestAsync(
            RequestInformation request,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            var token = await _getAccessToken();
            request.Headers.Add("Authorization", $"Bearer {token}");
        }
    }
}
