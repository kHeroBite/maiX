using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MaiX.Services.AI
{
    /// <summary>
    /// Ollama 로컬 AI Provider
    /// </summary>
    public class OllamaProvider : AIProviderBase
    {
        private const string DefaultBaseUrl = "http://localhost:11434/api/generate";
        private const string DefaultModel = "llama3";

        public override string ProviderName => "Ollama";

        protected override bool IsLocalProvider => true;

        public OllamaProvider()
        {
            _baseUrl = DefaultBaseUrl;
            _model = DefaultModel;
        }

        protected override void ConfigureHttpClient()
        {
            // Ollama는 로컬 서버이므로 인증 불필요
            _httpClient.DefaultRequestHeaders.Clear();
        }

        /// <summary>
        /// Ollama 서버 연결 상태 확인
        /// </summary>
        public override bool IsAvailable
        {
            get
            {
                try
                {
                    var response = _httpClient.GetAsync("http://localhost:11434/api/tags")
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
                prompt = prompt,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Debug("Ollama API 요청: Model={Model}", _model);

            var response = await _httpClient.PostAsync(_baseUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // response 필드 추출
            if (root.TryGetProperty("response", out var responseElement))
            {
                return responseElement.GetString() ?? string.Empty;
            }

            _logger.Warning("Ollama 응답에서 텍스트를 찾을 수 없음: {Response}", responseJson);
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
                prompt = prompt,
                stream = true
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    continue;

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // done 필드 확인
                if (root.TryGetProperty("done", out var doneElement) &&
                    doneElement.GetBoolean())
                {
                    break;
                }

                // response 필드에서 텍스트 추출
                if (root.TryGetProperty("response", out var responseElement))
                {
                    var text = responseElement.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
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
                var response = await _httpClient.GetAsync("http://localhost:11434/api/tags", ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                    {
                        foreach (var model in modelsArray.EnumerateArray())
                        {
                            if (model.TryGetProperty("name", out var nameElement))
                            {
                                var name = nameElement.GetString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    models.Add(name);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Ollama 모델 목록 조회 실패");
            }

            return models;
        }
    }
}
