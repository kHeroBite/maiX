using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using mAIx.Utils;

namespace mAIx.Services.Audio;

/// <summary>
/// 마이크 장치 정보
/// </summary>
public record AudioDeviceInfo(string DeviceId, string FriendlyName, bool IsDefault);

/// <summary>
/// 마이크 테스트 전용 서비스 (장치 열거, 실시간 모니터링, 테스트 녹음/재생, 볼륨 조절)
/// </summary>
public class MicrophoneTestService : IDisposable
{
    private IWaveIn? _capture;
    private WaveFileWriter? _waveWriter;
    private WaveOutEvent? _playbackDevice;
    private AudioFileReader? _audioFileReader;
    private string? _testRecordingPath;
    private bool _isMonitoring;
    private bool _isRecording;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private WaveFormat? _captureFormat;

    /// <summary>
    /// 볼륨 레벨 변경 이벤트 (0.0 ~ 1.0)
    /// </summary>
    public event Action<float>? VolumeLevelChanged;

    /// <summary>
    /// 데시벨 레벨 변경 이벤트 (dB값, 최소 -60dB)
    /// </summary>
    public event Action<float>? DecibelLevelChanged;

    /// <summary>
    /// 테스트 녹음 완료 이벤트 (파일 경로)
    /// </summary>
    public event Action<string>? TestRecordingCompleted;

    /// <summary>
    /// 테스트 재생 완료 이벤트
    /// </summary>
    public event Action? TestPlaybackCompleted;

