using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Models.Settings;

namespace mAIx.Services.Speech;

/// <summary>
/// Jarvis 음성 서버 HTTP 클라이언트
/// STT/TTS/화자분리를 서버에서 처리
/// </summary>
public class ServerSpeechService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly UserPreferencesSettings? _prefs;
    private bool _disposed;

    public ServerSpeechService(string baseUrl, UserPreferencesSettings? prefs = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _prefs = prefs;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    /// <summary>서버 연결 테스트</summary>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}{_prefs?.EndpointHealth ?? "/health"}", ct);
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

        var resp = await _http.PostAsync($"{_baseUrl}{_prefs?.EndpointStt ?? "/api/stt"}", form, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<SttResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("STT 서버 응답 파싱 실패");

        // 서버가 segments 배열 반환 시 시간 정보 포함 파싱 (우선)
        if (json.Segments != null && json.Segments.Count > 0)
        {
            return new TranscriptResult
            {
                AudioFilePath = audioFilePath,
                Segments = json.Segments.Select(s => new TranscriptSegment
                {
                    Text = s.Text,
                    Speaker = string.IsNullOrEmpty(s.Speaker) ? "화자 1" : s.Speaker,
                    StartTime = TimeSpan.FromSeconds(s.Start),
                    EndTime = TimeSpan.FromSeconds(s.End),
                    Confidence = s.Confidence,
                }).ToList(),
            };
        }

        // 폴백: 단일 텍스트 세그먼트
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

        var resp = await _http.PostAsync($"{_baseUrl}{_prefs?.EndpointDiarize ?? "/api/diarize"}", form, ct);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<DiarizeResult>(cancellationToken: ct);
        if (result?.Segments == null) return new List<(TimeSpan, TimeSpan, string)>();

        return result.Segments.Select(s => (
            TimeSpan.FromSeconds(s.Start),
            TimeSpan.FromSeconds(s.End),
            s.Speaker
        )).ToList();
    }

    /// <summary>서버 TTS: 텍스트 → WAV bytes</summary>
    public async Task<byte[]> SynthesizeAsync(string text, int speakerId = 0, CancellationToken ct = default)
    {
        var req = new { text, speaker_id = speakerId, engine = "vits2" };
        var resp = await _http.PostAsJsonAsync($"{_baseUrl}{_prefs?.EndpointTts ?? "/api/tts/preview"}", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>서버 STT 모델 목록 조회</summary>
    public async Task<(List<string> Models, string? Active)> GetSttModelsAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{_baseUrl}{_prefs?.EndpointSttModels ?? "/api/stt/models"}", ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<SttModelsResponse>(cancellationToken: ct);
        return (json?.Models ?? new(), json?.Active);
    }

    /// <summary>서버 TTS 화자 목록 조회</summary>
    public async Task<List<TtsSpeakerInfo>> GetTtsSpeakersAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{_baseUrl}{_prefs?.EndpointTtsSpeakers ?? "/api/tts/speakers"}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<TtsSpeakerInfo>>(cancellationToken: ct) ?? new();
    }

    /// <summary>통합 모델 상태 조회 (STT/TTS/VAD)</summary>
    public async Task<FullModelStatusResponse?> GetFullModelStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{_baseUrl}{_prefs?.EndpointModelsFullStatus ?? "/api/models/full-status"}", ct);
        resp.EnsureSuccessStatusCode();
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return await resp.Content.ReadFromJsonAsync<FullModelStatusResponse>(opts, ct);
    }

    /// <summary>TTS 엔진 목록 상세 조회</summary>
    public async Task<TtsEnginesResponse?> GetTtsEnginesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{_baseUrl}{_prefs?.EndpointTtsEngines ?? "/api/tts/engines"}", ct);
        resp.EnsureSuccessStatusCode();
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return await resp.Content.ReadFromJsonAsync<TtsEnginesResponse>(opts, ct);
    }

    /// <summary>오디오 지원 포맷/샘플레이트/채널 조회</summary>
    public async Task<AudioCapabilitiesResponse?> GetAudioCapabilitiesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{_baseUrl}{_prefs?.EndpointAudioCapabilities ?? "/api/audio/capabilities"}", ct);
        resp.EnsureSuccessStatusCode();
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return await resp.Content.ReadFromJsonAsync<AudioCapabilitiesResponse>(opts, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }

    // 응답 역직렬화용 내부 레코드
    private record SttSegment(string Text, float Start, float End, string Speaker, float Confidence);
    private record SttResult(string Text, string Language, float Confidence, bool IsFinal, List<SttSegment>? Segments);
    private record DiarizeSegment(float Start, float End, string Speaker);
    private record DiarizeResult(List<DiarizeSegment> Segments, int SpeakerCount);
    private record SttModelsResponse(List<string> Models, string? Active);
}

/// <summary>TTS 화자 정보</summary>
public record TtsSpeakerInfo(int Id, string Name, string Language, string Engine);

// FullModelStatus DTO
public record SttStatusInfo(string CurrentModel, List<string> AvailableModels, string Engine);
public record TtsStatusInfo(string CurrentEngine, List<string> ReadyEngines, List<string> AvailableEngines);
public record VadStatusInfo(string CurrentModel, List<string> AvailableModels);
public record FullModelStatusResponse(SttStatusInfo Stt, TtsStatusInfo Tts, VadStatusInfo Vad);

// TtsEngines DTO
public record TtsEngineDetail(string Name, string Type, string Device, bool Ready);
public record TtsEnginesResponse(List<string> Engines, List<string> ReadyEngines, string Active,
    Dictionary<string, TtsEngineDetail> Details);

// AudioCapabilities DTO
public record AudioCapabilitiesResponse(
    List<string> SupportedFormats,
    List<string> SupportedSampleRates,
    List<string> SupportedChannels);
