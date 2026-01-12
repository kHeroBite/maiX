using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace mailX.Services.Graph
{
    /// <summary>
    /// MSAL 기반 Microsoft Graph OAuth 2.0 인증 서비스
    /// </summary>
    public class GraphAuthService
    {
        // TODO: 설정 파일로 이동
        private const string ClientId = "YOUR_CLIENT_ID_HERE";
        private const string TenantId = "common"; // 모든 조직 및 개인 계정 허용

        /// <summary>
        /// Microsoft Graph API 권한 범위
        /// </summary>
        private static readonly string[] Scopes = new[]
        {
            "User.Read",
            "Mail.Read",
            "Mail.Send",
            "Mail.ReadWrite",
            "Files.Read.All",
            "Sites.Read.All"
        };

        private readonly IPublicClientApplication _msalClient;
        private AuthenticationResult _authResult;

        /// <summary>
        /// 현재 로그인된 사용자 이메일
        /// </summary>
        public string CurrentUserEmail => _authResult?.Account?.Username;

        /// <summary>
        /// 로그인 여부
        /// </summary>
        public bool IsLoggedIn => _authResult != null && !string.IsNullOrEmpty(_authResult.AccessToken);

        public GraphAuthService()
        {
            _msalClient = PublicClientApplicationBuilder
                .Create(ClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, TenantId)
                .WithDefaultRedirectUri()
                .Build();

            // 토큰 캐시 활성화
            TokenCacheHelper.EnableSerialization(_msalClient.UserTokenCache);
        }

        /// <summary>
        /// 대화형 로그인 (브라우저 팝업)
        /// </summary>
        /// <returns>로그인 성공 여부</returns>
        public async Task<bool> LoginInteractiveAsync()
        {
            try
            {
                _authResult = await _msalClient
                    .AcquireTokenInteractive(Scopes)
                    .ExecuteAsync();

                return IsLoggedIn;
            }
            catch (MsalException)
            {
                return false;
            }
        }

        /// <summary>
        /// 자동 로그인 (캐시된 토큰 사용)
        /// </summary>
        /// <param name="email">사용자 이메일 (선택)</param>
        /// <returns>로그인 성공 여부</returns>
        public async Task<bool> LoginSilentAsync(string email = null)
        {
            try
            {
                var accounts = await _msalClient.GetAccountsAsync();
                IAccount account = null;

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
                    return false;
                }

                _authResult = await _msalClient
                    .AcquireTokenSilent(Scopes, account)
                    .ExecuteAsync();

                return IsLoggedIn;
            }
            catch (MsalUiRequiredException)
            {
                // 대화형 로그인 필요
                return false;
            }
            catch (MsalException)
            {
                return false;
            }
        }

        /// <summary>
        /// 로그아웃
        /// </summary>
        public async Task LogoutAsync()
        {
            var accounts = await _msalClient.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _msalClient.RemoveAsync(account);
            }

            TokenCacheHelper.ClearCache();
            _authResult = null;
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
            Dictionary<string, object> additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            var token = await _getAccessToken();
            request.Headers.Add("Authorization", $"Bearer {token}");
        }
    }
}
