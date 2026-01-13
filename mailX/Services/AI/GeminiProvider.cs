using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace mailX.Services.AI
{
    /// <summary>
    /// Google Gemini API Provider
    /// </summary>
    public class GeminiProvider : AIProviderBase
    {
        private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
        private const string DefaultModel = "gemini-1.5-pro";

        public override string ProviderName => "Gemini";

        public GeminiProvider()
        {
            _baseUrl = DefaultBaseUrl;
            _model = DefaultModel;
        }

        protected override void ConfigureHttpClient()
        {
            // Gemini는 URL 파라미터로 API 키를 전달하므로 헤더 설정 불필요
            _httpClient.DefaultRequestHeaders.Clear();
        }

        private string GetApiUrl(bool stream = false)
        {
            var action = stream ? "streamGenerateContent" : "generateContent";
            return $"{_baseUrl}/{_model}:{action}?key={_apiKey}";
        }

        public override async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 4096
                }
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.Debug("Gemini API 요청: Model={Model}", _model);

            var response = await _httpClient.PostAsync(GetApiUrl(), content, ct);
            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponseAsync(response);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // candidates[0].content.parts[0].text 추출
            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString() ?? string.Empty;
                    }
                }
            }

            _logger.Warning("Gemini 응답에서 텍스트를 찾을 수 없음: {Response}", responseJson);
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
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 4096
                }
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, GetApiUrl(stream: true))
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

            var buffer = new StringBuilder();

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                {
                    // 빈 줄은 청크 구분자
                    if (buffer.Length > 0)
                    {
                        var text = ParseGeminiChunk(buffer.ToString());
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                        buffer.Clear();
                    }
                    continue;
                }

                buffer.AppendLine(line);
            }

            // 마지막 청크 처리
            if (buffer.Length > 0)
            {
                var text = ParseGeminiChunk(buffer.ToString());
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }

        private string ParseGeminiChunk(string chunk)
        {
            try
            {
                // Gemini 스트리밍은 JSON 배열로 래핑됨
                var trimmed = chunk.Trim();
                if (trimmed.StartsWith("["))
                {
                    trimmed = trimmed.TrimStart('[').TrimEnd(']');
                }
                if (trimmed.StartsWith(","))
                {
                    trimmed = trimmed.TrimStart(',');
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                    return string.Empty;

                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var firstPart = parts[0];
                        if (firstPart.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // 파싱 실패는 무시
            }

            return string.Empty;
        }
    }
}
