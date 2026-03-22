using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MaiX.Models;

namespace MaiX.Services.Speech;

/// <summary>
/// Jarvis 음성 서버 HTTP 클라이언트
/// STT/TTS/화자분리를 서버에서 처리
/// </summary>
public class ServerSpeechService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private bool _disposed;

    public ServerSpeechService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    /// <summary>서버 연결 테스트</summary>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>서버 STT: WAV 파일 → TranscriptResult</summary>
    public async Task<TranscriptResult> TranscribeFileAsync(string audioFilePath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(audioFilePath, ct);
        form.Add(new ByteArrayContent(fileBytes), "audio", Path.GetFileName(audioFilePath));

        var resp = await _http.PostAsync($"{_baseUrl}/api/stt", form, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<SttResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("STT 서버 응답 파싱 실패");

        var segment = new TranscriptSegment
        {
            Text = json.Text,
            Speaker = "화자 1",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.Zero,
        };

        return new TranscriptResult
        {
            AudioFilePath = audioFilePath,
            Segments = new List<TranscriptSegment> { segment },
        };
    }

    /// <summary>서버 화자분리: WAV 파일 → 세그먼트별 화자 레이블</summary>
    public async Task<List<(TimeSpan Start, TimeSpan End, string Speaker)>> DiarizeAsync(
        string audioFilePath, int numSpeakers = 0, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(audioFilePath, ct);
        form.Add(new ByteArrayContent(fileBytes), "audio", Path.GetFileName(audioFilePath));
        form.Add(new StringContent(numSpeakers.ToString()), "num_speakers");

        var resp = await _http.PostAsync($"{_baseUrl}/api/diarize", form, ct);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<DiarizeResult>(cancellationToken: ct);
        if (result?.Segments == null) return new List<(TimeSpan, TimeSpan, string)>();

        return result.Segments.Select(s => (
            TimeSpan.FromSeconds(s.Start),
            TimeSpan.FromSeconds(s.End),
            s.Speaker
        )).ToList();
    }

    /// <summary>서버 TTS: 텍스트 → WAV bytes (비스트리밍 preview)</summary>
    public async Task<byte[]> SynthesizeAsync(string text, int speakerId = 0, CancellationToken ct = default)
    {
        var req = new { text, speaker_id = speakerId, engine = "vits2" };
        var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/tts/preview", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }

    // 응답 역직렬화용 내부 레코드
    private record SttResult(string Text, string Language, float Confidence, bool IsFinal);
    private record DiarizeSegment(float Start, float End, string Speaker);
    private record DiarizeResult(List<DiarizeSegment> Segments, int SpeakerCount);
}
