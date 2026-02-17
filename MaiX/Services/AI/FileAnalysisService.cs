using MaiX.Models;
using MaiX.Services.Converter;
using MaiX.Services.Graph;
using MaiX.Services.Storage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaiX.Services.AI;

public class FileAnalysisService
{
    // STT (음성→텍스트) 서비스 — lazy 초기화
    private Speech.SpeechRecognitionService? _speechService;
    private readonly SemaphoreSlim _speechInitLock = new(1, 1);

    private readonly AIService _aiService;
    private readonly AttachmentProcessor _attachmentProcessor;
    private readonly OcrConverter _ocrConverter;
    private readonly GraphOneNoteService _graphOneNoteService;
    private readonly IServiceProvider _serviceProvider;
    private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(FileAnalysisService));

    public FileAnalysisService(
        AIService aiService,
        AttachmentProcessor attachmentProcessor,
        OcrConverter ocrConverter,
        GraphOneNoteService graphOneNoteService,
        IServiceProvider serviceProvider)
    {
        _aiService = aiService;
        _attachmentProcessor = attachmentProcessor;
        _ocrConverter = ocrConverter;
        _graphOneNoteService = graphOneNoteService;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 개별 파일 AI 분석
    /// </summary>
    public async Task AnalyzeFileAsync(OneNoteAttachment attachment, CancellationToken ct = default)
    {
        try
        {
            attachment.IsAnalyzing = true;
            attachment.AnalysisStatus = "분석 중...";
            attachment.AnalysisResult = string.Empty;

            // 1. 파일 다운로드 (Graph API URL인 경우)
            var filePath = await EnsureLocalFileAsync(attachment, ct);
            if (string.IsNullOrEmpty(filePath))
            {
                attachment.AnalysisStatus = "실패";
                attachment.AnalysisResult = "파일을 다운로드할 수 없습니다.";
                return;
            }

            // 2. 텍스트 추출 (오디오 파일은 STT로 변환)
            var isAudio = IsAudioExtension(attachment.Extension);
            if (isAudio)
                attachment.AnalysisStatus = "음성 인식 중...";

            var text = await Task.Run(() => ExtractTextAsync(filePath, attachment.Extension, ct), ct);
            if (string.IsNullOrEmpty(text))
            {
                attachment.AnalysisStatus = "실패";
                attachment.AnalysisResult = isAudio
                    ? "오디오 파일에서 음성을 인식할 수 없습니다."
                    : "파일에서 텍스트를 추출할 수 없습니다.";
                return;
            }

            // 3. AI 분석 — 스트리밍 결과를 모아서 한 번에 설정 (UI 블로킹 방지)
            attachment.AnalysisStatus = "AI 분석 중...";
            var prompt = await BuildAnalysisPromptAsync(attachment.FileName, text, isAudio);
            var resultBuilder = new System.Text.StringBuilder();
            var stream = await _aiService.StreamCompleteAsync(prompt, ct);
            await foreach (var chunk in stream.WithCancellation(ct))
            {
                resultBuilder.Append(chunk);
            }
            attachment.AnalysisResult = resultBuilder.ToString();

            // 분석 완료 후 요약 추출
            if (!string.IsNullOrEmpty(attachment.AnalysisResult))
            {
                attachment.AnalysisSummary = ExtractSummary(attachment.AnalysisResult);
            }

            attachment.AnalysisStatus = "완료";
        }
        catch (OperationCanceledException)
        {
            attachment.AnalysisStatus = "취소됨";
        }
        catch (Exception ex)
        {
            _log.Error($"파일 분석 실패: {attachment.FileName}", ex);
            attachment.AnalysisStatus = "실패";
            attachment.AnalysisResult = $"분석 중 오류 발생: {ex.Message}";
        }
        finally
        {
            attachment.IsAnalyzing = false;
        }
    }

    /// <summary>
    /// 전체 파일 일괄 AI 분석
    /// </summary>
    public async Task AnalyzeAllFilesAsync(IList<OneNoteAttachment> attachments, CancellationToken ct = default)
    {
        if (attachments == null || attachments.Count == 0) return;

        // 모든 파일에서 텍스트 추출
        var allTexts = new List<string>();
        foreach (var att in attachments)
        {
            att.IsAnalyzing = true;
            att.AnalysisStatus = "텍스트 추출 중...";
        }

        try
        {
            foreach (var att in attachments)
            {
                var filePath = await EnsureLocalFileAsync(att, ct);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var text = await Task.Run(() => ExtractTextAsync(filePath, att.Extension, ct), ct);
                    if (!string.IsNullOrEmpty(text))
                    {
                        allTexts.Add($"[{att.FileName}]\n{text}");
                    }
                }
            }

            if (allTexts.Count == 0)
            {
                foreach (var att in attachments)
                {
                    att.AnalysisStatus = "실패";
                    att.AnalysisResult = "텍스트를 추출할 수 없습니다.";
                    att.IsAnalyzing = false;
                }
                return;
            }

            // 전체 통합 AI 분석
            var combinedText = string.Join("\n\n---\n\n", allTexts);
            var prompt = await BuildAllFilesAnalysisPromptAsync(combinedText);
            foreach (var att in attachments)
            {
                att.AnalysisStatus = "AI 분석 중...";
            }

            var resultBuilder = new System.Text.StringBuilder();
            var stream = await _aiService.StreamCompleteAsync(prompt, ct);
            await foreach (var chunk in stream.WithCancellation(ct))
            {
                resultBuilder.Append(chunk);
            }
            var result = resultBuilder.ToString();

            // 요약 추출
            var summary = ExtractSummary(result);

            // 모든 파일에 동일 결과 설정
            foreach (var att in attachments)
            {
                att.AnalysisResult = result;
                att.AnalysisSummary = summary;
                att.AnalysisStatus = "완료";
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var att in attachments)
            {
                att.AnalysisStatus = "취소됨";
            }
        }
        catch (Exception ex)
        {
            _log.Error("전체 파일 분석 실패", ex);
            foreach (var att in attachments)
            {
                att.AnalysisStatus = "실패";
                att.AnalysisResult = $"분석 중 오류 발생: {ex.Message}";
            }
        }
        finally
        {
            foreach (var att in attachments)
            {
                att.IsAnalyzing = false;
            }
        }
    }

    private async Task<string> EnsureLocalFileAsync(OneNoteAttachment attachment, CancellationToken ct)
    {
        // 로컬 파일이 이미 있으면 반환
        if (!string.IsNullOrEmpty(attachment.LocalPath) && File.Exists(attachment.LocalPath))
            return attachment.LocalPath;

        // Graph API URL이면 다운로드
        if (!string.IsNullOrEmpty(attachment.DataUrl) && attachment.DataUrl.Contains("graph.microsoft.com"))
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "MaiX_Attachments");
                Directory.CreateDirectory(tempDir);

                var localPath = await _graphOneNoteService.DownloadAudioResourceAsync(
                    attachment.DataUrl, attachment.FileName, tempDir);

                if (!string.IsNullOrEmpty(localPath))
                {
                    attachment.LocalPath = localPath;
                    return localPath;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                _log.Error($"파일 다운로드 실패: {attachment.DataUrl}", ex);
                return string.Empty;
            }
        }

        return attachment.DataUrl; // 로컬 경로일 수 있음
    }

    private async Task<string> ExtractTextAsync(string filePath, string extension, CancellationToken ct)
    {
        try
        {
            if (IsAudioExtension(extension))
            {
                var speechService = await EnsureSpeechServiceAsync(ct);
                var result = await speechService.TranscribeFileAsync(filePath, ct);
                _log.Info($"STT 완료: {filePath}, 텍스트 길이={result.FullText.Length}");
                return result.FullText;
            }
            else if (IsImageExtension(extension))
            {
                return await _ocrConverter.ConvertToTextAsync(filePath, ct);
            }
            else
            {
                var result = await _attachmentProcessor.ProcessAttachmentAsync(filePath, ct);
                return result.Text;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"텍스트 추출 실패: {filePath}", ex);
            return string.Empty;
        }
    }


    /// <summary>
    /// STT 서비스를 lazy 초기화하여 반환
    /// </summary>
    private async Task<Speech.SpeechRecognitionService> EnsureSpeechServiceAsync(CancellationToken ct)
    {
        if (_speechService?.IsInitialized == true)
            return _speechService;

        await _speechInitLock.WaitAsync(ct);
        try
        {
            if (_speechService?.IsInitialized == true)
                return _speechService;

            _speechService ??= new Speech.SpeechRecognitionService();

            if (_speechService.NeedsSenseVoiceModelDownload())
            {
                _log.Info("STT 모델 다운로드 시작 (SenseVoice)...");
                await _speechService.DownloadSenseVoiceModelAsync(ct);
            }

            if (!_speechService.IsSenseVoiceInitialized)
            {
                await _speechService.InitializeSenseVoiceAsync(ct);
            }

            return _speechService;
        }
        finally
        {
            _speechInitLock.Release();
        }
    }

    /// <summary>
    /// 오디오 파일 확장자 판별
    /// </summary>
    private static bool IsAudioExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp3" or ".wav" or ".m4a" or ".ogg" or ".wma" or ".flac" or ".aac" => true,
        _ => false
    };

    private static bool IsImageExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tiff" => true,
        _ => false
    };

    private async Task<string> BuildAnalysisPromptAsync(string fileName, string text, bool isAudio = false)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();

            if (isAudio)
            {
                var result = await promptService.RenderPromptAsync("onenote_audio_analysis",
                    new Dictionary<string, string> { ["file_name"] = fileName, ["audio_text"] = text });
                if (result != null) return result;
            }
            else
            {
                var result = await promptService.RenderPromptAsync("onenote_file_analysis",
                    new Dictionary<string, string> { ["file_name"] = fileName, ["file_content"] = text });
                if (result != null) return result;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"프롬프트 서비스 조회 실패, 기본 프롬프트 사용: {ex.Message}");
        }

        // Fallback: 기존 하드코딩 프롬프트
        if (isAudio)
        {
            return $"""
                다음은 '{fileName}' 오디오 파일에서 STT(음성→텍스트)로 추출한 내용입니다.
                아래 분석항목을 작성하세요:
                1. 요약: 핵심을 1~2문장으로 간결 서술
                2. 주요 포인트: 중요 정보를 항목별로 나열
                3. 액션 아이템: 후속 조치가 있다면 나열

                ⚠️ 답변 형식: 번호 체계(1. 2. 3. / a. b. c. / i. ii. iii.) 사용, 들여쓰기로 계층 표현. ★핵심★ ▲긍정▲ ▼부정▼ ⚠주의⚠ ◆중요◆ ●참고● ◈결론◈ ♦수치♦ 하이라이팅. 마크다운(##, -, *, **) 금지. HTML 태그(<span> 등) 금지. 상위 제목 반복 금지 — 제목은 한 번만, 하위는 들여쓰기. 각 항목 1줄 간결하게.

                음성 내용:
                {text}
                """;
        }

        return $"""
            다음은 '{fileName}' 파일의 내용입니다.
            아래 분석항목을 작성하세요:
            1. 요약: 핵심을 1~2문장으로 간결 서술
            2. 주요 포인트: 중요 정보를 항목별로 나열
            3. 액션 아이템: 후속 조치가 있다면 나열

            ⚠️ 답변 형식: 번호 체계(1. 2. 3. / a. b. c. / i. ii. iii.) 사용, 들여쓰기로 계층 표현. ★핵심★ ▲긍정▲ ▼부정▼ ⚠주의⚠ ◆중요◆ ●참고● ◈결론◈ ♦수치♦ 하이라이팅. 마크다운(##, -, *, **) 금지. HTML 태그(<span> 등) 금지. 상위 제목 반복 금지 — 제목은 한 번만, 하위는 들여쓰기. 각 항목 1줄 간결하게.

            파일 내용:
            {text}
            """;
    }

    private async Task<string> BuildAllFilesAnalysisPromptAsync(string combinedText)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
            var result = await promptService.RenderPromptAsync("onenote_all_files_analysis",
                new Dictionary<string, string> { ["combined_text"] = combinedText });
            if (result != null) return result;
        }
        catch (Exception ex)
        {
            _log.Warn($"프롬프트 서비스 조회 실패, 기본 프롬프트 사용: {ex.Message}");
        }

        // Fallback
        return $"""
            다음은 여러 첨부파일의 내용입니다.
            아래 분석항목을 작성하세요:
            1. 종합 요약: 핵심을 1~2문장으로 간결 서술
            2. 파일별 주요 포인트: 각 파일의 중요 정보
            3. 연관성 분석: 파일 간의 관련성이나 공통 주제
            4. 액션 아이템: 후속 조치가 있다면 나열

            ⚠️ 답변 형식: 번호 체계(1. 2. 3. / a. b. c. / i. ii. iii.) 사용, 들여쓰기로 계층 표현. ★핵심★ ▲긍정▲ ▼부정▼ ⚠주의⚠ ◆중요◆ ●참고● ◈결론◈ ♦수치♦ 하이라이팅. 마크다운(##, -, *, **) 금지. HTML 태그(<span> 등) 금지. 상위 제목 반복 금지 — 제목은 한 번만, 하위는 들여쓰기. 각 항목 1줄 간결하게.

            전체 파일 내용:
            {combinedText}
            """;
    }

    /// <summary>
    /// 분석 결과에서 요약 부분만 추출
    /// </summary>
    private static string ExtractSummary(string analysisResult)
    {
        if (string.IsNullOrWhiteSpace(analysisResult))
            return string.Empty;

        // 새 리포트형식: "1. 요약" 패턴
        var summaryIndex = analysisResult.IndexOf("1. 요약", StringComparison.Ordinal);
        if (summaryIndex < 0)
            summaryIndex = analysisResult.IndexOf("1. 종합 요약", StringComparison.Ordinal);

        // 레거시 마크다운 형식 호환
        if (summaryIndex < 0)
            summaryIndex = analysisResult.IndexOf("**요약**", StringComparison.Ordinal);
        if (summaryIndex < 0)
            summaryIndex = analysisResult.IndexOf("**종합 요약**", StringComparison.Ordinal);

        if (summaryIndex >= 0)
        {
            var contentStart = analysisResult.IndexOf(':', summaryIndex);
            if (contentStart < 0) contentStart = summaryIndex + 5;
            else contentStart++;

            // 다음 섹션 찾기: "2." 또는 "**주요"
            var nextSection = analysisResult.IndexOf("\n2.", contentStart, StringComparison.Ordinal);
            if (nextSection < 0)
                nextSection = analysisResult.IndexOf("**주요", contentStart, StringComparison.Ordinal);

            var summary = nextSection >= 0
                ? analysisResult[contentStart..nextSection].Trim()
                : analysisResult[contentStart..].Trim();

            if (summary.Length > 150)
                summary = summary[..150] + "...";

            return summary;
        }

        // 패턴 없으면 첫 200자
        return analysisResult.Length > 100
            ? analysisResult[..100].Trim() + "..."
            : analysisResult.Trim();
    }
}
