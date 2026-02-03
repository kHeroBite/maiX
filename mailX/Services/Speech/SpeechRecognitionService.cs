using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace mailX.Services.Speech;

/// <summary>
/// STT 모델 유형
/// </summary>
public enum STTModelType
{
    /// <summary>
    /// SenseVoice - 빠른 속도, 보통 정확도
    /// </summary>
    SenseVoice,

    /// <summary>
    /// Whisper (CPU) - 느린 속도, 높은 정확도
    /// </summary>
    Whisper,

    /// <summary>
    /// Whisper (GPU) - 빠른 속도, 높은 정확도 (CUDA 필요)
    /// </summary>
    WhisperGpu
}

/// <summary>
/// 음성 인식 (STT) 서비스
/// - SenseVoice (sherpa-onnx): 빠른 속도
/// - Whisper: 높은 정확도 + sherpa-onnx 화자분리
/// </summary>
public class SpeechRecognitionService : IDisposable
{
    // 모델 파일 경로
    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mailX", "models", "sherpa-onnx");

    private static readonly string WhisperModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mailX", "models", "whisper");

    // 한국어 모델 (SenseVoice - 다국어 지원)
    private const string SenseVoiceModelName = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17";

    // Whisper 모델 설정
    private const string WhisperModelFileName = "ggml-medium.bin";
    private const GgmlType WhisperModelType = GgmlType.Medium;

    // 화자분리 모델 설정
    private static readonly string SpeakerModelsDir = Path.Combine(ModelsDir, "speaker-diarization");
    private const string SegmentationModelName = "sherpa-onnx-pyannote-segmentation-3-0";
    private const string SegmentationModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2";
    private const string EmbeddingModelName = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";
    private const string EmbeddingModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";

    private SherpaOnnx.OfflineRecognizer? _recognizer;
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisperProcessor;
    private SherpaOnnx.OfflineSpeakerDiarization? _speakerDiarizer;
    private bool _isSenseVoiceInitialized;
    private bool _isWhisperInitialized;
    private bool _isWhisperGpuInitialized;
    private bool _isSpeakerDiarizationInitialized;
    private bool _useGpu;
    private bool _isDisposed;

    // 실시간 STT 화자 추적용
    private int _realtimeSpeakerIndex = 1;
    private TimeSpan _lastSegmentEndTime = TimeSpan.Zero;
    private const double SpeakerChangeSilenceThreshold = 2.0; // 2초 이상 침묵 시 화자 전환 가능성

    /// <summary>
    /// SenseVoice 초기화 완료 여부
    /// </summary>
    public bool IsSenseVoiceInitialized => _isSenseVoiceInitialized;

    /// <summary>
    /// Whisper (CPU) 초기화 완료 여부
    /// </summary>
    public bool IsWhisperInitialized => _isWhisperInitialized;

    /// <summary>
    /// Whisper (GPU) 초기화 완료 여부
    /// </summary>
    public bool IsWhisperGpuInitialized => _isWhisperGpuInitialized;

    /// <summary>
    /// 화자분리 초기화 완료 여부
    /// </summary>
    public bool IsSpeakerDiarizationInitialized => _isSpeakerDiarizationInitialized;

    /// <summary>
    /// 현재 GPU 사용 중 여부
    /// </summary>
    public bool IsUsingGpu => _useGpu;

    /// <summary>
    /// 초기화 완료 여부 (SenseVoice 기준 - 기존 호환성)
    /// </summary>
    public bool IsInitialized => _isSenseVoiceInitialized;

    /// <summary>
    /// STT 세그먼트 인식 이벤트
    /// </summary>
    public event Action<Models.TranscriptSegment>? SegmentRecognized;

    /// <summary>
    /// 전체 전사 업데이트 이벤트
    /// </summary>
    public event Action<string>? FullTranscriptUpdated;

    /// <summary>
    /// 진행률 업데이트 이벤트 (0.0 ~ 1.0)
    /// </summary>
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// 모델 다운로드 진행률 이벤트
    /// </summary>
    public event Action<double, string>? DownloadProgressChanged;

