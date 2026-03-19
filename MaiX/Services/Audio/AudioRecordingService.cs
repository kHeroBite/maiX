using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MaiX.Utils;

namespace MaiX.Services.Audio;

/// <summary>
/// 오디오 녹음 서비스 (NAudio 사용)
/// </summary>
public class AudioRecordingService : IDisposable
{
    private IWaveIn? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentFilePath;
    private bool _isRecording;
    private bool _isPaused;
    private DateTime _recordingStartTime;
    private TimeSpan _pausedDuration;
    private DateTime _pauseStartTime;

    // WasapiNative 직접 캡처용 필드
    private IntPtr _nativeAudioClient = IntPtr.Zero;
    private CancellationTokenSource? _nativeCaptureCts;
    private Task? _nativeCaptureTask;

    // 캡처 장치 원본 포맷 (float/PCM 판별용)
    private WaveFormat? _captureFormat;
    // 파일 저장용 포맷 (16kHz 16bit mono — STT 최적)
    private static readonly WaveFormat _outputFormat = new(16000, 16, 1);

    // 실시간 STT용 버퍼
    private List<byte> _realtimeBuffer = new();
    private int _realtimeChunkSeconds = 15; // 15초 단위 청크
    private bool _realtimeEnabled = false;
    private int _totalBytesProcessed = 0;
    private readonly object _bufferLock = new();

    /// <summary>
    /// 녹음 중 여부
    /// </summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// 일시정지 여부
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// 현재 녹음 파일 경로
    /// </summary>
    public string? CurrentFilePath => _currentFilePath;

    /// <summary>
    /// 녹음 경과 시간
    /// </summary>
    public TimeSpan RecordingDuration
    {
        get
        {
            if (!_isRecording) return TimeSpan.Zero;
            var elapsed = DateTime.Now - _recordingStartTime - _pausedDuration;
            if (_isPaused)
            {
                elapsed -= (DateTime.Now - _pauseStartTime);
            }
            return elapsed;
        }
    }

    /// <summary>
    /// 볼륨 레벨 변경 이벤트 (0.0 ~ 1.0)
    /// </summary>
    public event Action<float>? VolumeChanged;

    /// <summary>
    /// 녹음 시간 변경 이벤트
    /// </summary>
    public event Action<TimeSpan>? DurationChanged;

    /// <summary>
    /// 녹음 완료 이벤트
    /// </summary>
    public event Action<string>? RecordingCompleted;

    /// <summary>
    /// 녹음 오류 이벤트
    /// </summary>
    public event Action<string>? RecordingError;

    /// <summary>
    /// 실시간 오디오 청크 준비 이벤트 (byte[] audioData, TimeSpan chunkStartTime)
    /// 녹음 중 지정된 시간(기본 15초)마다 발생
    /// </summary>
    public event Action<byte[], TimeSpan>? RealtimeAudioChunkReady;

    /// <summary>
    /// 실시간 STT 활성화 여부
    /// </summary>
    public bool RealtimeEnabled
    {
        get => _realtimeEnabled;
        set => _realtimeEnabled = value;
    }

    /// <summary>
    /// 실시간 청크 간격 (초 단위, 기본 15초)
    /// </summary>
    public int RealtimeChunkSeconds
    {
        get => _realtimeChunkSeconds;
        set => _realtimeChunkSeconds = Math.Max(5, Math.Min(60, value)); // 5~60초 범위
    }

    /// <summary>
    /// 녹음 파일 저장 디렉토리
    /// </summary>
    public static string RecordingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MaiX", "recordings");

