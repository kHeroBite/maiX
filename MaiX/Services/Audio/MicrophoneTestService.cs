using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

    // Native COM 직접 모니터링용 필드
    private IntPtr _nativeAudioClient = IntPtr.Zero;
    private IntPtr _nativeCaptureClient = IntPtr.Zero;
    private CancellationTokenSource? _nativeMonitorCts;
    private Task? _nativeMonitorTask;
    private WaveFormat? _nativeCaptureFormat;

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
    /// 지정한 장치로 실시간 모니터링 시작 (WASAPI → IAudioClient3 → WasapiNative(AUTOCONVERTPCM) → WaveInEvent MME 폴백)
    /// </summary>
    public void StartMonitoring(string deviceId)
    {
        StopMonitoring();

        // 1단계: WASAPI AudioClient 직접 캡처 시도
        if (TryStartWasapiMonitoring(deviceId))
            return;

        // 2단계: IAudioClient3 시도 — Intel SST 등 저지연 공유 모드 대응
        Log4.Warn("[마이크테스트] WasapiCapture 실패 — IAudioClient3 시도");
        if (TryStartAudioClient3Monitoring(deviceId))
            return;

        // 3단계: WasapiNative(AUTOCONVERTPCM) 시도 — AUTOCONVERT 필수 드라이버 대응
        Log4.Warn("[마이크테스트] IAudioClient3 실패 — WasapiNative(AUTOCONVERTPCM) 시도");
        if (TryStartNativeMonitoring(deviceId))
            return;

        // 4단계: WaveInEvent MME 폴백
        Log4.Warn("[마이크테스트] WasapiNative 실패 — WaveInEvent MME 폴백 시도");
        TryStartMmeMonitoring(deviceId);
    }

    private bool TryStartWasapiMonitoring(string deviceId)
    {
        // useEventSync true → false 순서로 WasapiCapture 시도 (NAudio 내부 Initialize 위임)
        var enumerator = new MMDeviceEnumerator();
        foreach (var useEvent in new[] { false, true })
        {
            try
            {
                var device = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                       .FirstOrDefault(d => d.ID == deviceId);
                if (device == null)
                {
                    Log4.Warn($"[마이크테스트] 모니터링 장치를 찾을 수 없음: {deviceId}");
                    return false;
                }

                var wasapi = new WasapiCapture(device, useEvent, 100);
                Log4.Info($"[마이크테스트] WasapiCapture 시도 (useEvent={useEvent}): {wasapi.WaveFormat.SampleRate}Hz {wasapi.WaveFormat.BitsPerSample}bit {wasapi.WaveFormat.Channels}ch");

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

    private bool TryStartAudioClient3Monitoring(string deviceId)
    {
        IntPtr pWfx = IntPtr.Zero;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                   .FirstOrDefault(d => d.ID == deviceId);
            if (device == null) return false;

            _nativeAudioClient = WasapiNative.ActivateAudioClient3(device);
            if (_nativeAudioClient == IntPtr.Zero)
            {
                Log4.Warn("[마이크테스트] ActivateAudioClient3 실패");
                return false;
            }

            int hr = WasapiNative.AudioClientGetMixFormat(_nativeAudioClient, out pWfx);
            if (hr != 0 || pWfx == IntPtr.Zero)
            {
                Log4.Warn($"[마이크테스트] AC3 GetMixFormat 실패 hr=0x{hr:X8}");
                WasapiNative.ComRelease(ref _nativeAudioClient);
                return false;
            }

            hr = WasapiNative.AudioClient3GetSharedModeEnginePeriod(
                _nativeAudioClient, pWfx, out uint defaultPeriod, out _, out _, out _);
            uint periodFrames = (hr == 0 && defaultPeriod > 0) ? defaultPeriod : 480u;
            Log4.Info($"[마이크테스트] AC3 period={periodFrames} hr=0x{hr:X8}");

            hr = WasapiNative.AudioClient3InitializeSharedAudioStream(
                _nativeAudioClient, 0, periodFrames, pWfx, IntPtr.Zero);
            if (hr != 0)
            {
                Log4.Warn($"[마이크테스트] AC3 InitializeSharedAudioStream hr=0x{hr:X8} — AUTOCONVERT 재시도");
                hr = WasapiNative.AudioClient3InitializeSharedAudioStream(
                    _nativeAudioClient,
                    WasapiNative.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | WasapiNative.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    periodFrames, pWfx, IntPtr.Zero);
            }
            if (hr != 0)
            {
                Log4.Warn($"[마이크테스트] AC3 InitializeSharedAudioStream 최종 실패 hr=0x{hr:X8}");
                WasapiNative.ComRelease(ref _nativeAudioClient);
                return false;
            }

            var captureIid = WasapiNative.IID_IAudioCaptureClient;
            hr = WasapiNative.AudioClientGetService(_nativeAudioClient, ref captureIid, out _nativeCaptureClient);
            if (hr != 0 || _nativeCaptureClient == IntPtr.Zero)
            {
                Log4.Warn($"[마이크테스트] AC3 GetService hr=0x{hr:X8}");
                WasapiNative.ComRelease(ref _nativeAudioClient);
                return false;
            }

            _nativeCaptureFormat = ParseWaveFormatFromPtr(pWfx);
            WasapiNative.AudioClientStart(_nativeAudioClient);
            _nativeMonitorCts = new CancellationTokenSource();
            _nativeMonitorTask = Task.Run(() => NativeCaptureLoop(_nativeMonitorCts.Token));
            _isMonitoring = true;

            Log4.Info("[마이크테스트] IAudioClient3 모니터링 시작 성공");
            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"[마이크테스트] TryStartAudioClient3Monitoring 예외: {ex}");
            CleanupNativeResources();
            return false;
        }
        finally
        {
            if (pWfx != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pWfx);
        }
    }

    private static WaveFormat? ParseWaveFormatFromPtr(IntPtr pWfx)
    {
        if (pWfx == IntPtr.Zero) return null;
        try
        {
            ushort tag = (ushort)Marshal.ReadInt16(pWfx, 0);
            ushort channels = (ushort)Marshal.ReadInt16(pWfx, 2);
            int sampleRate = Marshal.ReadInt32(pWfx, 4);
            ushort bits = (ushort)Marshal.ReadInt16(pWfx, 14);
            if (tag == 3 || tag == 0xFFFE)
            {
                // IEEE_FLOAT 또는 EXTENSIBLE — float 포맷으로 처리
                return WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            }
            return new WaveFormat(sampleRate, bits, channels);
        }
        catch { return null; }
    }

    private bool TryStartNativeMonitoring(string deviceId)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                   .FirstOrDefault(d => d.ID == deviceId);
            if (device == null)
            {
                Log4.Warn($"[마이크테스트] NativeMonitoring 장치를 찾을 수 없음: {deviceId}");
                return false;
            }

            // 1) ActivateAudioClient — 순수 P/Invoke로 IAudioClient COM 포인터 획득
            _nativeAudioClient = WasapiNative.ActivateAudioClient(device);
            if (_nativeAudioClient == IntPtr.Zero)
            {
                Log4.Warn("[마이크테스트] WasapiNative.ActivateAudioClient 실패 — IntPtr.Zero 반환");
                return false;
            }

            // 2) InitializeWithMixFormat — 4단계 AUTOCONVERTPCM 폴백
            int hr = WasapiNative.InitializeWithMixFormat(_nativeAudioClient, out long _);
            if (hr != 0)
            {
                Log4.Warn($"[마이크테스트] WasapiNative.InitializeWithMixFormat 실패 hr=0x{hr:X8}");
                WasapiNative.ComRelease(ref _nativeAudioClient);
                return false;
            }

            // 3) GetService → IAudioCaptureClient
            var iidCapture = WasapiNative.IID_IAudioCaptureClient;
            hr = WasapiNative.AudioClientGetService(_nativeAudioClient, ref iidCapture, out _nativeCaptureClient);
            if (hr != 0 || _nativeCaptureClient == IntPtr.Zero)
            {
                Log4.Warn($"[마이크테스트] AudioClientGetService(IAudioCaptureClient) 실패 hr=0x{hr:X8}");
                WasapiNative.ComRelease(ref _nativeAudioClient);
                return false;
            }

            // 4) GetMixFormat로 실제 캡처 포맷 획득
            hr = WasapiNative.AudioClientGetMixFormat(_nativeAudioClient, out IntPtr pWfx);
            if (hr == 0 && pWfx != IntPtr.Zero)
            {
                try
                {
                    var rawFmt = Marshal.PtrToStructure<WasapiNative.WAVEFORMATEXTENSIBLE>(pWfx);
                    bool isFloat = rawFmt.SubFormat == WasapiNative.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
                    _nativeCaptureFormat = isFloat
                        ? WaveFormat.CreateIeeeFloatWaveFormat((int)rawFmt.nSamplesPerSec, rawFmt.nChannels)
                        : new WaveFormat((int)rawFmt.nSamplesPerSec, rawFmt.wBitsPerSample, rawFmt.nChannels);
                    Log4.Info($"[마이크테스트] NativeCaptureFormat: {_nativeCaptureFormat.SampleRate}Hz {_nativeCaptureFormat.BitsPerSample}bit {_nativeCaptureFormat.Channels}ch isFloat={isFloat}");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pWfx);
                }
            }
            else
            {
                _nativeCaptureFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                Log4.Warn("[마이크테스트] GetMixFormat 실패 — 48kHz float stereo 폴백");
            }

            // 5) AudioClientStart
            hr = WasapiNative.AudioClientStart(_nativeAudioClient);
            if (hr != 0)
            {
                Log4.Warn($"[마이크테스트] AudioClientStart 실패 hr=0x{hr:X8}");
                WasapiNative.ComRelease(ref _nativeCaptureClient);
                WasapiNative.ComRelease(ref _nativeAudioClient);
                return false;
            }

            // 6) 캡처 루프 Task 시작
            _nativeMonitorCts = new CancellationTokenSource();
            _nativeMonitorTask = Task.Run(() => NativeCaptureLoop(_nativeMonitorCts.Token));
            _isMonitoring = true;

            Log4.Info("[마이크테스트] WasapiNative COM 직접 모니터링 시작 성공");
            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"[마이크테스트] TryStartNativeMonitoring 예외: {ex}");
            CleanupNativeResources();
            return false;
        }
    }

    private void NativeCaptureLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int hr = WasapiNative.CaptureClientGetNextPacketSize(_nativeCaptureClient, out uint packetSize);
                if (hr != 0 || packetSize == 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                hr = WasapiNative.CaptureClientGetBuffer(
                    _nativeCaptureClient,
                    out IntPtr pData,
                    out uint numFrames,
                    out uint flags,
                    out ulong devicePos,
                    out ulong qpcPos);

                if (hr != 0 || numFrames == 0)
                {
                    if (hr != 0) WasapiNative.CaptureClientReleaseBuffer(_nativeCaptureClient, 0);
                    Thread.Sleep(5);
                    continue;
                }

                // 바이트 복사
                int bytesPerFrame = _nativeCaptureFormat?.BlockAlign ?? 4;
                int totalBytes = (int)numFrames * bytesPerFrame;
                byte[] buffer = new byte[totalBytes];
                if (pData != IntPtr.Zero)
                    Marshal.Copy(pData, buffer, 0, totalBytes);

                WasapiNative.CaptureClientReleaseBuffer(_nativeCaptureClient, numFrames);

                // dB 미터 업데이트 이벤트 발생
                var fmt = _nativeCaptureFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                OnMonitoringDataAvailable(buffer, totalBytes, fmt);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log4.Error($"[마이크테스트] NativeCaptureLoop 예외: {ex}");
        }
    }

    private void CleanupNativeResources()
    {
        _nativeMonitorCts?.Cancel();
        try { _nativeMonitorTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _nativeMonitorCts?.Dispose();
        _nativeMonitorCts = null;
        _nativeMonitorTask = null;

        if (_nativeAudioClient != IntPtr.Zero)
        {
            try { WasapiNative.AudioClientStop(_nativeAudioClient); } catch { }
            WasapiNative.ComRelease(ref _nativeAudioClient);
        }
        if (_nativeCaptureClient != IntPtr.Zero)
            WasapiNative.ComRelease(ref _nativeCaptureClient);

        _nativeCaptureFormat = null;
    }

    private void TryStartMmeMonitoring(string deviceId)
    {
        // MixFormat 읽기 (포맷 우선순위 결정용)
        int sampleRate = 48000;
        int channels = 1;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                   .FirstOrDefault(d => d.ID == deviceId);
            if (device != null)
            {
                var mixFmt = device.AudioClient.MixFormat;
                sampleRate = mixFmt.SampleRate;
                channels = mixFmt.Channels;
            }
        }
        catch { }

        // 시도할 포맷 목록 (MixFormat 기반 → 범용 순서)
        var formatsToTry = new[]
        {
            new WaveFormat(sampleRate, 16, channels),
            new WaveFormat(sampleRate, 16, 1),
            new WaveFormat(48000, 16, 2),
            new WaveFormat(48000, 16, 1),
            new WaveFormat(44100, 16, 1),
        };

        int deviceCount = WaveInEvent.DeviceCount;
        Log4.Info($"[마이크테스트] MME 장치 수: {deviceCount}");

        for (int devIdx = 0; devIdx < Math.Max(1, deviceCount); devIdx++)
        {
            foreach (var fmt in formatsToTry)
            {
                try
                {
                    var mmeCapture = new WaveInEvent { DeviceNumber = devIdx, WaveFormat = fmt };
                    mmeCapture.DataAvailable += (s, e) =>
                    {
                        if (e.BytesRecorded > 0 && _captureFormat != null)
                            OnMonitoringDataAvailable(e.Buffer, e.BytesRecorded, _captureFormat);
                    };
                    mmeCapture.RecordingStopped += (s, e) =>
                    {
                        if (e.Exception != null)
                            Log4.Warn($"[마이크테스트] MME 모니터링 중지: {e.Exception.Message}");
                    };

                    _captureFormat = fmt;
                    _capture = mmeCapture;
                    mmeCapture.StartRecording();
                    _isMonitoring = true;

                    Log4.Info($"[마이크테스트] MME 모니터링 시작 성공: devIdx={devIdx}, {fmt.SampleRate}Hz {fmt.BitsPerSample}bit {fmt.Channels}ch");
                    return;
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[마이크테스트] MME 시도 실패 (devIdx={devIdx}, {fmt.SampleRate}Hz {fmt.Channels}ch): {ex.Message}");
                    if (_capture != null) { try { _capture.Dispose(); } catch { } _capture = null; }
                }
            }
        }

        Log4.Warn("[마이크테스트] MME 폴백도 실패 — 모니터링 불가");
        _isMonitoring = false;
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
            try { _monitorTask?.Wait(1000); } catch { }
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

        // Native COM 리소스 정리
        CleanupNativeResources();
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

            // MixFormat을 WasapiCapture 생성 전에 읽기 (AudioClient 오염 방지)
            WaveFormat? mixFormat = null;
            try { mixFormat = device.AudioClient.MixFormat; } catch { }

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
        _nativeMonitorCts?.Cancel();
        StopMonitoring();
        StopTestRecording();
        StopPlayback();
        CleanupRecording();
        CleanupNativeResources();
    }
}
