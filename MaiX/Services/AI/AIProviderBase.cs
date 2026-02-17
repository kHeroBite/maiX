using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MaiX.Services.AI
{
    /// <summary>
    /// AI Provider 추상 기본 클래스
    /// 공통 기능(HTTP 클라이언트, 설정 관리)을 제공
    /// </summary>
    public abstract class AIProviderBase : IAIProvider
    {
        protected HttpClient _httpClient;
        protected string _apiKey;
        protected string _baseUrl;
        protected string _model;
        protected readonly ILogger _logger;

        /// <summary>
        /// Provider 이름 (하위 클래스에서 구현)
        /// </summary>
        public abstract string ProviderName { get; }

        /// <summary>
        /// 현재 설정된 모델명
        /// </summary>
        public string ModelName => _model ?? ProviderName;

        /// <summary>
        /// Provider 사용 가능 여부
        /// </summary>
        public virtual bool IsAvailable => !string.IsNullOrEmpty(_apiKey) || IsLocalProvider;

        /// <summary>
        /// 로컬 Provider 여부 (Ollama, LM Studio 등)
        /// </summary>
        protected virtual bool IsLocalProvider => false;

        protected AIProviderBase()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _logger = Log.ForContext(GetType());
        }

        /// <summary>
        /// Provider 설정 구성
        /// </summary>
        public virtual void Configure(string apiKey, string baseUrl = null, string model = null)
        {
            _apiKey = apiKey;
            if (!string.IsNullOrEmpty(baseUrl))
            {
                _baseUrl = baseUrl;
            }
            if (!string.IsNullOrEmpty(model))
            {
                _model = model;
            }

            ConfigureHttpClient();
            _logger.Information("{Provider} 설정 완료: BaseUrl={BaseUrl}, Model={Model}",
                ProviderName, _baseUrl, _model);
        }

        /// <summary>
        /// HTTP 클라이언트 설정 (하위 클래스에서 헤더 등 추가 설정)
        /// </summary>
        protected virtual void ConfigureHttpClient()
        {
            // 기본 구현: 하위 클래스에서 오버라이드
        }

        /// <summary>
        /// 텍스트 완성 요청 (하위 클래스에서 구현)
        /// </summary>
        public abstract Task<string> CompleteAsync(string prompt, CancellationToken ct = default);

        /// <summary>
        /// 스트리밍 텍스트 완성 요청 (하위 클래스에서 구현)
        /// </summary>
        public abstract Task<IAsyncEnumerable<string>> StreamCompleteAsync(string prompt, CancellationToken ct = default);

        /// <summary>
        /// JSON 직렬화 옵션
        /// </summary>
        protected static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <summary>
        /// HTTP 응답 에러 처리
        /// </summary>
        protected async Task HandleErrorResponseAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            _logger.Error("{Provider} API 오류: {StatusCode} - {Content}",
                ProviderName, response.StatusCode, content);
            throw new HttpRequestException($"{ProviderName} API 오류: {response.StatusCode} - {content}");
        }
    }
}
