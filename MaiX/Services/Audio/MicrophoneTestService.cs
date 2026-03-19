using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MaiX.Utils;

namespace MaiX.Services.Audio;

/// <summary>
/// 마이크 장치 정보
/// </summary>
public record AudioDeviceInfo(string DeviceId, string FriendlyName, bool IsDefault);

/// <summary>
/// 마이크 테스트 전용 서비스 (장치 열거, 실시간 모니터링, 테스트 녹음/재생, 볼륨 조절)
/// </summary>
public class MicrophoneTestService : IDisposable
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _waveWriter;
    private WaveOutEvent? _playbackDevice;
    private AudioFileReader? _audioFileReader;
    private string? _testRecordingPath;
    private bool _isMonitoring;
    private bool _isRecording;

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
    /// 지정한 장치로 실시간 모니터링 시작
    /// </summary>
    public void StartMonitoring(string deviceId)
    {
        StopMonitoring();

        try
        {
            var enumerator = new MMDeviceEnumerator();
            var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var device = allDevices.FirstOrDefault(d => d.ID == deviceId);
            if (device == null)
            {
                Log4.Warn($"[마이크테스트] 모니터링 장치를 찾을 수 없음: {deviceId}");
                return;
            }

            _capture = new WasapiCapture(device);
            _capture.DataAvailable += OnMonitoringDataAvailable;
            _capture.StartRecording();
            _isMonitoring = true;
            Log4.Info($"[마이크테스트] 모니터링 시작: {device.FriendlyName}");
        }
        catch (Exception ex)
        {
            Log4.Warn($"[마이크테스트] 모니터링 시작 실패: {ex.Message}");
            StopMonitoring();
        }
    }

    /// <summary>
    /// 모니터링 중지
    /// </summary>
    public void StopMonitoring()
    {
        if (_capture != null)
        {
            _isMonitoring = false;
            try
            {
                _capture.DataAvailable -= OnMonitoringDataAvailable;
                _capture.StopRecording();
            }
            catch { }
            _capture.Dispose();
            _capture = null;
            Log4.Debug("[마이크테스트] 모니터링 중지");
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
            _capture = new WasapiCapture(device);
            _waveWriter = new WaveFileWriter(_testRecordingPath, _capture.WaveFormat);
            _capture.DataAvailable += OnRecordingDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isRecording = true;
            Log4.Info($"[마이크테스트] 테스트 녹음 시작: {device.FriendlyName}");
        }
        catch (Exception ex)
        {
            Log4.Warn($"[마이크테스트] 테스트 녹음 시작 실패: {ex.Message}");
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

    private void OnMonitoringDataAvailable(object? sender, WaveInEventArgs e)
    {
        float peak = 0;
        for (int i = 0; i < e.BytesRecorded; i += 4)
        {
            if (i + 4 > e.BytesRecorded) break;
            float sample = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
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
        OnMonitoringDataAvailable(sender, e);
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
        StopMonitoring();
        StopTestRecording();
        StopPlayback();
        CleanupRecording();
    }
}