    /// <summary>
    /// STT 서비스 초기화 (SenseVoice 모델 로드)
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        return await InitializeSenseVoiceAsync(cancellationToken);
    }

    /// <summary>
    /// SenseVoice 모델 초기화
    /// </summary>
    public async Task<bool> InitializeSenseVoiceAsync(CancellationToken cancellationToken = default)
    {
        if (_isSenseVoiceInitialized)
            return true;

        try
        {
            Utils.Log4.Info("[STT] SenseVoice 서비스 초기화 시작...");

            if (!Directory.Exists(ModelsDir))
            {
                Directory.CreateDirectory(ModelsDir);
            }

            if (NeedsSenseVoiceModelDownload())
            {
                Utils.Log4.Info("[STT] SenseVoice 모델 파일 다운로드 필요");
                var downloaded = await DownloadSenseVoiceModelAsync(cancellationToken);
                if (!downloaded)
                {
                    Utils.Log4.Warn("[STT] SenseVoice 모델 다운로드 실패");
                    return false;
                }
            }

            var config = CreateOfflineRecognizerConfig();
            _recognizer = new SherpaOnnx.OfflineRecognizer(config);

            _isSenseVoiceInitialized = true;
            Utils.Log4.Info("[STT] SenseVoice 서비스 초기화 완료");
            return true;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] SenseVoice 서비스 초기화 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 화자분리 서비스 초기화 (sherpa-onnx OfflineSpeakerDiarization)
    /// </summary>
    public async Task<bool> InitializeSpeakerDiarizationAsync(CancellationToken cancellationToken = default)
    {
        if (_isSpeakerDiarizationInitialized && _speakerDiarizer != null)
            return true;

        try
        {
            Utils.Log4.Info("[STT] 화자분리 서비스 초기화 시작...");

            // 모델 다운로드 필요 여부 확인
            if (NeedsSpeakerDiarizationModelDownload())
            {
                Utils.Log4.Info("[STT] 화자분리 모델 파일 다운로드 필요");
                var downloaded = await DownloadSpeakerDiarizationModelsAsync(cancellationToken);
                if (!downloaded)
                {
                    Utils.Log4.Warn("[STT] 화자분리 모델 다운로드 실패 - 폴백 휴리스틱 사용");
                    return false;
                }
            }

            var segmentationModelPath = Path.Combine(SpeakerModelsDir, SegmentationModelName, "model.onnx");
            var embeddingModelPath = Path.Combine(SpeakerModelsDir, EmbeddingModelName);

            // sherpa-onnx 화자분리 설정
            var config = new SherpaOnnx.OfflineSpeakerDiarizationConfig
            {
                Segmentation = new SherpaOnnx.OfflineSpeakerSegmentationModelConfig
                {
                    Pyannote = new SherpaOnnx.OfflineSpeakerSegmentationPyannoteModelConfig
                    {
                        Model = segmentationModelPath
                    },
                    NumThreads = 4,
                    Debug = 0,
                    Provider = "cpu"
                },
                Embedding = new SherpaOnnx.SpeakerEmbeddingExtractorConfig
                {
                    Model = embeddingModelPath,
                    NumThreads = 4,
                    Debug = 0,
                    Provider = "cpu"
                },
                Clustering = new SherpaOnnx.FastClusteringConfig
                {
                    NumClusters = -1,  // 화자 수 자동 감지
                    Threshold = 0.99f  // 클러스터링 임계값 (0.97→0.99 상향: 6명 내외 회의 적합)
                },
                MinDurationOn = 0.3f,   // 최소 음성 구간 (초)
                MinDurationOff = 0.5f   // 최소 무음 구간 (초)
            };

            _speakerDiarizer = new SherpaOnnx.OfflineSpeakerDiarization(config);
            _isSpeakerDiarizationInitialized = true;

            Utils.Log4.Info($"[STT] 화자분리 서비스 초기화 완료 (SampleRate: {_speakerDiarizer.SampleRate}Hz)");
            return true;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] 화자분리 서비스 초기화 실패: {ex.Message}");
            Utils.Log4.Warn("[STT] 폴백 휴리스틱 화자분리 사용");
            return false;
        }
    }

    /// <summary>
    /// Whisper 모델 초기화 (CPU)
    /// </summary>
    public async Task<bool> InitializeWhisperAsync(CancellationToken cancellationToken = default)
    {
        return await InitializeWhisperAsync(useGpu: false, cancellationToken);
    }

    /// <summary>
    /// Whisper 모델 초기화 (GPU 옵션)
    /// </summary>
    public async Task<bool> InitializeWhisperAsync(bool useGpu, CancellationToken cancellationToken = default)
    {
        // 이미 같은 모드로 초기화되어 있으면 스킵
        if (useGpu && _isWhisperGpuInitialized)
            return true;
        if (!useGpu && _isWhisperInitialized)
            return true;

        // 다른 모드로 초기화되어 있으면 리소스 해제
        if (_whisperProcessor != null || _whisperFactory != null)
        {
            Utils.Log4.Info($"[STT] Whisper 모드 변경: {(_useGpu ? "GPU" : "CPU")} → {(useGpu ? "GPU" : "CPU")}");
            _whisperProcessor?.Dispose();
            _whisperProcessor = null;
            _whisperFactory?.Dispose();
            _whisperFactory = null;
            _isWhisperInitialized = false;
            _isWhisperGpuInitialized = false;
        }

        try
        {
            var modeStr = useGpu ? "GPU (CUDA)" : "CPU";
            Utils.Log4.Info($"[STT] Whisper 서비스 초기화 시작... (모드: {modeStr})");

            if (!Directory.Exists(WhisperModelsDir))
            {
                Directory.CreateDirectory(WhisperModelsDir);
            }

            var modelPath = Path.Combine(WhisperModelsDir, WhisperModelFileName);

            if (!File.Exists(modelPath))
            {
                Utils.Log4.Info("[STT] Whisper 모델 파일 다운로드 필요");
                var downloaded = await DownloadWhisperModelAsync(cancellationToken);
                if (!downloaded)
                {
                    Utils.Log4.Warn("[STT] Whisper 모델 다운로드 실패");
                    return false;
                }
            }

            // Whisper 프로세서 생성 (WhisperFactory를 필드로 유지해야 함)
            if (useGpu)
            {
                try
                {
                    // GPU 런타임 우선 사용 설정 (Intel Arc GPU = OpenVINO 최적화)
                    // 순서: OpenVINO (Intel GPU) > Vulkan (범용) > CUDA (NVIDIA) > CPU
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.OpenVino,  // Intel Arc GPU에 최적화
                        RuntimeLibrary.Vulkan,    // 범용 GPU 지원
                        RuntimeLibrary.Cuda,      // NVIDIA GPU
                        RuntimeLibrary.Cpu        // CPU 폴백
                    };
                    _whisperFactory = WhisperFactory.FromPath(modelPath);
                    var loadedRuntime = RuntimeOptions.LoadedLibrary;
                    Utils.Log4.Info($"[STT] Whisper GPU 런타임 로드 시도 (OpenVINO > Vulkan > CUDA > CPU), 실제 로드된 런타임: {loadedRuntime}");
                }
                catch (Exception ex)
                {
                    Utils.Log4.Warn($"[STT] Whisper GPU 초기화 실패, CPU로 폴백: {ex.Message}");
                    // GPU 실패 시 CPU로 폴백
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> { RuntimeLibrary.Cpu };
                    _whisperFactory = WhisperFactory.FromPath(modelPath);
                    useGpu = false;
                }
            }
            else
            {
                // CPU 런타임 강제 사용
                RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> { RuntimeLibrary.Cpu };
                _whisperFactory = WhisperFactory.FromPath(modelPath);
                var loadedRuntime = RuntimeOptions.LoadedLibrary;
                Utils.Log4.Info($"[STT] Whisper CPU 런타임 강제 사용, 실제 로드된 런타임: {loadedRuntime}");
            }

            _whisperProcessor = _whisperFactory.CreateBuilder()
                .WithLanguage("ko")
                .WithPrompt("한국어 음성을 정확하게 인식합니다.")
                .Build();

            _useGpu = useGpu;
            if (useGpu)
            {
                _isWhisperGpuInitialized = true;
            }
            else
            {
                _isWhisperInitialized = true;
            }

            Utils.Log4.Info($"[STT] Whisper 서비스 초기화 완료 (모드: {(useGpu ? "GPU" : "CPU")})");
            return true;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] Whisper 서비스 초기화 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 오프라인 인식기 설정 생성 (SenseVoice 모델)
    /// </summary>
    private SherpaOnnx.OfflineRecognizerConfig CreateOfflineRecognizerConfig()
    {
        var modelDir = Path.Combine(ModelsDir, SenseVoiceModelName);

        var config = new SherpaOnnx.OfflineRecognizerConfig
        {
            FeatConfig = new SherpaOnnx.FeatureConfig
            {
                SampleRate = 16000,
                FeatureDim = 80
            },
            ModelConfig = new SherpaOnnx.OfflineModelConfig
            {
                SenseVoice = new SherpaOnnx.OfflineSenseVoiceModelConfig
                {
                    Model = Path.Combine(modelDir, "model.int8.onnx"),
                    Language = "ko",
                    UseInverseTextNormalization = 1
                },
                Tokens = Path.Combine(modelDir, "tokens.txt"),
                NumThreads = 4,
                Provider = "cpu",
                Debug = 0
            },
            DecodingMethod = "greedy_search"
        };

        return config;
    }

    /// <summary>
    /// 오디오 파일을 텍스트로 변환 (모델 선택 가능)
    /// UI 스레드 블로킹 방지를 위해 백그라운드 스레드에서 실행
    /// </summary>
    public async Task<Models.TranscriptResult> TranscribeFileAsync(
        string audioPath,
        STTModelType modelType = STTModelType.SenseVoice,
        CancellationToken cancellationToken = default)
    {
        // Task.Run으로 백그라운드 스레드에서 실행하여 UI 블로킹 방지
        return await Task.Run(async () =>
        {
            return modelType switch
            {
                STTModelType.Whisper => await TranscribeWithWhisperAsync(audioPath, useGpu: false, cancellationToken),
                STTModelType.WhisperGpu => await TranscribeWithWhisperAsync(audioPath, useGpu: true, cancellationToken),
                _ => await TranscribeWithSenseVoiceAsync(audioPath, cancellationToken)
            };
        }, cancellationToken);
    }

    /// <summary>
    /// 기존 호환성 유지 - SenseVoice로 기본 전사
    /// </summary>
    public async Task<Models.TranscriptResult> TranscribeFileAsync(
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        return await TranscribeWithSenseVoiceAsync(audioPath, cancellationToken);
    }

    /// <summary>
    /// 실시간 오디오 청크 처리 (녹음 중 STT용)
    /// byte[] 형태의 raw PCM 데이터를 받아서 바로 STT 처리
    /// </summary>
    /// <param name="audioData">44100Hz, 16bit, Mono PCM 데이터</param>
    /// <param name="chunkStartTime">이 청크의 시작 시간</param>
    /// <param name="sampleRate">샘플레이트 (기본 44100)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>인식된 세그먼트 리스트 (비어있을 수 있음)</returns>
    public async Task<List<Models.TranscriptSegment>> ProcessRealtimeChunkAsync(
        byte[] audioData,
        TimeSpan chunkStartTime,
        int sampleRate = 44100,
        CancellationToken cancellationToken = default)
    {
        var segments = new List<Models.TranscriptSegment>();

        if (audioData == null || audioData.Length == 0)
            return segments;

        try
        {
            // SenseVoice 초기화 확인
            if (!_isSenseVoiceInitialized || _recognizer == null)
            {
                var initialized = await InitializeSenseVoiceAsync(cancellationToken);
                if (!initialized)
                {
                    Utils.Log4.Warn("[STT] 실시간 청크 처리 불가: SenseVoice 초기화 실패");
                    return segments;
                }
            }

            Utils.Log4.Debug($"[STT] 실시간 청크 처리 시작: {chunkStartTime:mm\\:ss}, {audioData.Length} bytes");

            // byte[] → float[] 변환 (16bit PCM)
            var sampleCount = audioData.Length / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(audioData[i * 2 + 1] << 8 | audioData[i * 2]);
                samples[i] = sample / 32768f;
            }

            // 16kHz로 리샘플링 필요
            const int targetSampleRate = 16000;
            float[] resampledSamples;

            if (sampleRate != targetSampleRate)
            {
                var ratio = (double)targetSampleRate / sampleRate;
                var targetLength = (int)(samples.Length * ratio);
                resampledSamples = new float[targetLength];

                for (int i = 0; i < targetLength; i++)
                {
                    var srcIndex = i / ratio;
                    var srcIndexInt = (int)srcIndex;
                    var frac = srcIndex - srcIndexInt;

                    if (srcIndexInt + 1 < samples.Length)
                    {
                        resampledSamples[i] = (float)(samples[srcIndexInt] * (1 - frac) + samples[srcIndexInt + 1] * frac);
                    }
                    else if (srcIndexInt < samples.Length)
                    {
                        resampledSamples[i] = samples[srcIndexInt];
                    }
                }
            }
            else
            {
                resampledSamples = samples;
            }

            // 음성 구간 감지
            var voiceSegments = DetectVoiceSegments(resampledSamples, targetSampleRate);

            if (voiceSegments.Count == 0)
            {
                // 음성 구간 없으면 전체를 하나로 처리
                voiceSegments.Add((0, resampledSamples.Length));
            }

            Utils.Log4.Debug($"[STT] 실시간 청크: 음성 구간 {voiceSegments.Count}개 감지");

            // 각 음성 구간 STT 수행
            // 실시간 STT에서는 일정 시간 이상 침묵 후 새 음성이면 화자 전환 가능성
            foreach (var (startSample, endSample) in voiceSegments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var length = endSample - startSample;
                if (length < targetSampleRate * 0.3) // 0.3초 미만 스킵
                    continue;

                var chunk = new float[length];
                Array.Copy(resampledSamples, startSample, chunk, 0, length);

                // 청크 내 시작/종료 시간
                var segmentStartOffset = TimeSpan.FromSeconds((double)startSample / targetSampleRate);
                var segmentEndOffset = TimeSpan.FromSeconds((double)endSample / targetSampleRate);

                // 전체 녹음 기준 시간
                var segmentStartTime = chunkStartTime + segmentStartOffset;
                var segmentEndTime = chunkStartTime + segmentEndOffset;

                // 화자 전환 감지: 이전 세그먼트와 일정 시간 이상 간격이 있으면 화자 전환 가능성
                // (단, 최대 화자 수 제한: 10명)
                if (_lastSegmentEndTime != TimeSpan.Zero)
                {
                    var silenceDuration = (segmentStartTime - _lastSegmentEndTime).TotalSeconds;
                    if (silenceDuration > SpeakerChangeSilenceThreshold && _realtimeSpeakerIndex < 10)
                    {
                        _realtimeSpeakerIndex++;
                        Utils.Log4.Debug($"[STT] 화자 전환 감지: {silenceDuration:F1}초 침묵 → 화자 {_realtimeSpeakerIndex}");
                    }
                }

                // STT 수행
                using var stream = _recognizer!.CreateStream();
                stream.AcceptWaveform(targetSampleRate, chunk);
                _recognizer.Decode(stream);
                var result = stream.Result;

                var text = result.Text?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    var segment = new Models.TranscriptSegment
                    {
                        StartTime = segmentStartTime,
                        EndTime = segmentEndTime,
                        Speaker = $"화자 {_realtimeSpeakerIndex}",
                        Text = text,
                        Confidence = 0.85
                    };

                    segments.Add(segment);
                    SegmentRecognized?.Invoke(segment);

                    // 마지막 세그먼트 종료 시간 업데이트
                    _lastSegmentEndTime = segmentEndTime;

                    var displayText = text.Length > 40 ? text[..40] + "..." : text;
                    Utils.Log4.Debug($"[STT] 실시간 세그먼트: [{segmentStartTime:mm\\:ss}] 화자{_realtimeSpeakerIndex}: {displayText}");
                }
            }

            Utils.Log4.Debug($"[STT] 실시간 청크 처리 완료: {segments.Count}개 세그먼트");
            return segments;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Debug("[STT] 실시간 청크 처리 취소됨");
            throw;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] 실시간 청크 처리 실패: {ex.Message}");
            return segments;
        }
    }

    // 긴 오디오 파일 청크 처리 설정
    private const int CHUNK_DURATION_SECONDS = 180; // 3분 단위로 청크 분할
    private const int CHUNK_OVERLAP_SECONDS = 5; // 청크 간 5초 오버랩 (문맥 유지)

    /// <summary>
    /// Whisper + sherpa-onnx 화자분리 통합 STT
    /// 긴 오디오 파일(1~2시간)도 청크 단위로 분할하여 안정적으로 처리
    /// </summary>
    private async Task<Models.TranscriptResult> TranscribeWithWhisperAsync(
        string audioPath,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        // Whisper 초기화 확인 (GPU/CPU 모드에 따라)
        var needsInit = useGpu ? !_isWhisperGpuInitialized : !_isWhisperInitialized;
        var modeChanged = _whisperProcessor != null && _useGpu != useGpu;

        if (needsInit || modeChanged)
        {
            var initialized = await InitializeWhisperAsync(useGpu, cancellationToken);
            if (!initialized)
            {
                throw new InvalidOperationException($"Whisper 모델 초기화에 실패했습니다. (모드: {(useGpu ? "GPU" : "CPU")})");
            }
        }

        // SenseVoice도 초기화 (화자분리용)
        if (!_isSenseVoiceInitialized)
        {
            await InitializeSenseVoiceAsync(cancellationToken);
        }

        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("오디오 파일을 찾을 수 없습니다.", audioPath);
        }

        var modeStr = _useGpu ? "GPU" : "CPU";
        Utils.Log4.Info($"[STT] Whisper ({modeStr}) 파일 분석 시작: {audioPath}");

        var result = new Models.TranscriptResult
        {
            AudioFilePath = audioPath,
            CreatedAt = DateTime.Now,
            ModelName = $"Whisper (medium, {modeStr}) + 화자분리",
            Language = "ko"
        };

        try
        {
            // 오디오 파일 로드 및 16kHz 모노로 변환
            var (samples, sampleRate, duration) = await LoadAndConvertAudioAsync(audioPath, cancellationToken);
            result.TotalDuration = duration;

            Utils.Log4.Info($"[STT] 오디오 로드 완료: {duration}, {samples.Length} 샘플, {sampleRate}Hz");
            ProgressChanged?.Invoke(0.05);

            // 1. sherpa-onnx VAD로 음성 구간 감지
            var voiceSegments = DetectVoiceSegments(samples, sampleRate);
            Utils.Log4.Info($"[STT] 음성 구간 {voiceSegments.Count}개 감지됨");

            if (voiceSegments.Count == 0)
            {
                voiceSegments.Add((0, samples.Length));
            }

            ProgressChanged?.Invoke(0.1);

            // 2. sherpa-onnx로 화자분리 (비동기)
            var speakerLabels = await PerformSpeakerDiarizationAsync(samples, sampleRate, voiceSegments, cancellationToken);
            ProgressChanged?.Invoke(0.15);

            // 3. Whisper로 오디오 STT 수행 (청크 단위 처리)
            Utils.Log4.Info("[STT] Whisper 전사 시작...");

            var whisperSegments = new List<(TimeSpan start, TimeSpan end, string text)>();

            // 청크 분할 계산
            var chunkSamples = CHUNK_DURATION_SECONDS * sampleRate;
            var overlapSamples = CHUNK_OVERLAP_SECONDS * sampleRate;
            var totalChunks = (int)Math.Ceiling((double)samples.Length / (chunkSamples - overlapSamples));
            var totalDurationSeconds = (double)samples.Length / sampleRate;

            Utils.Log4.Info($"[STT] 총 {totalChunks}개 청크로 분할 처리 (각 {CHUNK_DURATION_SECONDS}초, {CHUNK_OVERLAP_SECONDS}초 오버랩)");

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunkStartSample = chunkIndex * (chunkSamples - overlapSamples);
                var chunkEndSample = Math.Min(chunkStartSample + chunkSamples, samples.Length);
                var chunkLength = chunkEndSample - chunkStartSample;

                // 청크 시작 시간 (오프셋)
                var chunkStartTime = TimeSpan.FromSeconds((double)chunkStartSample / sampleRate);

                // 청크 시작 시 진행률 업데이트 (전체 오디오 시간 기준)
                var chunkStartProgress = 0.15 + (0.7 * chunkStartTime.TotalSeconds / totalDurationSeconds);
                ProgressChanged?.Invoke(Math.Min(chunkStartProgress, 0.85));

                Utils.Log4.Info($"[STT] 청크 {chunkIndex + 1}/{totalChunks} 처리 중 (시작: {chunkStartTime:hh\\:mm\\:ss})...");

                // 청크 데이터 추출
                var chunkData = new float[chunkLength];
                Array.Copy(samples, chunkStartSample, chunkData, 0, chunkLength);

                // 청크를 임시 WAV 파일로 저장
                var tempWavPath = Path.Combine(Path.GetTempPath(), $"whisper_chunk_{Guid.NewGuid()}.wav");
                try
                {
                    await SaveAsWavAsync(chunkData, sampleRate, tempWavPath, cancellationToken);

                    // Whisper 전사 수행
                    using var fileStream = File.OpenRead(tempWavPath);
                    await foreach (var segment in _whisperProcessor!.ProcessAsync(fileStream, cancellationToken))
                    {
                        if (!string.IsNullOrWhiteSpace(segment.Text))
                        {
                            // 전체 오디오 기준 시간으로 변환
                            var absoluteStart = chunkStartTime + segment.Start;
                            var absoluteEnd = chunkStartTime + segment.End;

                            // 세그먼트 처리 시 진행률 업데이트 (실시간)
                            var segmentProgress = 0.15 + (0.7 * absoluteEnd.TotalSeconds / totalDurationSeconds);
                            ProgressChanged?.Invoke(Math.Min(segmentProgress, 0.85));

                            // 오버랩 구간에서 중복 방지
                            if (chunkIndex > 0)
                            {
                                var overlapThreshold = TimeSpan.FromSeconds(CHUNK_OVERLAP_SECONDS);
                                if (segment.Start < overlapThreshold)
                                {
                                    // 이전 청크와 겹치는 세그먼트 - 중복 체크
                                    var isDuplicate = whisperSegments.Any(ws =>
                                        Math.Abs((ws.end - absoluteStart).TotalSeconds) < 2 &&
                                        IsSimilarText(ws.text, segment.Text.Trim()));

                                    if (isDuplicate)
                                        continue;
                                }
                            }

                            whisperSegments.Add((absoluteStart, absoluteEnd, segment.Text.Trim()));
                        }
                    }

                    // 청크 완료 시 진행률 업데이트
                    var chunkEndProgress = 0.15 + (0.7 * (chunkIndex + 1) / totalChunks);
                    ProgressChanged?.Invoke(Math.Min(chunkEndProgress, 0.85));
                }
                finally
                {
                    // 임시 파일 삭제
                    if (File.Exists(tempWavPath))
                    {
                        try { File.Delete(tempWavPath); } catch { }
                    }
                }

                // 메모리 정리를 위한 잠시 대기
                await Task.Delay(100, cancellationToken);
                GC.Collect(0, GCCollectionMode.Optimized);
            }

            Utils.Log4.Info($"[STT] Whisper 전사 완료: {whisperSegments.Count}개 세그먼트");

            // 3.5. 화자분리 적용 전 원본 세그먼트 저장 (비교용)
            result.SegmentsBeforeDiarization = new List<Models.TranscriptSegment>();
            foreach (var (start, end, text) in whisperSegments.OrderBy(s => s.start))
            {
                result.SegmentsBeforeDiarization.Add(new Models.TranscriptSegment
                {
                    StartTime = start,
                    EndTime = end,
                    Speaker = "화자", // 화자분리 전이므로 단일 화자로 표시
                    Text = text,
                    Confidence = 0.95
                });
            }

            // 4. Whisper 결과와 화자분리 결과 병합
            ProgressChanged?.Invoke(0.9);

            foreach (var (start, end, text) in whisperSegments.OrderBy(s => s.start))
            {
                // 해당 시간대의 화자 레이블 찾기
                var speaker = FindSpeakerForTimeRange(start, end, voiceSegments, speakerLabels, sampleRate);

                var segment = new Models.TranscriptSegment
                {
                    StartTime = start,
                    EndTime = end,
                    Speaker = speaker,
                    Text = text,
                    Confidence = 0.95 // Whisper는 높은 정확도
                };

                result.Segments.Add(segment);
                SegmentRecognized?.Invoke(segment);

                var displayText = text.Length > 50 ? text[..50] + "..." : text;
                Utils.Log4.Debug($"[STT] Whisper 세그먼트: [{start:hh\\:mm\\:ss} - {end:hh\\:mm\\:ss}] {speaker}: {displayText}");
            }

            // 화자 목록 추출
            result.Speakers = result.Segments
                .Select(s => s.Speaker)
                .Distinct()
                .ToList();

            FullTranscriptUpdated?.Invoke(result.FullText);
            ProgressChanged?.Invoke(1.0);

            Utils.Log4.Info($"[STT] Whisper 분석 완료: {result.Segments.Count}개 세그먼트, 화자 {result.Speakers.Count}명");

            return result;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Warn("[STT] Whisper 분석 취소됨");
            throw;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] Whisper 파일 분석 실패: {audioPath} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 텍스트 유사도 체크 (중복 세그먼트 방지)
    /// </summary>
    private static bool IsSimilarText(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return false;

        // 짧은 텍스트는 완전 일치만 체크
        if (text1.Length < 10 || text2.Length < 10)
            return text1 == text2;

        // Jaccard 유사도로 비교 (단어 기반)
        var words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        var similarity = union > 0 ? (double)intersection / union : 0;
        return similarity > 0.7; // 70% 이상 유사하면 중복으로 판단
    }

    /// <summary>
    /// 시간 범위에 해당하는 화자 찾기
    /// </summary>
    private string FindSpeakerForTimeRange(
        TimeSpan start,
        TimeSpan end,
        List<(int start, int end)> voiceSegments,
        Dictionary<int, string> speakerLabels,
        int sampleRate)
    {
        var startSample = (int)(start.TotalSeconds * sampleRate);
        var endSample = (int)(end.TotalSeconds * sampleRate);
        var midSample = (startSample + endSample) / 2;

        // 가장 많이 겹치는 세그먼트의 화자 찾기
        var speakerCounts = new Dictionary<string, int>();

        for (int i = 0; i < voiceSegments.Count; i++)
        {
            var (segStart, segEnd) = voiceSegments[i];

            // 겹치는 구간 계산
            var overlapStart = Math.Max(startSample, segStart);
            var overlapEnd = Math.Min(endSample, segEnd);

            if (overlapEnd > overlapStart)
            {
                var speaker = speakerLabels.ContainsKey(i) ? speakerLabels[i] : "화자 1";
                var overlap = overlapEnd - overlapStart;

                if (!speakerCounts.ContainsKey(speaker))
                    speakerCounts[speaker] = 0;
                speakerCounts[speaker] += overlap;
            }
        }

        if (speakerCounts.Count > 0)
        {
            return speakerCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        return "화자 1";
    }

    /// <summary>
    /// 샘플 데이터를 WAV 파일로 저장 (PCM 16bit 형식 - Whisper 호환)
    /// </summary>
    private async Task SaveAsWavAsync(float[] samples, int sampleRate, string outputPath, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            // Whisper.net은 PCM 16bit WAV만 지원
            var format = new WaveFormat(sampleRate, 16, 1);
            using var writer = new WaveFileWriter(outputPath, format);

            // float를 16bit PCM으로 변환하여 쓰기
            const int chunkSize = 16000; // 1초 분량
            var pcmBuffer = new short[chunkSize];

            for (int i = 0; i < samples.Length; i += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(chunkSize, samples.Length - i);

                // float (-1.0 ~ 1.0) → short (-32768 ~ 32767)
                for (int j = 0; j < length; j++)
                {
                    var sample = samples[i + j];
                    // 클리핑 방지
                    sample = Math.Max(-1.0f, Math.Min(1.0f, sample));
                    pcmBuffer[j] = (short)(sample * 32767);
                }

                // byte 배열로 변환하여 쓰기
                var byteBuffer = new byte[length * 2];
                Buffer.BlockCopy(pcmBuffer, 0, byteBuffer, 0, length * 2);
                writer.Write(byteBuffer, 0, length * 2);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// SenseVoice로 STT 수행 (기존 메서드)
    /// </summary>
    private async Task<Models.TranscriptResult> TranscribeWithSenseVoiceAsync(
        string audioPath,
        CancellationToken cancellationToken)
    {
        if (!_isSenseVoiceInitialized || _recognizer == null)
        {
            var initialized = await InitializeSenseVoiceAsync(cancellationToken);
            if (!initialized)
            {
                throw new InvalidOperationException("STT 서비스가 초기화되지 않았습니다. InitializeAsync()를 먼저 호출하세요.");
            }
        }

        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("오디오 파일을 찾을 수 없습니다.", audioPath);
        }

        Utils.Log4.Info($"[STT] SenseVoice 파일 분석 시작: {audioPath}");

        var result = new Models.TranscriptResult
        {
            AudioFilePath = audioPath,
            CreatedAt = DateTime.Now,
            ModelName = "SenseVoice",
            Language = "ko"
        };

        try
        {
            // 오디오 파일 로드 및 16kHz 모노로 변환
            var (samples, sampleRate, duration) = await LoadAndConvertAudioAsync(audioPath, cancellationToken);
            result.TotalDuration = duration;

            Utils.Log4.Info($"[STT] 오디오 로드 완료: {duration}, {samples.Length} 샘플, {sampleRate}Hz");

            ProgressChanged?.Invoke(0.1);

            // 음성 구간 감지 (VAD) 및 세그먼트 분할
            var voiceSegments = DetectVoiceSegments(samples, sampleRate);
            Utils.Log4.Info($"[STT] 음성 구간 {voiceSegments.Count}개 감지됨");

            if (voiceSegments.Count == 0)
            {
                // 음성 구간이 감지되지 않으면 전체를 하나의 세그먼트로 처리
                voiceSegments.Add((0, samples.Length));
            }

            // 화자 분리를 위한 음성 특성 분석 (비동기)
            var speakerLabels = await PerformSpeakerDiarizationAsync(samples, sampleRate, voiceSegments, cancellationToken);

            // 각 음성 구간에 대해 STT 수행
            var totalSegments = voiceSegments.Count;
            for (int i = 0; i < totalSegments; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (startSample, endSample) = voiceSegments[i];
                var length = endSample - startSample;

                // 너무 짧은 세그먼트는 스킵
                if (length < sampleRate * 0.3) // 0.3초 미만
                    continue;

                var chunk = new float[length];
                Array.Copy(samples, startSample, chunk, 0, length);

                // 시작/종료 시간 계산
                var startTime = TimeSpan.FromSeconds((double)startSample / sampleRate);
                var endTime = TimeSpan.FromSeconds((double)endSample / sampleRate);

                // STT 수행
                using var stream = _recognizer!.CreateStream();
                stream.AcceptWaveform(sampleRate, chunk);
                _recognizer.Decode(stream);
                var recognitionResult = stream.Result;

                var text = recognitionResult.Text?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    var speaker = speakerLabels.ContainsKey(i) ? speakerLabels[i] : "화자 1";

                    var segment = new Models.TranscriptSegment
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Speaker = speaker,
                        Text = text,
                        Confidence = 0.9
                    };

                    result.Segments.Add(segment);
                    SegmentRecognized?.Invoke(segment);

                    var displayText = text.Length > 50 ? text[..50] + "..." : text;
                    Utils.Log4.Debug($"[STT] 세그먼트 {i}: [{startTime:mm\\:ss} - {endTime:mm\\:ss}] {speaker}: {displayText}");
                }

                // 진행률 업데이트
                var progress = 0.1 + (0.9 * (i + 1) / totalSegments);
                ProgressChanged?.Invoke(progress);

                // UI 업데이트 기회 제공
                await Task.Delay(10, cancellationToken);
            }

            // 화자 목록 추출
            result.Speakers = result.Segments
                .Select(s => s.Speaker)
                .Distinct()
                .ToList();

            FullTranscriptUpdated?.Invoke(result.FullText);
            ProgressChanged?.Invoke(1.0);

            Utils.Log4.Info($"[STT] SenseVoice 분석 완료: {result.Segments.Count}개 세그먼트, 화자 {result.Speakers.Count}명");

            return result;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Warn("[STT] SenseVoice 분석 취소됨");
            throw;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] SenseVoice 파일 분석 실패: {audioPath} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 음성 구간 감지 (VAD - Voice Activity Detection)
    /// 에너지 기반 간단한 VAD 구현
    /// </summary>
    private List<(int start, int end)> DetectVoiceSegments(float[] samples, int sampleRate)
    {
        var segments = new List<(int start, int end)>();

        // 프레임 설정
        var frameSize = sampleRate / 50; // 20ms 프레임
        var hopSize = frameSize / 2; // 50% 오버랩
        var minSilenceDuration = sampleRate / 2; // 0.5초 침묵으로 세그먼트 분리
        var minSegmentDuration = sampleRate; // 최소 1초 세그먼트

        // 에너지 계산
        var energies = new List<double>();
        for (int i = 0; i < samples.Length - frameSize; i += hopSize)
        {
            double energy = 0;
            for (int j = 0; j < frameSize; j++)
            {
                energy += samples[i + j] * samples[i + j];
            }
            energies.Add(energy / frameSize);
        }

        if (energies.Count == 0)
            return segments;

        // 에너지 임계값 계산 (상위 90%의 평균의 10%)
        var sortedEnergies = energies.OrderByDescending(e => e).ToList();
        var topEnergies = sortedEnergies.Take(sortedEnergies.Count / 10).ToList();
        var threshold = topEnergies.Count > 0 ? topEnergies.Average() * 0.1 : 0.001;

        // 음성 구간 감지
        int? segmentStart = null;
        int silenceCount = 0;

        for (int i = 0; i < energies.Count; i++)
        {
            var sampleIndex = i * hopSize;

            if (energies[i] > threshold)
            {
                if (segmentStart == null)
                {
                    segmentStart = sampleIndex;
                }
                silenceCount = 0;
            }
            else
            {
                silenceCount += hopSize;

                if (segmentStart != null && silenceCount > minSilenceDuration)
                {
                    var end = sampleIndex - silenceCount + hopSize;
                    if (end - segmentStart.Value > minSegmentDuration)
                    {
                        segments.Add((segmentStart.Value, end));
                    }
                    segmentStart = null;
                    silenceCount = 0;
                }
            }
        }

        // 마지막 세그먼트 처리
        if (segmentStart != null)
        {
            var end = samples.Length;
            if (end - segmentStart.Value > minSegmentDuration)
            {
                segments.Add((segmentStart.Value, end));
            }
        }

        // 세그먼트가 너무 많으면 병합 (최대 30초 단위)
        var maxSegmentLength = sampleRate * 30;
        var mergedSegments = new List<(int start, int end)>();

        foreach (var seg in segments)
        {
            if (seg.end - seg.start > maxSegmentLength)
            {
                // 긴 세그먼트 분할
                for (int start = seg.start; start < seg.end; start += maxSegmentLength)
                {
                    var end = Math.Min(start + maxSegmentLength, seg.end);
                    mergedSegments.Add((start, end));
                }
            }
            else
            {
                mergedSegments.Add(seg);
            }
        }

        return mergedSegments;
    }

    /// <summary>
    /// 화자 분리 (Speaker Diarization)
    /// 간단한 에너지/음높이 기반 화자 구분
    /// </summary>
    private async Task<Dictionary<int, string>> PerformSpeakerDiarizationAsync(
        float[] samples,
        int sampleRate,
        List<(int start, int end)> segments,
        CancellationToken cancellationToken = default)
    {
        // sherpa-onnx 화자분리 초기화 시도
        if (!_isSpeakerDiarizationInitialized)
        {
            await InitializeSpeakerDiarizationAsync(cancellationToken);
        }

        // sherpa-onnx 화자분리 사용 가능한 경우
        if (_speakerDiarizer != null && _isSpeakerDiarizationInitialized)
        {
            try
            {
                Utils.Log4.Info("[STT] sherpa-onnx 화자분리 수행 중...");

                // 샘플레이트가 다르면 리샘플링 필요
                float[] processedSamples = samples;
                if (sampleRate != _speakerDiarizer.SampleRate)
                {
                    Utils.Log4.Debug($"[STT] 화자분리용 리샘플링: {sampleRate}Hz → {_speakerDiarizer.SampleRate}Hz");
                    processedSamples = ResampleAudio(samples, sampleRate, _speakerDiarizer.SampleRate);
                }

                // 화자분리 수행
                var diarizationResult = _speakerDiarizer.Process(processedSamples);

                // 화자 수 카운트
                var speakers = new HashSet<int>();
                foreach (var segment in diarizationResult)
                {
                    speakers.Add(segment.Speaker);
                }

                Utils.Log4.Info($"[STT] 화자분리 완료: {diarizationResult.Length}개 구간, {speakers.Count}명 화자 감지");

                // 세그먼트 인덱스별 화자 레이블 매핑
                var speakerLabels = new Dictionary<int, string>();
                var targetSampleRate = _speakerDiarizer.SampleRate;

                for (int i = 0; i < segments.Count; i++)
                {
                    var (segStart, segEnd) = segments[i];

                    // 원본 샘플레이트 기준 시간으로 변환
                    var segStartTime = (float)segStart / sampleRate;
                    var segEndTime = (float)segEnd / sampleRate;

                    // 해당 시간대와 가장 많이 겹치는 화자 찾기
                    var speakerVotes = new Dictionary<int, float>();

                    foreach (var diaSegment in diarizationResult)
                    {
                        // 겹치는 구간 계산
                        var overlapStart = Math.Max(segStartTime, diaSegment.Start);
                        var overlapEnd = Math.Min(segEndTime, diaSegment.End);
                        var overlapDuration = overlapEnd - overlapStart;

                        if (overlapDuration > 0)
                        {
                            if (!speakerVotes.ContainsKey(diaSegment.Speaker))
                                speakerVotes[diaSegment.Speaker] = 0;
                            speakerVotes[diaSegment.Speaker] += overlapDuration;
                        }
                    }

                    if (speakerVotes.Count > 0)
                    {
                        var dominantSpeaker = speakerVotes.OrderByDescending(kv => kv.Value).First().Key;
                        speakerLabels[i] = $"화자 {dominantSpeaker + 1}";
                    }
                    else
                    {
                        speakerLabels[i] = "화자 1";
                    }
                }

                return speakerLabels;
            }
            catch (Exception ex)
            {
                Utils.Log4.Warn($"[STT] sherpa-onnx 화자분리 실패, 폴백 사용: {ex.Message}");
            }
        }

        // 폴백: 기존 휴리스틱 방식 (개선 버전)
        Utils.Log4.Debug("[STT] 휴리스틱 화자분리 사용 (폴백)");
        return PerformSpeakerDiarizationFallback(samples, sampleRate, segments);
    }

    /// <summary>
    /// 오디오 리샘플링 (선형 보간)
    /// </summary>
    private float[] ResampleAudio(float[] samples, int sourceSampleRate, int targetSampleRate)
    {
        if (sourceSampleRate == targetSampleRate)
            return samples;

        var ratio = (double)targetSampleRate / sourceSampleRate;
        var newLength = (int)(samples.Length * ratio);
        var resampled = new float[newLength];

        for (int i = 0; i < newLength; i++)
        {
            var srcIndex = i / ratio;
            var srcIndexFloor = (int)srcIndex;
            var srcIndexCeil = Math.Min(srcIndexFloor + 1, samples.Length - 1);
            var fraction = srcIndex - srcIndexFloor;

            resampled[i] = (float)(samples[srcIndexFloor] * (1 - fraction) + samples[srcIndexCeil] * fraction);
        }

        return resampled;
    }

    /// <summary>
    /// 폴백 화자분리 (개선된 휴리스틱 방식)
    /// - 에너지와 ZCR 기반으로 50:50 분배
    /// </summary>
    private Dictionary<int, string> PerformSpeakerDiarizationFallback(
        float[] samples,
        int sampleRate,
        List<(int start, int end)> segments)
    {
        var speakerLabels = new Dictionary<int, string>();
        var speakerFeatures = new List<(int segmentIndex, double avgEnergy, double zeroCrossRate)>();

        // 각 세그먼트의 특성 추출
        for (int i = 0; i < segments.Count; i++)
        {
            var (start, end) = segments[i];
            var length = end - start;

            // 평균 에너지
            double energy = 0;
            for (int j = start; j < end; j++)
            {
                energy += Math.Abs(samples[j]);
            }
            var avgEnergy = energy / length;

            // 제로 크로싱 비율 (음높이 추정)
            int zeroCrossings = 0;
            for (int j = start + 1; j < end; j++)
            {
                if ((samples[j] > 0 && samples[j - 1] < 0) ||
                    (samples[j] < 0 && samples[j - 1] > 0))
                {
                    zeroCrossings++;
                }
            }
            var zeroCrossRate = (double)zeroCrossings / length * sampleRate;

            speakerFeatures.Add((i, avgEnergy, zeroCrossRate));
        }

        if (speakerFeatures.Count == 0)
            return speakerLabels;

        // 개선된 클러스터링 (50:50 분배)
        var meanEnergy = speakerFeatures.Average(f => f.avgEnergy);
        var meanZCR = speakerFeatures.Average(f => f.zeroCrossRate);

        foreach (var feature in speakerFeatures)
        {
            // 에너지와 ZCR을 기반으로 화자 구분 (개선: XOR 조건으로 50:50 분배)
            var energyScore = feature.avgEnergy > meanEnergy ? 1 : 0;
            var zcrScore = feature.zeroCrossRate > meanZCR ? 1 : 0;

            // 개선: energyScore != zcrScore면 화자 1, 같으면 화자 2 (50:50 분배)
            var speakerNum = energyScore != zcrScore ? 1 : 2;
            speakerLabels[feature.segmentIndex] = $"화자 {speakerNum}";
        }

        // 연속된 동일 화자 세그먼트 확인 (급격한 변화 방지)
        for (int i = 1; i < segments.Count - 1; i++)
        {
            if (speakerLabels.ContainsKey(i - 1) && speakerLabels.ContainsKey(i + 1))
            {
                if (speakerLabels[i - 1] == speakerLabels[i + 1] &&
                    speakerLabels[i] != speakerLabels[i - 1])
                {
                    // 짧은 세그먼트는 이전/다음 화자와 동일하게
                    var (start, end) = segments[i];
                    if (end - start < sampleRate * 2) // 2초 미만
                    {
                        speakerLabels[i] = speakerLabels[i - 1];
                    }
                }
            }
        }

        return speakerLabels;
    }

    /// <summary>
    /// 오디오 파일 로드 및 16kHz 모노 PCM으로 변환
    /// </summary>
    private async Task<(float[] samples, int sampleRate, TimeSpan duration)> LoadAndConvertAudioAsync(
        string audioPath,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            const int targetSampleRate = 16000;
            var extension = Path.GetExtension(audioPath).ToLowerInvariant();

            // 오디오 파일 읽기
            WaveStream reader;
            if (extension == ".wma" || extension == ".m4a" || extension == ".aac")
            {
                reader = new MediaFoundationReader(audioPath);
            }
            else
            {
                reader = new AudioFileReader(audioPath);
            }

            using (reader)
            {
                var duration = reader.TotalTime;
                var originalFormat = reader.WaveFormat;

                Utils.Log4.Debug($"[STT] 원본 오디오: {originalFormat.Channels}ch, {originalFormat.SampleRate}Hz, {originalFormat.BitsPerSample}bit");

                // 16kHz 모노로 리샘플링
                ISampleProvider sampleProvider;

                if (reader is AudioFileReader audioFileReader)
                {
                    sampleProvider = audioFileReader;
                }
                else
                {
                    sampleProvider = reader.ToSampleProvider();
                }

                // 모노로 변환
                if (sampleProvider.WaveFormat.Channels > 1)
                {
                    sampleProvider = sampleProvider.ToMono();
                }

                // 리샘플링이 필요한 경우
                var sourceSampleRate = sampleProvider.WaveFormat.SampleRate;
                var samples = new List<float>();
                var buffer = new float[sourceSampleRate]; // 1초 버퍼

                int samplesRead;
                while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (sourceSampleRate != targetSampleRate)
                    {
                        // 간단한 리샘플링 (선형 보간)
                        var ratio = (double)targetSampleRate / sourceSampleRate;
                        var targetLength = (int)(samplesRead * ratio);

                        for (int i = 0; i < targetLength; i++)
                        {
                            var srcIndex = i / ratio;
                            var srcIndexInt = (int)srcIndex;
                            var frac = srcIndex - srcIndexInt;

                            if (srcIndexInt + 1 < samplesRead)
                            {
                                var interpolated = buffer[srcIndexInt] * (1 - frac) + buffer[srcIndexInt + 1] * frac;
                                samples.Add((float)interpolated);
                            }
                            else if (srcIndexInt < samplesRead)
                            {
                                samples.Add(buffer[srcIndexInt]);
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < samplesRead; i++)
                        {
                            samples.Add(buffer[i]);
                        }
                    }
                }

                return (samples.ToArray(), targetSampleRate, duration);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// SenseVoice 모델 다운로드 필요 여부 확인
    /// </summary>
    public bool NeedsSenseVoiceModelDownload()
    {
        var modelDir = Path.Combine(ModelsDir, SenseVoiceModelName);
        var modelPath = Path.Combine(modelDir, "model.int8.onnx");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");

        return !File.Exists(modelPath) || !File.Exists(tokensPath);
    }

    /// <summary>
    /// 기존 호환성 유지
    /// </summary>
    public bool NeedsModelDownload() => NeedsSenseVoiceModelDownload();

    /// <summary>
    /// Whisper 모델 다운로드 필요 여부 확인
    /// </summary>
    public bool NeedsWhisperModelDownload()
    {
        var modelPath = Path.Combine(WhisperModelsDir, WhisperModelFileName);
        return !File.Exists(modelPath);
    }

    /// <summary>
    /// SenseVoice 모델 다운로드
    /// </summary>
    public async Task<bool> DownloadSenseVoiceModelAsync(CancellationToken cancellationToken = default)
    {
        var modelDir = Path.Combine(ModelsDir, SenseVoiceModelName);

        try
        {
            Utils.Log4.Info($"[STT] SenseVoice 모델 다운로드 시작: {SenseVoiceModelName}");
            DownloadProgressChanged?.Invoke(0, "SenseVoice 모델 다운로드 준비 중...");

            if (!Directory.Exists(modelDir))
            {
                Directory.CreateDirectory(modelDir);
            }

            var baseUrl = $"https://huggingface.co/csukuangfj/{SenseVoiceModelName}/resolve/main";

            var filesToDownload = new[]
            {
                ("model.int8.onnx", "SenseVoice 모델 파일 (239MB)"),
                ("tokens.txt", "토큰 파일")
            };

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(30);

            for (int i = 0; i < filesToDownload.Length; i++)
            {
                var (fileName, description) = filesToDownload[i];
                var url = $"{baseUrl}/{fileName}";
                var localPath = Path.Combine(modelDir, fileName);

                if (File.Exists(localPath))
                {
                    Utils.Log4.Debug($"[STT] 파일 이미 존재: {fileName}");
                    continue;
                }

                Utils.Log4.Info($"[STT] 다운로드: {fileName}");
                DownloadProgressChanged?.Invoke((double)i / filesToDownload.Length, $"{description} 다운로드 중...");

                try
                {
                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    var downloadedBytes = 0L;

                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var fileProgress = (double)downloadedBytes / totalBytes;
                            var overallProgress = (i + fileProgress) / filesToDownload.Length;
                            DownloadProgressChanged?.Invoke(overallProgress,
                                $"{description} 다운로드 중... ({downloadedBytes / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB)");
                        }
                    }

                    Utils.Log4.Info($"[STT] 다운로드 완료: {fileName}");
                }
                catch (Exception ex)
                {
                    Utils.Log4.Error($"[STT] 파일 다운로드 실패: {fileName} - {ex.Message}");

                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }

                    throw;
                }
            }

            DownloadProgressChanged?.Invoke(1.0, "SenseVoice 다운로드 완료");
            Utils.Log4.Info("[STT] SenseVoice 모델 다운로드 완료");
            return true;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Warn("[STT] SenseVoice 모델 다운로드 취소됨");
            return false;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] SenseVoice 모델 다운로드 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 기존 호환성 유지
    /// </summary>
    public async Task<bool> DownloadModelAsync(CancellationToken cancellationToken = default)
    {
        return await DownloadSenseVoiceModelAsync(cancellationToken);
    }

    /// <summary>
    /// Whisper 모델 다운로드 (Whisper.net 내장 다운로더 사용)
    /// </summary>
    public async Task<bool> DownloadWhisperModelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Utils.Log4.Info("[STT] Whisper 모델 다운로드 시작 (medium 모델)...");
            DownloadProgressChanged?.Invoke(0, "Whisper 모델 다운로드 준비 중...");

            if (!Directory.Exists(WhisperModelsDir))
            {
                Directory.CreateDirectory(WhisperModelsDir);
            }

            var modelPath = Path.Combine(WhisperModelsDir, WhisperModelFileName);

            // Whisper.net 내장 다운로더 사용
            DownloadProgressChanged?.Invoke(0.1, "Whisper medium 모델 다운로드 중 (1.5GB)...");

            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(WhisperModelType);

            using var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int bytesRead;
            long totalBytesRead = 0;

            while ((bytesRead = await modelStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                // 대략적인 진행률 (medium 모델은 약 1.5GB)
                var progress = Math.Min(0.1 + (0.9 * totalBytesRead / (1500.0 * 1024 * 1024)), 0.99);
                DownloadProgressChanged?.Invoke(progress, $"Whisper 모델 다운로드 중... ({totalBytesRead / 1024 / 1024}MB)");
            }

            DownloadProgressChanged?.Invoke(1.0, "Whisper 다운로드 완료");
            Utils.Log4.Info("[STT] Whisper 모델 다운로드 완료");
            return true;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Warn("[STT] Whisper 모델 다운로드 취소됨");
            return false;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] Whisper 모델 다운로드 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 화자분리 모델 다운로드 필요 여부 확인
    /// </summary>
    public bool NeedsSpeakerDiarizationModelDownload()
    {
        var segmentationModelPath = Path.Combine(SpeakerModelsDir, SegmentationModelName, "model.onnx");
        var embeddingModelPath = Path.Combine(SpeakerModelsDir, EmbeddingModelName);
        return !File.Exists(segmentationModelPath) || !File.Exists(embeddingModelPath);
    }

    /// <summary>
    /// 화자분리 모델 다운로드 (Pyannote 세그먼테이션 + 3D-Speaker 임베딩)
    /// </summary>
    public async Task<bool> DownloadSpeakerDiarizationModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Utils.Log4.Info("[STT] 화자분리 모델 다운로드 시작...");
            DownloadProgressChanged?.Invoke(0, "화자분리 모델 다운로드 준비 중...");

            if (!Directory.Exists(SpeakerModelsDir))
            {
                Directory.CreateDirectory(SpeakerModelsDir);
            }

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(30);

            // 1. 세그먼테이션 모델 다운로드 (tar.bz2)
            var segmentationDir = Path.Combine(SpeakerModelsDir, SegmentationModelName);
            var segmentationModelPath = Path.Combine(segmentationDir, "model.onnx");

            if (!File.Exists(segmentationModelPath))
            {
                Utils.Log4.Info("[STT] Pyannote 세그먼테이션 모델 다운로드 중...");
                DownloadProgressChanged?.Invoke(0.1, "Pyannote 세그먼테이션 모델 다운로드 중 (~6MB)...");

                var tempBz2Path = Path.Combine(Path.GetTempPath(), $"segmentation_{Guid.NewGuid()}.tar.bz2");
                try
                {
                    // 다운로드
                    var response = await client.GetAsync(SegmentationModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    var downloadedBytes = 0L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(tempBz2Path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            downloadedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                var progress = 0.1 + (0.3 * downloadedBytes / totalBytes);
                                DownloadProgressChanged?.Invoke(progress,
                                    $"세그먼테이션 모델 다운로드 중... ({downloadedBytes / 1024}KB / {totalBytes / 1024}KB)");
                            }
                        }
                    }

                    // tar.bz2 압축 해제
                    DownloadProgressChanged?.Invoke(0.4, "세그먼테이션 모델 압축 해제 중...");
                    Utils.Log4.Info("[STT] 세그먼테이션 모델 압축 해제 중...");

                    using (var bz2Stream = new FileStream(tempBz2Path, FileMode.Open, FileAccess.Read))
                    using (var decompressedStream = new BZip2InputStream(bz2Stream))
                    using (var tarArchive = TarArchive.CreateInputTarArchive(decompressedStream, System.Text.Encoding.UTF8))
                    {
                        tarArchive.ExtractContents(SpeakerModelsDir);
                    }

                    Utils.Log4.Info("[STT] 세그먼테이션 모델 다운로드 완료");
                }
                finally
                {
                    if (File.Exists(tempBz2Path))
                    {
                        try { File.Delete(tempBz2Path); } catch { }
                    }
                }
            }
            else
            {
                Utils.Log4.Debug("[STT] 세그먼테이션 모델 이미 존재함");
            }

            // 2. 임베딩 모델 다운로드 (단일 ONNX 파일)
            var embeddingModelPath = Path.Combine(SpeakerModelsDir, EmbeddingModelName);

            if (!File.Exists(embeddingModelPath))
            {
                Utils.Log4.Info("[STT] 3D-Speaker 임베딩 모델 다운로드 중...");
                DownloadProgressChanged?.Invoke(0.5, "3D-Speaker 임베딩 모델 다운로드 중 (~30MB)...");

                var response = await client.GetAsync(EmbeddingModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(embeddingModelPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = 0.5 + (0.5 * downloadedBytes / totalBytes);
                            DownloadProgressChanged?.Invoke(progress,
                                $"임베딩 모델 다운로드 중... ({downloadedBytes / 1024}KB / {totalBytes / 1024}KB)");
                        }
                    }
                }

                Utils.Log4.Info("[STT] 임베딩 모델 다운로드 완료");
            }
            else
            {
                Utils.Log4.Debug("[STT] 임베딩 모델 이미 존재함");
            }

            DownloadProgressChanged?.Invoke(1.0, "화자분리 모델 다운로드 완료");
            Utils.Log4.Info("[STT] 화자분리 모델 다운로드 완료");
            return true;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Warn("[STT] 화자분리 모델 다운로드 취소됨");
            return false;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] 화자분리 모델 다운로드 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// STT 결과를 JSON 파일로 저장
    /// </summary>
    public async Task SaveResultAsync(Models.TranscriptResult result, string outputPath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(result, options);
        await File.WriteAllTextAsync(outputPath, json);

        Utils.Log4.Info($"[STT] 결과 저장: {outputPath}");
    }

    /// <summary>
    /// 저장된 STT 결과 로드
    /// </summary>
    public async Task<Models.TranscriptResult?> LoadResultAsync(string path)
    {
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Models.TranscriptResult>(json);
    }

    /// <summary>
    /// 모델 디렉토리 경로 반환
    /// </summary>
    public string GetModelsDirectory() => ModelsDir;

    /// <summary>
    /// Whisper 모델 디렉토리 경로 반환
    /// </summary>
    public string GetWhisperModelsDirectory() => WhisperModelsDir;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _recognizer = null;

        // Whisper 리소스 해제 (순서 중요: 프로세서 먼저, 팩토리 나중에)
        _whisperProcessor?.Dispose();
        _whisperProcessor = null;

        _whisperFactory?.Dispose();
        _whisperFactory = null;

        // 화자분리 리소스 해제
        _speakerDiarizer?.Dispose();
        _speakerDiarizer = null;
    }
}