    /// <summary>
    /// 모니터링 중 여부
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// 녹음 중 여부
    /// </summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// 사용 가능한 마이크 장치 목록 반환
    /// </summary>
    public static List<AudioDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            MMDevice? defaultDevice = null;
            try
            {
                defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
            catch { }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                bool isDefault = defaultDevice != null && device.ID == defaultDevice.ID;
                devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, isDefault));
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[마이크테스트] 장치 열거 실패: {ex.Message}");
        }
        return devices;
    }

    /// <summary>
    /// 지정한 장치로 실시간 모니터링 시작 (WASAPI)
    /// </summary>
    public void StartMonitoring(string deviceId)
    {
        StopMonitoring();

        if (!TryStartWasapiMonitoring(deviceId))
            Log4.Error("[마이크테스트] WasapiCapture 모니터링 시작 실패");
    }

    private bool TryStartWasapiMonitoring(string deviceId)
    {
        // MixFormat을 별도 인스턴스에서 읽기 (NAudio AudioClient 싱글톤 캐시 오염 방지)
        WaveFormat? mixFmt = null;
        try
        {
            using var fmtEnum = new MMDeviceEnumerator();
            var fmtDevice = fmtEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                    .FirstOrDefault(d => d.ID == deviceId);
            if (fmtDevice != null) mixFmt = fmtDevice.AudioClient.MixFormat;
        }
        catch { }

        // useEventSync false → true 순서로 WasapiCapture 시도 (NAudio 내부 Initialize 위임)
        foreach (var useEvent in new[] { false, true })
        {
            try
            {
                // WasapiCapture에 전달할 freshDevice — AudioClient 미사용 상태의 새 인스턴스
                var freshEnum = new MMDeviceEnumerator();
                var freshDevice = freshEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                           .FirstOrDefault(d => d.ID == deviceId);
                if (freshDevice == null)
                {
                    Log4.Warn($"[마이크테스트] 모니터링 장치를 찾을 수 없음: {deviceId}");
                    return false;
                }

                var wasapi = new WasapiCapture(freshDevice, useEvent, 100);
                // MixFormat 주입 (드라이버 네이티브 포맷 강제 — 0x80070057 방지)
                if (mixFmt != null) wasapi.WaveFormat = mixFmt;
                Log4.Info($"[마이크테스트] WasapiCapture 시도 (useEvent={useEvent}): {wasapi.WaveFormat.SampleRate}Hz {wasapi.WaveFormat.BitsPerSample}bit {wasapi.WaveFormat.Channels}ch (mixFmt={(mixFmt != null ? "적용" : "기본")})");

                _captureFormat = wasapi.WaveFormat;
                _capture = wasapi;

                wasapi.DataAvailable += (s, e) =>
                {
                    if (e.BytesRecorded > 0 && _captureFormat != null)
                        OnMonitoringDataAvailable(e.Buffer, e.BytesRecorded, _captureFormat);
                };
                wasapi.RecordingStopped += (s, e) =>
                {
                    if (e.Exception != null && _isMonitoring)
                        Log4.Warn($"[마이크테스트] WasapiCapture 중지: {e.Exception.Message}");
                };

                wasapi.StartRecording();
                _isMonitoring = true;
                Log4.Info($"[마이크테스트] WasapiCapture 모니터링 시작 (useEvent={useEvent})");
                return true;
            }
            catch (Exception ex)
            {
                Log4.Warn($"[마이크테스트] WasapiCapture 시도 실패 (useEvent={useEvent}): 0x{ex.HResult:X8} {ex.Message}");
                if (_capture != null) { try { _capture.Dispose(); } catch { } _capture = null; }
            }
        }
        _isMonitoring = false;
        return false;
    }

    private void OnVolumeLevelChanged(float level)
    {
        VolumeLevelChanged?.Invoke(Math.Min(level, 1.0f));
    }

    private void OnDecibelLevelChanged(float db)
    {
        DecibelLevelChanged?.Invoke(Math.Max(db, -60f));
    }

    /// <summary>
    /// 모니터링 중지
    /// </summary>
    public void StopMonitoring()
    {
        _isMonitoring = false;
        if (_monitorCts != null)
        {
            _monitorCts.Cancel();
            try { _monitorTask?.Wait(1000); } catch { } // Dispose 경로 — 동기 블로킹 의도적 (타임아웃 1초)
            _monitorCts.Dispose();
            _monitorCts = null;
            _monitorTask = null;
            Log4.Debug("[마이크테스트] 모니터링 중지");
        }
        if (_capture != null)
        {
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }

    }

    /// <summary>
    /// 테스트 녹음 시작
    /// </summary>
    public void StartTestRecording(string deviceId)
    {
        StopTestRecording();
        StopMonitoring();

        try
        {
            var enumerator = new MMDeviceEnumerator();
            var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var device = allDevices.FirstOrDefault(d => d.ID == deviceId);
            if (device == null)
            {
                Log4.Warn($"[마이크테스트] 녹음 장치를 찾을 수 없음: {deviceId}");
                return;
            }

            _testRecordingPath = Path.Combine(Path.GetTempPath(), "maix_mic_test.wav");

            // MixFormat을 별도 인스턴스에서 읽기 (NAudio AudioClient 싱글톤 캐시 오염 방지)
            WaveFormat? mixFormat = null;
            try
            {
                using var fmtEnum = new MMDeviceEnumerator();
                var fmtDevice = fmtEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                        .FirstOrDefault(d => d.ID == deviceId);
                if (fmtDevice != null) mixFormat = fmtDevice.AudioClient.MixFormat;
            }
            catch { }

            // useEventSync: true로 시도, 실패 시 false로 재시도
            Exception? lastEx = null;
            foreach (var useEvent in new[] { true, false })
            {
                try
                {
                    _capture?.Dispose();
                    _waveWriter?.Dispose();
                    // 장치를 새로 열기 위해 enumerator에서 다시 가져옴
                    var freshDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                                 .FirstOrDefault(d => d.ID == deviceId);
                    if (freshDevice == null) break;
                    _capture = new WasapiCapture(freshDevice, useEvent, 100);
                    if (mixFormat != null) _capture.WaveFormat = mixFormat;
                    _waveWriter = new WaveFileWriter(_testRecordingPath, _capture.WaveFormat);
                    _capture.DataAvailable += OnRecordingDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;
                    _capture.StartRecording();
                    _isRecording = true;
                    Log4.Info($"[마이크테스트] 테스트 녹음 시작: {freshDevice.FriendlyName} (포맷: {_capture.WaveFormat}, useEvent={useEvent})");
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Log4.Warn($"[마이크테스트] 녹음 시도 실패 (useEvent={useEvent}): {ex.Message}");
                    if (_capture != null) { try { _capture.Dispose(); } catch { } _capture = null; }
                    if (_waveWriter != null) { try { _waveWriter.Dispose(); } catch { } _waveWriter = null; }
                }
            }
            Log4.Warn($"[마이크테스트] 테스트 녹음 시작 최종 실패: {lastEx?.Message}\n{lastEx?.StackTrace}");
        }
        catch (Exception ex)
        {
            Log4.Warn($"[마이크테스트] 테스트 녹음 시작 실패 ({ex.GetType().Name}): {ex.Message}\n{ex.StackTrace}");
            CleanupRecording();
        }
    }

    /// <summary>
    /// 테스트 녹음 중지
    /// </summary>
    public void StopTestRecording()
    {
        if (_capture != null && _isRecording)
        {
            _isRecording = false;
            try
            {
                _capture.StopRecording();
            }
            catch { }
        }
    }

    /// <summary>
    /// 테스트 녹음 파일 재생
    /// </summary>
    public void PlayTestRecording(string filePath)
    {
        StopPlayback();

        try
        {
            _audioFileReader = new AudioFileReader(filePath);
            _playbackDevice = new WaveOutEvent();
            _playbackDevice.PlaybackStopped += OnPlaybackStopped;
            _playbackDevice.Init(_audioFileReader);
            _playbackDevice.Play();
            Log4.Info($"[마이크테스트] 테스트 재생 시작: {filePath}");
        }
        catch (Exception ex)
        {
            Log4.Warn($"[마이크테스트] 테스트 재생 실패: {ex.Message}");
            StopPlayback();
        }
    }

    /// <summary>
    /// 장치 볼륨 가져오기 (0.0 ~ 1.0)
    /// </summary>
    public float GetDeviceVolume(string deviceId)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var device = allDevices.FirstOrDefault(d => d.ID == deviceId);
            if (device != null)
            {
                return device.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[마이크테스트] 볼륨 조회 실패: {ex.Message}");
        }
        return 0.5f;
    }

    /// <summary>
    /// 장치 볼륨 설정 (0.0 ~ 1.0)
    /// </summary>
    public void SetDeviceVolume(string deviceId, float volume)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var device = allDevices.FirstOrDefault(d => d.ID == deviceId);
            if (device != null)
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
                Log4.Debug($"[마이크테스트] 볼륨 설정: {volume:P0}");
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[마이크테스트] 볼륨 설정 실패: {ex.Message}");
        }
    }

    private void OnMonitoringDataAvailable(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        if (bytesRecorded == 0) return;

        int bytesPerSample = waveFormat.BitsPerSample / 8;
        if (bytesPerSample <= 0) return;

        float peak = 0;
        for (int i = 0; i < bytesRecorded; i += bytesPerSample)
        {
            if (i + bytesPerSample > bytesRecorded) break;

            float sample;
            if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                sample = Math.Abs(BitConverter.ToSingle(buffer, i));
            else if (waveFormat.BitsPerSample == 16)
                sample = Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f);
            else if (waveFormat.BitsPerSample == 24)
            {
                int value = buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16);
                if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                sample = Math.Abs(value / 8388608f);
            }
            else if (waveFormat.BitsPerSample == 32)
                sample = Math.Abs(BitConverter.ToInt32(buffer, i) / 2147483648f);
            else
                sample = 0;

            if (sample > peak) peak = sample;
        }
        peak = Math.Min(peak, 1.0f);
        VolumeLevelChanged?.Invoke(peak);

        float db = peak > 0 ? 20f * MathF.Log10(peak) : -60f;
        db = Math.Max(db, -60f);
        DecibelLevelChanged?.Invoke(db);
    }

    private void OnRecordingDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_waveWriter != null)
        {
            _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
        }

        // 녹음 중에도 레벨 표시
        var waveFormat = _capture?.WaveFormat;
        if (waveFormat != null)
            OnMonitoringDataAvailable(e.Buffer, e.BytesRecorded, waveFormat);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var path = _testRecordingPath;
        CleanupRecording();

        if (!string.IsNullOrEmpty(path))
        {
            Log4.Info($"[마이크테스트] 테스트 녹음 완료: {path}");
            TestRecordingCompleted?.Invoke(path);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        StopPlayback();
        Log4.Debug("[마이크테스트] 테스트 재생 완료");
        TestPlaybackCompleted?.Invoke();
    }

    private void StopPlayback()
    {
        if (_playbackDevice != null)
        {
            _playbackDevice.PlaybackStopped -= OnPlaybackStopped;
            try { _playbackDevice.Stop(); } catch { }
            _playbackDevice.Dispose();
            _playbackDevice = null;
        }
        if (_audioFileReader != null)
        {
            _audioFileReader.Dispose();
            _audioFileReader = null;
        }
    }

    private void CleanupRecording()
    {
        _isRecording = false;
        if (_capture != null)
        {
            _capture.DataAvailable -= OnRecordingDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }
        if (_waveWriter != null)
        {
            _waveWriter.Dispose();
            _waveWriter = null;
        }
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        StopMonitoring();
        StopTestRecording();
        StopPlayback();
        CleanupRecording();
    }
}
