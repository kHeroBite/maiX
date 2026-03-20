using System;
using System.Runtime.InteropServices;
using MaiX.Utils;

namespace MaiX.Services.Audio;

/// <summary>
/// WASAPI IAudioClient COM P/Invoke — NAudio 마샬링 버그 우회
/// WaveFormatExtensible을 IntPtr로 전달하여 cbSize 잘림 방지
/// </summary>
internal static class WasapiNative
{
    // WaveFormatExtensible 구조체 (40바이트)
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WAVEFORMATEXTENSIBLE
    {
        public ushort wFormatTag;       // 0xFFFE = EXTENSIBLE
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;           // 22 (Extensible 추가 필드 크기)
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;          // KSDATAFORMAT_SUBTYPE_PCM 또는 SUBTYPE_IEEE_FLOAT
    }

    // KSDATAFORMAT_SUBTYPE_PCM: {00000001-0000-0010-8000-00AA00389B71}
    public static readonly Guid KSDATAFORMAT_SUBTYPE_PCM =
        new Guid(0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: {00000003-0000-0010-8000-00AA00389B71}
    public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT =
        new Guid(0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    /// <summary>
    /// IAudioClient::Initialize를 직접 vtbl 호출 — WaveFormatExtensible IntPtr 전달
    /// </summary>
    public static unsafe int AudioClientInitialize(
        IntPtr pAudioClient,
        int shareMode,          // 0=Shared, 1=Exclusive
        uint streamFlags,       // AudioClientStreamFlags
        long hnsBufferDuration,
        long hnsPeriodicity,
        ref WAVEFORMATEXTENSIBLE pFormat,
        ref Guid audioSessionGuid)
    {
        // vtbl[3] = Initialize (IUnknown 3개 이후)
        var vtbl = *(IntPtr**)pAudioClient;
        var initialize = (delegate* unmanaged[Stdcall]<
            IntPtr, int, uint, long, long, WAVEFORMATEXTENSIBLE*, Guid*, int>)vtbl[3];

        fixed (WAVEFORMATEXTENSIBLE* pFmt = &pFormat)
        fixed (Guid* pGuid = &audioSessionGuid)
        {
            return initialize(pAudioClient, shareMode, streamFlags,
                hnsBufferDuration, hnsPeriodicity, pFmt, pGuid);
        }
    }

    /// <summary>
    /// IAudioClient::Start — vtbl[10]
    /// </summary>
    public static unsafe int AudioClientStart(IntPtr pAudioClient)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var start = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtbl[10];
        return start(pAudioClient);
    }

    /// <summary>
    /// IAudioClient::Stop — vtbl[11]
    /// </summary>
    public static unsafe int AudioClientStop(IntPtr pAudioClient)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var stop = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtbl[11];
        return stop(pAudioClient);
    }

