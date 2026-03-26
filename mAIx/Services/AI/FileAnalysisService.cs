using MaiX.Models;
using MaiX.Services.Converter;
using MaiX.Services.Graph;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaiX.Services.AI;

public class FileAnalysisService
{
    // STT (음성→텍스트) 서비스 — 서버 모드 전용
    private Speech.ServerSpeechService? _serverSpeechService;
    private readonly SemaphoreSlim _speechInitLock = new(1, 1);

    private readonly AIService _aiService;
    private readonly AttachmentProcessor _attachmentProcessor;
    private readonly OcrConverter _ocrConverter;
    private readonly GraphOneNoteService _graphOneNoteService;
    private readonly PromptCacheService _promptCache;
    private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(FileAnalysisService));

    public FileAnalysisService(
        AIService aiService,
        AttachmentProcessor attachmentProcessor,
        OcrConverter ocrConverter,
        GraphOneNoteService graphOneNoteService,
        PromptCacheService promptCache)
    {
        _aiService = aiService;
        _attachmentProcessor = attachmentProcessor;
        _ocrConverter = ocrConverter;
        _graphOneNoteService = graphOneNoteService;
        _promptCache = promptCache;
    }

    /// <summary>
    /// 개별 파일 AI 분석
    /// </summary>
    public async Task AnalyzeFileAsync(OneNoteAttachment attachment, CancellationToken ct = default)
    {
        try
        {
            // UI 중지 버튼용 CTS 생성 + 외부 ct 연결
            attachment.Cts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, attachment.Cts.Token);
            ct = linkedCts.Token;

            attachment.IsAnalyzing = true;
            attachment.AnalysisStatus = "분석 중...";
            attachment.AnalysisResult = string.Empty;

            // 1. 파일 다운로드 (Graph API URL인 경우)
            var filePath = await EnsureLocalFileAsync(attachment, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(filePath))
            {
                attachment.AnalysisStatus = "실패";
                attachment.AnalysisResult = "파일을 다운로드할 수 없습니다.";
                return;
            }

            // 2. 텍스트 추출 (오디오 파일은 STT로 변환)
            ct.ThrowIfCancellationRequested();
            var isAudio = IsAudioExtension(attachment.Extension);
            if (isAudio)
                attachment.AnalysisStatus = "음성 인식 중...";

            var text = await Task.Run(() => ExtractTextAsync(filePath, attachment.Extension, ct), ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(text))
            {
                attachment.AnalysisStatus = "실패";
                attachment.AnalysisResult = isAudio
                    ? "오디오 파일에서 음성을 인식할 수 없습니다."
                    : "파일에서 텍스트를 추출할 수 없습니다.";
                return;
            }

            // 3. AI 분석 — 스트리밍 결과를 모아서 한 번에 설정 (UI 블로킹 방지)
            ct.ThrowIfCancellationRequested();
            attachment.AnalysisStatus = "AI 분석 중...";
            var prompt = await BuildAnalysisPromptAsync(attachment.FileName, text, isAudio).ConfigureAwait(false);
            var resultBuilder = new System.Text.StringBuilder();
            var stream = await _aiService.StreamCompleteAsync(prompt, ct).ConfigureAwait(false);
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
            attachment.Cts?.Dispose();
            attachment.Cts = null;
            attachment.IsAnalyzing = false;
        }
    }

    /// <summary>
    /// 전체 파일 일괄 AI 분석
    /// </summary>
    public async Task AnalyzeAllFilesAsync(IList<OneNoteAttachment> attachments, CancellationToken ct = default)
    {
        if (attachments == null || attachments.Count == 0) return;

        // UI 중지 버튼용 CTS 생성 + 외부 ct 연결 (첫 번째 attachment에 대표 CTS)
        var masterCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, masterCts.Token);
        ct = linkedCts.Token;

        // 모든 파일에서 텍스트 추출
        var allTexts = new List<string>();
        foreach (var att in attachments)
        {
            att.Cts = masterCts; // 모든 attachment가 동일 CTS 공유 → 어느 하나라도 Cancel 시 전체 중지
            att.IsAnalyzing = true;
            att.AnalysisStatus = "텍스트 추출 중...";
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            foreach (var att in attachments)
            {
                var filePath = await EnsureLocalFileAsync(att, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var text = await Task.Run(() => ExtractTextAsync(filePath, att.Extension, ct), ct).ConfigureAwait(false);
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
            ct.ThrowIfCancellationRequested();
            var combinedText = string.Join("\n\n---\n\n", allTexts);
            var prompt = await BuildAllFilesAnalysisPromptAsync(combinedText).ConfigureAwait(false);
            foreach (var att in attachments)
            {
                att.AnalysisStatus = "AI 분석 중...";
            }

            var resultBuilder = new System.Text.StringBuilder();
            var stream = await _aiService.StreamCompleteAsync(prompt, ct).ConfigureAwait(false);
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
                att.Cts = null;
                att.IsAnalyzing = false;
            }
            masterCts.Dispose();
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
                    attachment.DataUrl, attachment.FileName, tempDir).ConfigureAwait(false);

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
                var serverSpeech = await EnsureServerSpeechServiceAsync(ct).ConfigureAwait(false);
                var result = await serverSpeech.TranscribeFileAsync(filePath, ct).ConfigureAwait(false);
                _log.Info($"STT 완료: {filePath}, 텍스트 길이={result.FullText.Length}");
                return result.FullText;
            }
            else if (IsImageExtension(extension))
            {
                return await _ocrConverter.ConvertToTextAsync(filePath, ct).ConfigureAwait(false);
            }
            else
            {
                var result = await _attachmentProcessor.ProcessAttachmentAsync(filePath, ct).ConfigureAwait(false);
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
    /// 서버 STT 서비스를 lazy 초기화하여 반환
    /// </summary>
    private async Task<Speech.ServerSpeechService> EnsureServerSpeechServiceAsync(CancellationToken ct)
    {
        if (_serverSpeechService != null)
            return _serverSpeechService;

        await _speechInitLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_serverSpeechService != null)
                return _serverSpeechService;

            var serverUrl = App.Settings.UserPreferences.SpeechServerUrl;
            _serverSpeechService = new Speech.ServerSpeechService(serverUrl);
            return _serverSpeechService;
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
        var promptFileName = isAudio ? "onenote_audio_analysis.txt" : "onenote_file_analysis.txt";
        var template = await _promptCache.GetTemplateAsync(promptFileName).ConfigureAwait(false);

        var variables = new Dictionary<string, string>
        {
            ["file_name"] = fileName,
            ["file_content"] = text,
            ["audio_text"] = text
        };

        return RenderTemplate(template, variables);
    }

    private async Task<string> BuildAllFilesAnalysisPromptAsync(string combinedText)
    {
        var template = await _promptCache.GetTemplateAsync("onenote_all_files_analysis.txt").ConfigureAwait(false);

        var variables = new Dictionary<string, string>
        {
            ["combined_text"] = combinedText
        };

        return RenderTemplate(template, variables);
    }

    /// <summary>
    /// 템플릿 내 {{변수명}} 플레이스홀더를 실제 값으로 치환
    /// </summary>
    private static string RenderTemplate(string template, Dictionary<string, string> variables)
    {
        var result = template;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"{{{{{key}}}}}", value);
        }
        return result;
    }

    /// <summary>
    /// 분석 결과에서 요약 부분만 추출
    /// </summary>
    private static string ExtractSummary(string analysisResult)
    {
        if (string.IsNullOrWhiteSpace(analysisResult))
            return string.Empty;

        // 유연한 패턴 매칭: "1.핵심요약", "1. 핵심요약", "1. 요약", "1. 종합 요약" 등
        var match = System.Text.RegularExpressions.Regex.Match(analysisResult,
            @"1\.\s*(?:핵심\s*)?(?:종합\s*)?요약", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var summaryIndex = match.Success ? match.Index : -1;

        // 레거시 마크다운 형식 호환
        if (summaryIndex < 0)
            summaryIndex = analysisResult.IndexOf("**요약**", StringComparison.Ordinal);
        if (summaryIndex < 0)
            summaryIndex = analysisResult.IndexOf("**종합 요약**", StringComparison.Ordinal);

        if (summaryIndex >= 0)
        {
            // 타이틀 줄 전체 스킵 (첫 줄바꿈까지)
            var afterTitle = analysisResult.Substring(summaryIndex);
            var firstNewLine = afterTitle.IndexOf('\n');
            var contentStart = summaryIndex + (firstNewLine >= 0 ? firstNewLine + 1 : afterTitle.Length);

            // 다음 섹션 찾기: "2." (주요포인트) — 줄 시작 "2." 패턴
            var match2 = System.Text.RegularExpressions.Regex.Match(analysisResult.Substring(contentStart),
                @"^\s*2\.\s*", System.Text.RegularExpressions.RegexOptions.Multiline);
            var nextSection = match2.Success ? contentStart + match2.Index : -1;
            if (nextSection < 0)
                nextSection = analysisResult.IndexOf("**주요", contentStart, StringComparison.Ordinal);

            var summary = nextSection >= 0
                ? analysisResult[contentStart..nextSection].Trim()
                : analysisResult[contentStart..].Trim();

            // HTML 태그 제거
            summary = System.Text.RegularExpressions.Regex.Replace(summary, @"<[^>]+>", "");

            // 각 줄의 앞뒤 공백(들여쓰기) 제거 — 줄바꿈은 유지
            summary = System.Text.RegularExpressions.Regex.Replace(summary, @"^[ \t]+|[ \t]+$", "",
                System.Text.RegularExpressions.RegexOptions.Multiline).Trim();

            if (summary.Length > 300)
                summary = summary[..300] + "...";

            return summary;
        }

        // 패턴 없으면 첫 200자
        var fallback = analysisResult.Length > 200
            ? analysisResult[..200].Trim() + "..."
            : analysisResult.Trim();
        fallback = System.Text.RegularExpressions.Regex.Replace(fallback, @"<[^>]+>", "");
        fallback = System.Text.RegularExpressions.Regex.Replace(fallback, @"^[ \t]+|[ \t]+$", "",
            System.Text.RegularExpressions.RegexOptions.Multiline).Trim();
        return fallback;
    }
}
