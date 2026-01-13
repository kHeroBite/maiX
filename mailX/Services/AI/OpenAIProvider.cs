using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace mailX.Services.AI
{
    /// <summary>
    /// OpenAI API Provider
    /// </summary>
    public class OpenAIProvider : AIProviderBase
    {
        private const string DefaultBaseUrl = "https://api.openai.com/v1/chat/completions";
        private const string DefaultModel = "gpt-4o";

        public override string ProviderName => "OpenAI";

        public OpenAIProvider()
        {
            _baseUrl = DefaultBaseUrl;
            _model = DefaultModel;
        }

        protected override void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);
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
                max_tokens = 4096
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Debug("OpenAI API 요청: Model={Model}", _model);

            var response = await _httpClient.PostAsync(_baseUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // choices[0].message.content 추출
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

            _logger.Warning("OpenAI 응답에서 텍스트를 찾을 수 없음: {Response}", responseJson);
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
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                    continue;

                var data = line.Substring(6);
                if (data == "[DONE]")
                    break;

                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // choices[0].delta.content 추출
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
    }
}
