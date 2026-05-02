using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace mAIx.Services.AI
{
    /// <summary>
    /// LM Studio 로컬 AI Provider (OpenAI 호환 API)
    /// </summary>
    public class LMStudioProvider : AIProviderBase
    {
        private const string DefaultBaseUrl = "http://localhost:1234/v1/chat/completions";
        private const string DefaultModel = "local-model";

        public override string ProviderName => "LMStudio";

        protected override bool IsLocalProvider => true;

        public LMStudioProvider()
        {
            _baseUrl = DefaultBaseUrl;
            _model = DefaultModel;
        }

        protected override void ConfigureHttpClient()
        {
            // LM Studio는 로컬 서버이므로 인증 불필요
            _httpClient.DefaultRequestHeaders.Clear();
        }

        /// <summary>
        /// LM Studio 서버 연결 상태 확인
        /// </summary>
        public override bool IsAvailable
        {
            get
            {
                try
                {
                    // P2-05: 데드락 방지 — Task.Run으로 동기 컨텍스트에서 비동기 호출 격리
                    var response = Task.Run(() => _httpClient.GetAsync("http://localhost:1234/v1/models"))
                        .GetAwaiter().GetResult();
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }

        public override async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 4096,
                temperature = 0.7
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Debug("LM Studio API 요청: Model={Model}", _model);

            var response = await _httpClient.PostAsync(_baseUrl, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response).ConfigureAwait(false);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // OpenAI 형식: choices[0].message.content 추출
            if (root.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    return contentElement.GetString() ?? string.Empty;
                }
            }

            _logger.Warning("LM Studio 응답에서 텍스트를 찾을 수 없음: {Response}", responseJson);
            return string.Empty;
        }

        public override async Task<IAsyncEnumerable<string>> StreamCompleteAsync(string prompt, CancellationToken ct = default)
        {
            return StreamCompleteInternalAsync(prompt, ct);
        }

        private async IAsyncEnumerable<string> StreamCompleteInternalAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 4096,
                temperature = 0.7,
                stream = true
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response).ConfigureAwait(false);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                    continue;

                var data = line.Substring(6);
                if (data == "[DONE]")
                    break;

                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // OpenAI 스트리밍 형식: choices[0].delta.content 추출
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var contentElement))
                    {
                        var text = contentElement.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 사용 가능한 모델 목록 조회
        /// </summary>
        public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        {
            var models = new List<string>();

            try
            {
                var response = await _httpClient.GetAsync("http://localhost:1234/v1/models", ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var model in dataArray.EnumerateArray())
                        {
                            if (model.TryGetProperty("id", out var idElement))
                            {
                                var id = idElement.GetString();
                                if (!string.IsNullOrEmpty(id))
                                {
                                    models.Add(id);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "LM Studio 모델 목록 조회 실패");
            }

            return models;
        }
    }
}