    /// <summary>
    /// 녹음 시작
    /// </summary>
    /// <param name="pageId">연결할 OneNote 페이지 ID (선택)</param>
    /// <returns>녹음 파일 경로</returns>
    public async Task<string> StartRecordingAsync(string? pageId = null, string? preferredDeviceId = null)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException("이미 녹음 중입니다.");
        }

        try
        {
            // 저장 디렉토리 생성
            if (!Directory.Exists(RecordingsDirectory))
            {
                Directory.CreateDirectory(RecordingsDirectory);
            }

            // 파일명 생성: recording_{pageId}_{timestamp}.wav 또는 recording_{timestamp}.wav
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = string.IsNullOrEmpty(pageId)
                ? $"recording_{timestamp}.wav"
                : $"recording_{SanitizePageId(pageId)}_{timestamp}.wav";

            _currentFilePath = Path.Combine(RecordingsDirectory, fileName);

            // WasapiCapture → WaveInEvent 2단계 fallback 체인
            // 1단계: WasapiCapture 다중 장치 + 다단계 버퍼 재시도
            bool startSuccess = false;
            var enumerator = new MMDeviceEnumerator();

            // 시도할 장치 목록 구성 (중복 제거)
            var devicesToTry = new List<MMDevice>();
            var addedIds = new HashSet<string>();

            // 0순위: 사용자 선택 장치
            if (!string.IsNullOrEmpty(preferredDeviceId))
            {
                try
                {
                    var allDevs = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                    var preferred = allDevs.FirstOrDefault(d => d.ID == preferredDeviceId);
                    if (preferred != null && addedIds.Add(preferred.ID))
                    {
                        devicesToTry.Insert(0, preferred);
                        Log4.Info($"[녹음] 선택 마이크 0순위 추가: {preferred.FriendlyName}");
                    }
                }
                catch (Exception ex) { Log4.Warn($"[녹음] 선택 마이크 장치 획득 실패: {ex.Message}"); }
            }

            // 1순위: Communications Role
            try
            {
                var commDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                if (addedIds.Add(commDevice.ID)) devicesToTry.Add(commDevice);
            }
            catch (Exception ex) { Log4.Warn($"[녹음] Communications 장치 획득 실패: {ex.Message}"); }

            // 2순위: Multimedia Role (Communications와 다를 경우만)
            try
            {
                var multimediaDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                if (addedIds.Add(multimediaDevice.ID)) devicesToTry.Add(multimediaDevice);
            }
            catch (Exception ex) { Log4.Warn($"[녹음] Multimedia 장치 획득 실패: {ex.Message}"); }

            // 3순위: 모든 활성 캡처 장치 순회
            try
            {
                var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var dev in allDevices)
                {
                    if (addedIds.Add(dev.ID)) devicesToTry.Add(dev);
                }
            }
            catch (Exception ex) { Log4.Warn($"[녹음] 장치 열거 실패: {ex.Message}"); }

            Log4.Info($"[녹음] 캡처 장치 {devicesToTry.Count}개 탐색 시작");

            // 1단계: NAudio AudioClient 직접 사용 (streamFlags=0 — WasapiCapture의 AUTOCONVERTPCM 우회)
            // PowerShell 검증: device.AudioClient.Initialize(Shared, streamFlags=0, 0, 0, mixFormat, Guid.Empty) → 성공
            foreach (var device in devicesToTry)
            {
                Log4.Info($"[녹음] 장치 시도 (AudioClient 직접): {device.FriendlyName}");
                try
                {
                    var audioClient = device.AudioClient;
                    var mixFmt = audioClient.MixFormat;
                    Log4.Info($"[녹음] MixFormat: {mixFmt.SampleRate}Hz, {mixFmt.BitsPerSample}bit, {mixFmt.Channels}ch, {mixFmt.Encoding}");

                    // streamFlags=0 (AUTOCONVERTPCM 없음) — Intel SST 호환
                    audioClient.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.None, 0, 0, mixFmt, Guid.Empty);
                    Log4.Info("[녹음] AudioClient.Initialize 성공");

                    var captureClient = audioClient.AudioCaptureClient;
                    audioClient.Start();
                    Log4.Info("[녹음] AudioClient.Start 성공");

                    _captureFormat = mixFmt;
                    _writer = new WaveFileWriter(_currentFilePath, _outputFormat);
                    _nativeCaptureCts = new CancellationTokenSource();
                    var token = _nativeCaptureCts.Token;

                    _nativeCaptureTask = Task.Run(() =>
                    {
                        Log4.Info("[녹음] AudioClient 직접 캡처 루프 시작");
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                int nextPacketSize = captureClient.GetNextPacketSize();
                                if (nextPacketSize == 0) { Thread.Sleep(10); continue; }

                                nint bufPtr = captureClient.GetBuffer(out int numFrames, out AudioClientBufferFlags flags);
                                if (numFrames > 0 && bufPtr != 0 &&
                                    !flags.HasFlag(AudioClientBufferFlags.Silent) &&
                                    _captureFormat != null)
                                {
                                    int byteCount = numFrames * _captureFormat.BlockAlign;
                                    var raw = new byte[byteCount];
                                    System.Runtime.InteropServices.Marshal.Copy(bufPtr, raw, 0, byteCount);
                                    OnDataAvailable(null, new WaveInEventArgs(raw, byteCount));
                                }
                                captureClient.ReleaseBuffer(numFrames);
                            }
                        }
                        catch (Exception ex) { Log4.Error($"[녹음] AudioClient 캡처 루프 오류: {ex.Message}"); }
                        finally { try { audioClient.Stop(); } catch { } Log4.Info("[녹음] AudioClient 캡처 루프 종료"); }
                    }, token);

                    startSuccess = true;
                    Log4.Info($"[녹음] AudioClient 직접 캡처 시작 성공 — {device.FriendlyName}");
                    break;
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[녹음] AudioClient 직접 초기화 실패 — {device.FriendlyName}: {ex.Message} (HR=0x{ex.HResult:X8})");
                    _captureFormat = null;
                    if (_writer != null) { try { _writer.Dispose(); } catch { } _writer = null; }
                }
            }

            // 2단계: WasapiNative 직접 P/Invoke (NAudio streamFlags 우회 — Intel SST 호환)
            if (!startSuccess)
            {
                Log4.Warn("[녹음] NAudio WASAPI 전체 실패 — WasapiNative 직접 초기화 시도");
                try
                {
                    var firstDevice = devicesToTry.FirstOrDefault();
                    if (firstDevice != null)
                    {
                        var pAudioClient = WasapiNative.ActivateAudioClient(firstDevice);
                        Log4.Info($"[녹음] WasapiNative: IAudioClient 획득 0x{pAudioClient:X}");

                        int hr = WasapiNative.InitializeWithMixFormat(pAudioClient, out long usedDuration);
                        Log4.Info($"[녹음] WasapiNative: Initialize HR=0x{hr:X8}, duration={usedDuration}");

                        if (hr == 0)
                        {
                            // GetMixFormat으로 포맷 확인
                            hr = WasapiNative.AudioClientGetMixFormat(pAudioClient, out IntPtr pWfx);
                            if (hr == 0 && pWfx != IntPtr.Zero)
                            {
                                _captureFormat = System.Runtime.InteropServices.Marshal.PtrToStructure<NAudio.Wave.WaveFormat>(pWfx);
                                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pWfx);
                                Log4.Info($"[녹음] WasapiNative 포맷: {_captureFormat.SampleRate}Hz, {_captureFormat.BitsPerSample}bit, {_captureFormat.Channels}ch");
                            }
                            else
                            {
                                _captureFormat = new NAudio.Wave.WaveFormat(48000, 32, 2);
                            }

                            // NAudio WasapiCapture 내부 상태 우회 — 커스텀 캡처 루프 사용
                            _nativeAudioClient = pAudioClient;
                            startSuccess = StartNativeCaptureLoop(pAudioClient);
                        }
                        else
                        {
                            Log4.Warn($"[녹음] WasapiNative Initialize 실패: 0x{hr:X8}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[녹음] WasapiNative 직접 초기화 실패: {ex.Message}");
                }
            }

            // 3단계: WaveInEvent fallback (MME API — WASAPI 우회)
            if (!startSuccess)
            {
                Log4.Warn("[녹음] WasapiNative 실패 — WaveInEvent(MME) fallback 시도");
                _captureFormat = GetBestWaveFormat(0);
                Log4.Info($"[녹음] WaveInEvent 포맷: {_captureFormat.SampleRate}Hz, {_captureFormat.BitsPerSample}bit, {_captureFormat.Channels}ch");
                _waveIn = new WaveInEvent { DeviceNumber = 0, WaveFormat = _captureFormat };
                _writer = new WaveFileWriter(_currentFilePath, _outputFormat);
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                Log4.Info("[녹음] WaveInEvent 시작 성공");
            }
            _isRecording = true;
            _isPaused = false;
            _recordingStartTime = DateTime.Now;
            _pausedDuration = TimeSpan.Zero;

            Log4.Info($"[녹음] 녹음 시작: {_currentFilePath}");
            return _currentFilePath;
        }
        catch (Exception ex)
        {
            Log4.Error($"[녹음] 녹음 시작 실패: {ex.Message}");
            Cleanup();
            throw;
        }
    }

    /// <summary>
    /// 녹음 중지
    /// </summary>
    /// <returns>저장된 파일 경로</returns>
    public string? StopRecording()
    {
        if (!_isRecording)
        {
            return null;
        }

        try
        {
            _waveIn?.StopRecording();
            return _currentFilePath;
        }
        catch (Exception ex)
        {
            Log4.Error($"[녹음] 녹음 중지 실패: {ex.Message}");
            Cleanup();
            throw;
        }
    }

    /// <summary>
    /// 녹음 일시정지
    /// </summary>
    public void PauseRecording()
    {
        if (!_isRecording || _isPaused) return;

        _isPaused = true;
        _pauseStartTime = DateTime.Now;
        Log4.Debug("[녹음] 녹음 일시정지");
    }

    /// <summary>
    /// 녹음 재개
    /// </summary>
    public void ResumeRecording()
    {
        if (!_isRecording || !_isPaused) return;

        _pausedDuration += DateTime.Now - _pauseStartTime;
        _isPaused = false;
        Log4.Debug("[녹음] 녹음 재개");
    }

    /// <summary>
    /// 녹음 취소 (파일 삭제)
    /// </summary>
    public void CancelRecording()
    {
        if (!_isRecording) return;

        var filePath = _currentFilePath;

        try
        {
            _waveIn?.StopRecording();
        }
        catch { }

        Cleanup();

        // 파일 삭제
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                Log4.Info($"[녹음] 녹음 취소됨, 파일 삭제: {filePath}");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[녹음] 녹음 파일 삭제 실패: {filePath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 사용 가능한 녹음 장치 목록 가져오기
    /// </summary>
    public static List<string> GetAvailableDevices()
    {
        var devices = new List<string>();
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(
            NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
        {
            devices.Add(device.FriendlyName);
        }
        return devices;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_writer == null || _isPaused || _captureFormat == null) return;

        try
        {
            // 1) 캡처 데이터를 float 샘플 배열로 변환 + 볼륨 계산
            float[] floatSamples;
            float maxVolume = 0;

            if (_captureFormat.Encoding == WaveFormatEncoding.IeeeFloat && _captureFormat.BitsPerSample == 32)
            {
                // 32bit IEEE Float (WasapiCapture 기본)
                var span = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
                floatSamples = new float[span.Length];
                for (int i = 0; i < span.Length; i++)
                {
                    floatSamples[i] = span[i];
                    var abs = Math.Abs(span[i]);
                    if (abs > maxVolume) maxVolume = abs;
                }
            }
            else
            {
                // 16bit PCM → float 변환
                int sampleCount = e.BytesRecorded / 2;
                floatSamples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = (short)(e.Buffer[i * 2 + 1] << 8 | e.Buffer[i * 2]);
                    floatSamples[i] = sample / 32768f;
                    var abs = Math.Abs(floatSamples[i]);
                    if (abs > maxVolume) maxVolume = abs;
                }
            }

            VolumeChanged?.Invoke(maxVolume);
            DurationChanged?.Invoke(RecordingDuration);

            // 2) 스테레오 → mono 다운믹스 (STT는 mono 선호)
            float[] monoSamples;
            if (_captureFormat.Channels >= 2)
            {
                int channels = _captureFormat.Channels;
                int monoCount = floatSamples.Length / channels;
                monoSamples = new float[monoCount];
                for (int i = 0; i < monoCount; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < channels; ch++)
                        sum += floatSamples[i * channels + ch];
                    monoSamples[i] = sum / channels;
                }
            }
            else
            {
                monoSamples = floatSamples;
            }

            // 3) 리샘플링: 캡처 SampleRate → 16kHz (선형 보간)
            float[] resampledSamples;
            int captureSampleRate = _captureFormat.SampleRate;
            if (captureSampleRate != _outputFormat.SampleRate)
            {
                double ratio = (double)_outputFormat.SampleRate / captureSampleRate;
                int outCount = (int)(monoSamples.Length * ratio);
                resampledSamples = new float[outCount];
                for (int i = 0; i < outCount; i++)
                {
                    double srcIdx = i / ratio;
                    int idx0 = (int)srcIdx;
                    int idx1 = Math.Min(idx0 + 1, monoSamples.Length - 1);
                    float frac = (float)(srcIdx - idx0);
                    resampledSamples[i] = monoSamples[idx0] * (1f - frac) + monoSamples[idx1] * frac;
                }
            }
            else
            {
                resampledSamples = monoSamples;
            }

            // 4) float → 16bit PCM 변환
            var pcmBuffer = new byte[resampledSamples.Length * 2];
            for (int i = 0; i < resampledSamples.Length; i++)
            {
                var clamped = Math.Clamp(resampledSamples[i], -1f, 1f);
                var pcmSample = (short)(clamped * 32767);
                pcmBuffer[i * 2] = (byte)(pcmSample & 0xFF);
                pcmBuffer[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
            }

            // 5) 파일에 쓰기 (16kHz 16bit mono PCM)
            _writer.Write(pcmBuffer, 0, pcmBuffer.Length);

            // 6) 실시간 STT용 버퍼링 (변환된 PCM 데이터 사용)
            if (_realtimeEnabled)
            {
                lock (_bufferLock)
                {
                    _realtimeBuffer.AddRange(pcmBuffer);

                    // 청크 크기 계산: 16000Hz * 2bytes * 1ch = 32000 bytes/sec
                    var bytesPerSecond = _outputFormat.AverageBytesPerSecond;
                    var chunkSizeBytes = bytesPerSecond * _realtimeChunkSeconds;

                    if (_realtimeBuffer.Count >= chunkSizeBytes)
                    {
                        var chunkData = _realtimeBuffer.ToArray();
                        var chunkStartTime = TimeSpan.FromSeconds((double)_totalBytesProcessed / bytesPerSecond);

                        _totalBytesProcessed += _realtimeBuffer.Count;
                        _realtimeBuffer.Clear();

                        Log4.Debug($"[녹음] 실시간 청크 준비: {chunkStartTime}, {chunkData.Length} bytes");
                        RealtimeAudioChunkReady?.Invoke(chunkData, chunkStartTime);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[녹음] 녹음 데이터 처리 실패: {ex.Message}");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Log4.Info($"[AudioRecording] ★ OnRecordingStopped 진입");
        var filePath = _currentFilePath;
        Log4.Info($"[AudioRecording] ★ 파일 경로: {filePath ?? "null"}");

        // 실시간 모드에서 남은 버퍼 처리 (변환된 PCM 기준)
        if (_realtimeEnabled)
        {
            lock (_bufferLock)
            {
                if (_realtimeBuffer.Count > 0)
                {
                    var chunkData = _realtimeBuffer.ToArray();
                    var bytesPerSecond = _outputFormat.AverageBytesPerSecond;
                    var chunkStartTime = TimeSpan.FromSeconds((double)_totalBytesProcessed / bytesPerSecond);

                    Log4.Debug($"[녹음] 실시간 최종 청크 처리: {chunkStartTime}, {chunkData.Length} bytes");
                    RealtimeAudioChunkReady?.Invoke(chunkData, chunkStartTime);
                }
            }
        }

        Cleanup();

        if (e.Exception != null)
        {
            Log4.Error($"[AudioRecording] ★ 녹음 중 오류 발생: {e.Exception.Message}");
            Log4.Error($"[녹음] 녹음 중 오류 발생: {e.Exception.Message}");
            RecordingError?.Invoke(e.Exception.Message);
        }
        else if (!string.IsNullOrEmpty(filePath))
        {
            Log4.Info($"[AudioRecording] ★ 녹음 완료, RecordingCompleted 이벤트 발생 전");
            Log4.Info($"[AudioRecording] ★ RecordingCompleted 핸들러 수: {RecordingCompleted?.GetInvocationList().Length ?? 0}");
            Log4.Info($"[녹음] 녹음 완료: {filePath}");
            RecordingCompleted?.Invoke(filePath);
            Log4.Info($"[AudioRecording] ★ RecordingCompleted 이벤트 발생 완료");
        }
        else
        {
            Log4.Warn($"[AudioRecording] ★ 파일 경로가 비어있음, 이벤트 발생 안 함");
        }
    }

    /// <summary>
    /// WasapiNative IAudioClient로 캡처 루프 시작 (NAudio 우회)
    /// </summary>
    private bool StartNativeCaptureLoop(IntPtr pAudioClient)
    {
        try
        {
            var iidCapture = WasapiNative.IID_IAudioCaptureClient;
            int hr = WasapiNative.AudioClientGetService(pAudioClient, ref iidCapture, out IntPtr pCaptureClient);
            if (hr != 0) { Log4.Warn($"[녹음] WasapiNative: GetService 실패 0x{hr:X8}"); return false; }

            hr = WasapiNative.AudioClientStart(pAudioClient);
            if (hr != 0) { Log4.Warn($"[녹음] WasapiNative: AudioClientStart 실패 0x{hr:X8}"); return false; }

            Log4.Info("[녹음] WasapiNative: 캡처 시작 성공");
            _writer = new WaveFileWriter(_currentFilePath!, _outputFormat);
            _nativeCaptureCts = new CancellationTokenSource();
            var token = _nativeCaptureCts.Token;

            _nativeCaptureTask = Task.Run(() =>
            {
                Log4.Info("[녹음] WasapiNative: 캡처 루프 시작");
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        int hrNext = WasapiNative.CaptureClientGetNextPacketSize(pCaptureClient, out uint packetSize);
                        if (hrNext != 0 || packetSize == 0) { Thread.Sleep(10); continue; }

                        int hrBuf = WasapiNative.CaptureClientGetBuffer(pCaptureClient,
                            out IntPtr pData, out uint numFrames, out uint flags, out _, out _);
                        if (hrBuf != 0) { Thread.Sleep(5); continue; }

                        if (numFrames > 0 && pData != IntPtr.Zero &&
                            (flags & WasapiNative.AUDCLNT_BUFFERFLAGS_SILENT) == 0 &&
                            _captureFormat != null)
                        {
                            int byteCount = (int)numFrames * _captureFormat.BlockAlign;
                            var raw = new byte[byteCount];
                            System.Runtime.InteropServices.Marshal.Copy(pData, raw, 0, byteCount);
                            OnDataAvailable(null, new WaveInEventArgs(raw, byteCount));
                        }
                        WasapiNative.CaptureClientReleaseBuffer(pCaptureClient, numFrames);
                    }
                }
                catch (Exception ex) { Log4.Error($"[녹음] WasapiNative: 캡처 루프 오류: {ex.Message}"); }
                finally
                {
                    WasapiNative.AudioClientStop(pAudioClient);
                    Log4.Info("[녹음] WasapiNative: 캡처 루프 종료");
                }
            }, token);

            return true;
        }
        catch (Exception ex) { Log4.Error($"[녹음] WasapiNative: StartNativeCaptureLoop 실패: {ex.Message}"); return false; }
    }

    private void Cleanup()
    {
        _isRecording = false;
        _isPaused = false;

        // WasapiNative 캡처 루프 정리
        if (_nativeCaptureCts != null)
        {
            _nativeCaptureCts.Cancel();
            try { _nativeCaptureTask?.Wait(2000); } catch { }
            _nativeCaptureCts.Dispose();
            _nativeCaptureCts = null;
            _nativeCaptureTask = null;
        }
        if (_nativeAudioClient != IntPtr.Zero)
        {
            try { unsafe { var v = *(IntPtr**)_nativeAudioClient; ((delegate* unmanaged[Stdcall]<IntPtr, uint>)v[2])(_nativeAudioClient); } } catch { }
            _nativeAudioClient = IntPtr.Zero;
        }

        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _captureFormat = null;

        if (_writer != null)
        {
            _writer.Dispose();
            _writer = null;
        }

        _currentFilePath = null;

        // 실시간 버퍼 초기화
        lock (_bufferLock)
        {
            _realtimeBuffer.Clear();
            _totalBytesProcessed = 0;
        }
    }

    /// <summary>
    /// 마이크 장치가 지원하는 최적 WaveFormat 자동 감지
    /// </summary>
    /// <param name="deviceNumber">마이크 장치 번호</param>
    /// <returns>지원되는 WaveFormat (기본: 44100Hz, 16bit, Mono)</returns>
    private WaveFormat GetBestWaveFormat(int deviceNumber)
    {
        var candidateRates = new[] { 44100, 48000, 22050, 11025, 8000 };
        var candidateChannels = new[] { 1, 2 };

        try
        {
            var capabilities = WaveInEvent.GetCapabilities(deviceNumber);

            foreach (var rate in candidateRates)
            {
                foreach (var channels in candidateChannels)
                {
                    if (IsSupportedFormat(capabilities, rate, channels))
                    {
                        Log4.Debug($"[녹음] 지원 포맷 감지: {rate}Hz, {channels}ch");
                        return new WaveFormat(rate, 16, channels);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[녹음] WaveFormat 자동감지 실패, 기본값(44100Hz, Mono) 사용: {ex.Message}");
        }

        return new WaveFormat(44100, 16, 1);
    }

    /// <summary>
    /// WaveInCapabilities로 특정 SampleRate/Channels 조합 지원 여부 확인
    /// </summary>
    private static bool IsSupportedFormat(WaveInCapabilities capabilities, int sampleRate, int channels)
    {
        // NAudio SupportedWaveFormat 열거형 매핑
        var formatMap = new Dictionary<(int rate, int ch), SupportedWaveFormat>
        {
            { (8000, 1), SupportedWaveFormat.WAVE_FORMAT_1M16 },   // 근사 매핑
            { (8000, 2), SupportedWaveFormat.WAVE_FORMAT_1S16 },
            { (11025, 1), SupportedWaveFormat.WAVE_FORMAT_1M16 },
            { (11025, 2), SupportedWaveFormat.WAVE_FORMAT_1S16 },
            { (22050, 1), SupportedWaveFormat.WAVE_FORMAT_2M16 },
            { (22050, 2), SupportedWaveFormat.WAVE_FORMAT_2S16 },
            { (44100, 1), SupportedWaveFormat.WAVE_FORMAT_4M16 },
            { (44100, 2), SupportedWaveFormat.WAVE_FORMAT_4S16 },
            { (48000, 1), SupportedWaveFormat.WAVE_FORMAT_48M16 },
            { (48000, 2), SupportedWaveFormat.WAVE_FORMAT_48S16 },
        };

        // 매핑에 있는 포맷은 정확히 확인
        if (formatMap.TryGetValue((sampleRate, channels), out var supportedFormat))
        {
            return capabilities.SupportsWaveFormat(supportedFormat);
        }

        // formatMap에 없는 포맷은 지원 불가로 판정 (WaveInEvent 생성만으로는 waveInOpen 미호출되어 검증 불가)
        return false;
    }

    /// <summary>
    /// 페이지 ID를 파일명에 사용 가능하도록 정리
    /// </summary>
    private static string SanitizePageId(string pageId)
    {
        // 특수문자 제거 및 길이 제한
        var sanitized = string.Join("", pageId.Split(Path.GetInvalidFileNameChars()));
        if (sanitized.Length > 20)
        {
            sanitized = sanitized.Substring(0, 20);
        }
        return sanitized;
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            try
            {
                StopRecording();
            }
            catch { }
        }
        Cleanup();
    }
}
