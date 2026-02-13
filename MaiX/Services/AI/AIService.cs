using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MaiX.Services.AI
{
    /// <summary>
    /// AI 서비스 통합 관리 클래스
    /// 여러 AI Provider를 등록하고 전환하여 사용
    /// </summary>
    public class AIService
    {
        private readonly Dictionary<string, IAIProvider> _providers;
        private IAIProvider _currentProvider;
        private readonly ILogger _logger;

        public AIService()
        {
            _providers = new Dictionary<string, IAIProvider>(StringComparer.OrdinalIgnoreCase);
            _logger = Log.ForContext<AIService>();

            // 기본 Provider 등록
            RegisterDefaultProviders();
        }

        /// <summary>
        /// 현재 활성화된 Provider
        /// </summary>
        public IAIProvider CurrentProvider => _currentProvider;

        /// <summary>
        /// 현재 Provider 이름
        /// </summary>
        public string CurrentProviderName => _currentProvider?.ProviderName ?? "None";

        /// <summary>
        /// 기본 Provider 등록
        /// </summary>
        private void RegisterDefaultProviders()
        {
            RegisterProvider(new ClaudeProvider());
            RegisterProvider(new OpenAIProvider());
            RegisterProvider(new GeminiProvider());
            RegisterProvider(new OllamaProvider());
            RegisterProvider(new LMStudioProvider());

            _logger.Information("기본 AI Provider 등록 완료: {Count}개", _providers.Count);
        }

        /// <summary>
        /// Provider 등록
        /// </summary>
        public void RegisterProvider(IAIProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            _providers[provider.ProviderName] = provider;
            _logger.Debug("AI Provider 등록: {Name}", provider.ProviderName);

            // 첫 번째 Provider를 기본으로 설정
            if (_currentProvider == null)
            {
                _currentProvider = provider;
            }
        }

        /// <summary>
        /// 현재 Provider 설정
        /// </summary>
        public bool SetCurrentProvider(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                _logger.Warning("Provider 이름이 비어있음");
                return false;
            }

            if (_providers.TryGetValue(name, out var provider))
            {
                _currentProvider = provider;
                _logger.Information("현재 Provider 변경: {Name}", name);
                return true;
            }

            _logger.Warning("Provider를 찾을 수 없음: {Name}", name);
            return false;
        }

        /// <summary>
        /// Provider 설정 구성
        /// </summary>
        public bool ConfigureProvider(string name, string apiKey, string baseUrl = null, string model = null)
        {
            if (_providers.TryGetValue(name, out var provider))
            {
                provider.Configure(apiKey, baseUrl, model);
                return true;
            }

            _logger.Warning("설정할 Provider를 찾을 수 없음: {Name}", name);
            return false;
        }

        /// <summary>
        /// 사용 가능한 Provider 목록
        /// </summary>
        public List<string> GetAvailableProviders()
        {
            return _providers
                .Where(p => p.Value.IsAvailable)
                .Select(p => p.Key)
                .ToList();
        }

        /// <summary>
        /// 등록된 모든 Provider 목록
        /// </summary>
        public List<string> GetAllProviders()
        {
            return _providers.Keys.ToList();
        }

        /// <summary>
        /// Provider 인스턴스 조회
        /// </summary>
        public IAIProvider GetProvider(string name)
        {
            return _providers.TryGetValue(name, out var provider) ? provider : null;
        }

        /// <summary>
        /// 텍스트 완성 요청 (현재 Provider 사용)
        /// </summary>
        public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
        {
            EnsureProviderAvailable();
            return await _currentProvider.CompleteAsync(prompt, ct);
        }

        /// <summary>
        /// 스트리밍 텍스트 완성 요청 (현재 Provider 사용)
        /// </summary>
        public async Task<IAsyncEnumerable<string>> StreamCompleteAsync(string prompt, CancellationToken ct = default)
        {
            EnsureProviderAvailable();
            return await _currentProvider.StreamCompleteAsync(prompt, ct);
        }

        /// <summary>
        /// 특정 Provider로 텍스트 완성 요청
        /// </summary>
        public async Task<string> CompleteWithProviderAsync(string providerName, string prompt, CancellationToken ct = default)
        {
            var provider = GetProvider(providerName);
            if (provider == null)
            {
                throw new InvalidOperationException($"Provider를 찾을 수 없음: {providerName}");
            }

            if (!provider.IsAvailable)
            {
                throw new InvalidOperationException($"Provider가 사용 불가 상태: {providerName}");
            }

            return await provider.CompleteAsync(prompt, ct);
        }

        /// <summary>
        /// 특정 Provider로 스트리밍 텍스트 완성 요청
        /// </summary>
        public async Task<IAsyncEnumerable<string>> StreamCompleteWithProviderAsync(
            string providerName, string prompt, CancellationToken ct = default)
        {
            var provider = GetProvider(providerName);
            if (provider == null)
            {
                throw new InvalidOperationException($"Provider를 찾을 수 없음: {providerName}");
            }

            if (!provider.IsAvailable)
            {
                throw new InvalidOperationException($"Provider가 사용 불가 상태: {providerName}");
            }

            return await provider.StreamCompleteAsync(prompt, ct);
        }

        /// <summary>
        /// Provider 사용 가능 여부 확인
        /// </summary>
        private void EnsureProviderAvailable()
        {
            if (_currentProvider == null)
            {
                throw new InvalidOperationException("AI Provider가 설정되지 않음");
            }

            if (!_currentProvider.IsAvailable)
            {
                throw new InvalidOperationException($"AI Provider({_currentProvider.ProviderName})가 사용 불가 상태");
            }
        }

        /// <summary>
        /// Provider 상태 정보
        /// </summary>
        public Dictionary<string, bool> GetProviderStatus()
        {
            return _providers.ToDictionary(
                p => p.Key,
                p => p.Value.IsAvailable
            );
        }
    }
}
