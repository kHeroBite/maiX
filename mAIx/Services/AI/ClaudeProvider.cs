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
    /// Anthropic Claude API Provider
    /// </summary>
    public class ClaudeProvider : AIProviderBase
    {
        private const string DefaultBaseUrl = "https://api.anthropic.com/v1/messages";
        private const string DefaultModel = "claude-sonnet-4-20250514";
        private const string AnthropicVersion = "2023-06-01";

        public override string ProviderName => "Claude";

        public ClaudeProvider()
        {
            _baseUrl = DefaultBaseUrl;
            _model = DefaultModel;
        }

        protected override void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        }

        public override async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
        {
            var requestBody = new
            {
                model = _model,
                max_tokens = 4096,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Debug("Claude API 요청: Model={Model}", _model);

            var response = await _httpClient.PostAsync(_baseUrl, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response).ConfigureAwait(false);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // content[0].text 추출
            if (root.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }
            }

            _logger.Warning("Claude 응답에서 텍스트를 찾을 수 없음: {Response}", responseJson);
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
                max_tokens = 4096,
                stream = true,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
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

                // content_block_delta 이벤트에서 텍스트 추출
                if (root.TryGetProperty("type", out var typeElement) &&
                    typeElement.GetString() == "content_block_delta")
                {
                    if (root.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var textElement))
                    {
                        var text = textElement.GetString();
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
