using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Serilog;

namespace mailX.Services.Speech;

/// <summary>
/// sherpa-onnx 기반 음성 인식 (STT) 서비스
/// 한국어 음성을 텍스트로 변환하고 화자 분리 지원
/// </summary>
public class SpeechRecognitionService : IDisposable
{
    // Log4 유틸리티 사용 (log4net 기반)

    // 모델 파일 경로
    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mailX", "models", "sherpa-onnx");

    // 한국어 모델 (SenseVoice - 다국어 지원)
    private const string SenseVoiceModelName = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17";

    private SherpaOnnx.OfflineRecognizer? _recognizer;
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>
    /// 초기화 완료 여부
    /// </summary>
    public bool IsInitialized => _isInitialized;

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
    /// STT 서비스 초기화 (모델 로드)
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return true;

        try
        {
            Utils.Log4.Info("[STT] 서비스 초기화 시작...");

            // 모델 디렉토리 생성
            if (!Directory.Exists(ModelsDir))
            {
                Directory.CreateDirectory(ModelsDir);
            }

            // 모델 파일 확인
            if (NeedsModelDownload())
            {
                Utils.Log4.Info("[STT] 모델 파일 다운로드 필요");
                var downloaded = await DownloadModelAsync(cancellationToken);
                if (!downloaded)
                {
                    Utils.Log4.Warn("[STT] 모델 다운로드 실패");
                    return false;
                }
            }

            // sherpa-onnx 인식기 초기화
            var config = CreateOfflineRecognizerConfig();
            _recognizer = new SherpaOnnx.OfflineRecognizer(config);

            _isInitialized = true;
            Utils.Log4.Info("[STT] 서비스 초기화 완료");
            return true;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] 서비스 초기화 실패: {ex.Message}");
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
    /// 오디오 파일을 텍스트로 변환 (화자 분리 포함)
    /// </summary>
    /// <param name="audioPath">오디오 파일 경로</param>
    /// <returns>전사 결과</returns>
    public async Task<Models.TranscriptResult> TranscribeFileAsync(
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized || _recognizer == null)
        {
            throw new InvalidOperationException("STT 서비스가 초기화되지 않았습니다. InitializeAsync()를 먼저 호출하세요.");
        }

        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("오디오 파일을 찾을 수 없습니다.", audioPath);
        }

        Utils.Log4.Info($"[STT] 파일 분석 시작: {audioPath}");

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

            // 화자 분리를 위한 음성 특성 분석
            var speakerLabels = PerformSpeakerDiarization(samples, sampleRate, voiceSegments);

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
                using var stream = _recognizer.CreateStream();
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

            Utils.Log4.Info($"[STT] 분석 완료: {result.Segments.Count}개 세그먼트, 화자 {result.Speakers.Count}명");

            return result;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Warn("[STT] 분석 취소됨");
            throw;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] 파일 분석 실패: {audioPath} - {ex.Message}");
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
    private Dictionary<int, string> PerformSpeakerDiarization(
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

        // 간단한 K-means 스타일 클러스터링 (2개 화자 가정)
        var meanEnergy = speakerFeatures.Average(f => f.avgEnergy);
        var meanZCR = speakerFeatures.Average(f => f.zeroCrossRate);

        foreach (var feature in speakerFeatures)
        {
            // 에너지와 ZCR을 기반으로 화자 구분
            // 에너지가 높고 ZCR이 낮으면 화자 1 (남성 성향)
            // 에너지가 낮고 ZCR이 높으면 화자 2 (여성 성향)
            var energyScore = feature.avgEnergy > meanEnergy ? 1 : 0;
            var zcrScore = feature.zeroCrossRate > meanZCR ? 1 : 0;

            var speakerNum = (energyScore + zcrScore) >= 1 ? 1 : 2;
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
    /// 모델 다운로드 필요 여부 확인
    /// </summary>
    public bool NeedsModelDownload()
    {
        var modelDir = Path.Combine(ModelsDir, SenseVoiceModelName);
        var modelPath = Path.Combine(modelDir, "model.int8.onnx");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");

        return !File.Exists(modelPath) || !File.Exists(tokensPath);
    }

    /// <summary>
    /// 모델 다운로드 (Hugging Face에서 다운로드)
    /// </summary>
    public async Task<bool> DownloadModelAsync(CancellationToken cancellationToken = default)
    {
        var modelDir = Path.Combine(ModelsDir, SenseVoiceModelName);

        try
        {
            Utils.Log4.Info($"[STT] 모델 다운로드 시작: {SenseVoiceModelName}");
            DownloadProgressChanged?.Invoke(0, "모델 다운로드 준비 중...");

            if (!Directory.Exists(modelDir))
            {
                Directory.CreateDirectory(modelDir);
            }

            // Hugging Face에서 모델 파일 다운로드 (개별 파일 직접 다운로드 가능)
            var baseUrl = $"https://huggingface.co/csukuangfj/{SenseVoiceModelName}/resolve/main";

            var filesToDownload = new[]
            {
                ("model.int8.onnx", "모델 파일 (239MB)"),
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

                    var buffer = new byte[81920]; // 80KB 버퍼
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

                    // 부분 다운로드 파일 삭제
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }

                    throw;
                }
            }

            DownloadProgressChanged?.Invoke(1.0, "다운로드 완료");
            Utils.Log4.Info("[STT] 모델 다운로드 완료");
            return true;
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Warn("[STT] 모델 다운로드 취소됨");
            return false;
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[STT] 모델 다운로드 실패: {ex.Message}");
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

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _recognizer = null;
    }
}