    /// <summary>
    /// IAudioClient::Reset — vtbl[12]
    /// </summary>
    public static unsafe int AudioClientReset(IntPtr pAudioClient)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var reset = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtbl[12];
        return reset(pAudioClient);
    }

    /// <summary>
    /// IAudioClient::GetBufferSize — vtbl[4]
    /// </summary>
    public static unsafe int AudioClientGetBufferSize(IntPtr pAudioClient, out uint bufferFrameCount)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var getBufferSize = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)vtbl[4];
        uint count;
        int hr = getBufferSize(pAudioClient, &count);
        bufferFrameCount = count;
        return hr;
    }

    /// <summary>
    /// IAudioClient::GetCurrentPadding — vtbl[6]
    /// </summary>
    public static unsafe int AudioClientGetCurrentPadding(IntPtr pAudioClient, out uint numPaddingFrames)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var getCurrentPadding = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)vtbl[6];
        uint padding;
        int hr = getCurrentPadding(pAudioClient, &padding);
        numPaddingFrames = padding;
        return hr;
    }

    /// <summary>
    /// IAudioClient::GetService(IID_IAudioCaptureClient) — vtbl[14]
    /// </summary>
    public static unsafe int AudioClientGetService(
        IntPtr pAudioClient,
        ref Guid riid,
        out IntPtr ppv)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var getService = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[14];
        fixed (Guid* pRiid = &riid)
        {
            IntPtr result;
            int hr = getService(pAudioClient, pRiid, &result);
            ppv = result;
            return hr;
        }
    }

    // IAudioCaptureClient IID: {C8ADBD64-E71E-48A0-A4DE-185C395CD317}
    public static Guid IID_IAudioCaptureClient =
        new Guid(0xC8ADBD64, 0xE71E, 0x48A0, 0xA4, 0xDE, 0x18, 0x5C, 0x39, 0x5C, 0xD3, 0x17);

    // IAudioCaptureClient vtbl 오프셋 (IUnknown 3개 이후):
    // GetBuffer = vtbl[3], ReleaseBuffer = vtbl[4], GetNextPacketSize = vtbl[5]

    /// <summary>
    /// IAudioCaptureClient::GetBuffer — vtbl[3]
    /// </summary>
    public static unsafe int CaptureClientGetBuffer(
        IntPtr pCaptureClient,
        out IntPtr ppData,
        out uint numFramesRead,
        out uint dwFlags,
        out ulong devicePosition,
        out ulong qpcPosition)
    {
        var vtbl = *(IntPtr**)pCaptureClient;
        var getBuffer = (delegate* unmanaged[Stdcall]<
            IntPtr, IntPtr*, uint*, uint*, ulong*, ulong*, int>)vtbl[3];

        IntPtr data;
        uint frames, flags;
        ulong devPos, qpc;
        int hr = getBuffer(pCaptureClient, &data, &frames, &flags, &devPos, &qpc);
        ppData = data;
        numFramesRead = frames;
        dwFlags = flags;
        devicePosition = devPos;
        qpcPosition = qpc;
        return hr;
    }

    /// <summary>
    /// IAudioCaptureClient::ReleaseBuffer — vtbl[4]
    /// </summary>
    public static unsafe int CaptureClientReleaseBuffer(IntPtr pCaptureClient, uint numFramesRead)
    {
        var vtbl = *(IntPtr**)pCaptureClient;
        var releaseBuffer = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)vtbl[4];
        return releaseBuffer(pCaptureClient, numFramesRead);
    }

    /// <summary>
    /// IAudioCaptureClient::GetNextPacketSize — vtbl[5]
    /// </summary>
    public static unsafe int CaptureClientGetNextPacketSize(IntPtr pCaptureClient, out uint numFramesInNextPacket)
    {
        var vtbl = *(IntPtr**)pCaptureClient;
        var getNextPacketSize = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)vtbl[5];
        uint count;
        int hr = getNextPacketSize(pCaptureClient, &count);
        numFramesInNextPacket = count;
        return hr;
    }

    // AUDCLNT_BUFFERFLAGS
    public const uint AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY = 0x1;
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    public const uint AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR = 0x4;

    // AudioClientStreamFlags
    public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    public const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;

    // SPEAKER 채널 마스크
    public const uint SPEAKER_FRONT_LEFT = 0x1;
    public const uint SPEAKER_FRONT_RIGHT = 0x2;
    public const uint SPEAKER_FRONT_CENTER = 0x4;
    public const uint KSAUDIO_SPEAKER_STEREO = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
    public const uint KSAUDIO_SPEAKER_MONO = SPEAKER_FRONT_CENTER;

    /// <summary>
    /// MixFormat(NAudio WaveFormat)에서 WAVEFORMATEXTENSIBLE 구조체 생성
    /// </summary>
    public static WAVEFORMATEXTENSIBLE CreateFromMixFormat(NAudio.Wave.WaveFormat mixFormat)
    {
        bool isFloat = mixFormat.Encoding == NAudio.Wave.WaveFormatEncoding.IeeeFloat ||
                       (mixFormat is NAudio.Wave.WaveFormatExtensible ext &&
                        ext.SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);

        return new WAVEFORMATEXTENSIBLE
        {
            wFormatTag = 0xFFFE,  // WAVE_FORMAT_EXTENSIBLE
            nChannels = (ushort)mixFormat.Channels,
            nSamplesPerSec = (uint)mixFormat.SampleRate,
            wBitsPerSample = (ushort)mixFormat.BitsPerSample,
            nBlockAlign = (ushort)mixFormat.BlockAlign,
            nAvgBytesPerSec = (uint)mixFormat.AverageBytesPerSecond,
            cbSize = 22,
            wValidBitsPerSample = (ushort)mixFormat.BitsPerSample,
            dwChannelMask = mixFormat.Channels == 1 ? KSAUDIO_SPEAKER_MONO : KSAUDIO_SPEAKER_STEREO,
            SubFormat = isFloat ? KSDATAFORMAT_SUBTYPE_IEEE_FLOAT : KSDATAFORMAT_SUBTYPE_PCM
        };
    }

    /// <summary>
    /// IAudioClient::GetMixFormat — vtbl[8]
    /// 반환된 pWfx는 CoTaskMemFree 필요
    /// </summary>
    public static unsafe int AudioClientGetMixFormat(IntPtr pAudioClient, out IntPtr ppDeviceFormat)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var getMixFormat = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)vtbl[8];
        IntPtr pWfx;
        int hr = getMixFormat(pAudioClient, &pWfx);
        ppDeviceFormat = pWfx;
        return hr;
    }

    /// <summary>
    /// IAudioClient::GetDevicePeriod — vtbl[9]
    /// defaultDevicePeriod: 기본 주기 (100ns 단위), minimumDevicePeriod: 최소 주기
    /// </summary>
    public static unsafe int AudioClientGetDevicePeriod(IntPtr pAudioClient,
        out long defaultDevicePeriod, out long minimumDevicePeriod)
    {
        var vtbl = *(IntPtr**)pAudioClient;
        var getDevicePeriod = (delegate* unmanaged[Stdcall]<IntPtr, long*, long*, int>)vtbl[9];
        long def, min;
        int hr = getDevicePeriod(pAudioClient, &def, &min);
        defaultDevicePeriod = def;
        minimumDevicePeriod = min;
        return hr;
    }

    /// <summary>
    /// IAudioClient::Initialize — GetMixFormat 원본 pWfx 포인터로 직접 호출
    /// 4단계 폴백: flags=0/dur=0 → AUTOCONVERT/dur=0 → AUTOCONVERT/defaultPeriod → flags=0/defaultPeriod
    /// Intel SST 등 AUTOCONVERTPCM 필요 드라이버 대응
    /// </summary>
    public static unsafe int InitializeWithMixFormat(IntPtr pAudioClient, out long usedDuration)
    {
        // GetMixFormat으로 드라이버 네이티브 포맷 획득
        int hr = AudioClientGetMixFormat(pAudioClient, out IntPtr pWfx);
        if (hr != 0) { usedDuration = 0; return hr; }

        try
        {
            var vtbl = *(IntPtr**)pAudioClient;
            var initialize = (delegate* unmanaged[Stdcall]<IntPtr, int, uint, long, long, IntPtr, IntPtr, int>)vtbl[3];

            // 시도 1: flags=0, dur=0 (드라이버가 자동 결정)
            hr = initialize(pAudioClient, 0, 0, 0, 0, pWfx, IntPtr.Zero);
            if (hr == 0) { usedDuration = 0; return 0; }
            Log4.Warn($"[WasapiNative] Initialize 시도1 실패 HResult=0x{(uint)hr:X8} flags=0 dur=0");

            // 시도 2: AUTOCONVERTPCM | SRC_DEFAULT_QUALITY, dur=0
            const uint autoConvertFlags = AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            hr = initialize(pAudioClient, 0, autoConvertFlags, 0, 0, pWfx, IntPtr.Zero);
            if (hr == 0) { usedDuration = 0; return 0; }
            Log4.Warn($"[WasapiNative] Initialize 시도2 실패 HResult=0x{(uint)hr:X8} flags=AUTOCONVERT dur=0");

            // GetDevicePeriod 취득
            hr = AudioClientGetDevicePeriod(pAudioClient, out long defaultPeriod, out _);
            if (hr != 0) { usedDuration = 0; return hr; }
            Log4.Info($"[WasapiNative] GetDevicePeriod: default={defaultPeriod}");

            // 시도 3: AUTOCONVERTPCM | SRC_DEFAULT_QUALITY, dur=defaultPeriod
            hr = initialize(pAudioClient, 0, autoConvertFlags, defaultPeriod, 0, pWfx, IntPtr.Zero);
            if (hr == 0) { usedDuration = defaultPeriod; return 0; }
            Log4.Warn($"[WasapiNative] Initialize 시도3 실패 HResult=0x{(uint)hr:X8} flags=AUTOCONVERT dur={defaultPeriod}");

            // 시도 4: flags=0, dur=defaultPeriod (최종 폴백)
            hr = initialize(pAudioClient, 0, 0, defaultPeriod, 0, pWfx, IntPtr.Zero);
            usedDuration = defaultPeriod;
            return hr;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pWfx);
        }
    }

    // IID_IAudioClient: {1CB9AD4C-DBFA-4C32-B178-C2F568A703B2}
    public static Guid IID_IAudioClient =
        new Guid(0x1CB9AD4C, 0xDBFA, 0x4C32, 0xB1, 0x78, 0xC2, 0xF5, 0x68, 0xA7, 0x03, 0xB2);

    // IMMDeviceEnumerator CLSID: {BCDE0395-E52F-467C-8E3D-C4579291692E}
    private static Guid CLSID_MMDeviceEnumerator =
        new Guid(0xBCDE0395, 0xE52F, 0x467C, 0x8E, 0x3D, 0xC4, 0x57, 0x92, 0x91, 0x69, 0x2E);

    // IMMDeviceEnumerator IID: {A95664D2-9614-4F35-A746-DE8DB63617E6}
    private static Guid IID_IMMDeviceEnumerator =
        new Guid(0xA95664D2, 0x9614, 0x4F35, 0xA7, 0x46, 0xDE, 0x8D, 0xB6, 0x36, 0x17, 0xE6);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    /// <summary>
    /// NAudio MMDevice에서 IAudioClient COM 포인터 직접 획득
    /// CoCreateInstance → IMMDeviceEnumerator → GetDevice → Activate 순수 P/Invoke
    /// Marshal.GetComInterfaceForObject 완전 우회
    /// </summary>
    public static unsafe IntPtr ActivateAudioClient(NAudio.CoreAudioApi.MMDevice device)
    {
        // NAudio MMDevice에서 DeviceID 추출
        string deviceId = device.ID;
        Log4.Info($"[WasapiNative] DeviceId: {deviceId}");

        // CoCreateInstance로 IMMDeviceEnumerator 직접 생성
        var clsid = CLSID_MMDeviceEnumerator;
        var iid = IID_IMMDeviceEnumerator;
        const uint CLSCTX_ALL = 0x17;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iid, out IntPtr pEnumerator);
        Log4.Info($"[WasapiNative] CoCreateInstance HR=0x{hr:X8}, pEnumerator=0x{pEnumerator:X}");
        if (hr != 0)
            throw new InvalidOperationException($"IMMDeviceEnumerator 생성 실패: 0x{hr:X8}");

        try
        {
            // IMMDeviceEnumerator::GetDevice(deviceId, &pDevice) — vtbl[5]
            // vtbl: QI=0, AddRef=1, Release=2, EnumAudioEndpoints=3, GetDefaultAudioEndpoint=4, GetDevice=5
            var enumVtbl = *(IntPtr**)pEnumerator;
            var getDevice = (delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr*, int>)enumVtbl[5];

            IntPtr pDevice;
            fixed (char* pId = deviceId)
            {
                hr = getDevice(pEnumerator, pId, &pDevice);
            }
            Log4.Info($"[WasapiNative] GetDevice HR=0x{hr:X8}, pDevice=0x{pDevice:X}");
            if (hr != 0)
                throw new InvalidOperationException($"IMMDeviceEnumerator::GetDevice 실패: 0x{hr:X8}");

            try
            {
                // IMMDevice::Activate(IID_IAudioClient, CLSCTX_ALL, NULL, &pAudioClient) — vtbl[3]
                var devVtbl = *(IntPtr**)pDevice;
                var activate = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, IntPtr, IntPtr*, int>)devVtbl[3];

                var iidAudioClient = IID_IAudioClient;
                IntPtr pAudioClient;
                hr = activate(pDevice, &iidAudioClient, CLSCTX_ALL, IntPtr.Zero, &pAudioClient);
                Log4.Info($"[WasapiNative] Activate HR=0x{hr:X8}, pAudioClient=0x{pAudioClient:X}");
                if (hr != 0)
                    throw new InvalidOperationException($"IMMDevice::Activate 실패: 0x{hr:X8}");

                return pAudioClient;
            }
            finally
            {
                // IMMDevice::Release — vtbl[2]
                var devVtbl2 = *(IntPtr**)pDevice;
                var releaseDevice = (delegate* unmanaged[Stdcall]<IntPtr, uint>)devVtbl2[2];
                releaseDevice(pDevice);
            }
        }
        finally
        {
            // IMMDeviceEnumerator::Release — vtbl[2]
            var enumVtbl2 = *(IntPtr**)pEnumerator;
            var releaseEnum = (delegate* unmanaged[Stdcall]<IntPtr, uint>)enumVtbl2[2];
            releaseEnum(pEnumerator);
        }
    }

    /// <summary>
    /// COM 포인터 안전 Release — vtbl[2] 호출 후 IntPtr.Zero 대입
    /// </summary>
    public static unsafe void ComRelease(ref IntPtr pUnknown)
    {
        if (pUnknown == IntPtr.Zero) return;
        var vtbl = *(IntPtr**)pUnknown;
        var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtbl[2];
        release(pUnknown);
        pUnknown = IntPtr.Zero;
    }
}
