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
    private readonly AIService _aiService;
    private readonly AttachmentProcessor _attachmentProcessor;
    private readonly OcrConverter _ocrConverter;
    private readonly GraphOneNoteService _graphOneNoteService;
    private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(FileAnalysisService));

    public FileAnalysisService(
        AIService aiService,
        AttachmentProcessor attachmentProcessor,
        OcrConverter ocrConverter,
        GraphOneNoteService graphOneNoteService)
    {
        _aiService = aiService;
        _attachmentProcessor = attachmentProcessor;
        _ocrConverter = ocrConverter;
        _graphOneNoteService = graphOneNoteService;
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

            // 2. 텍스트 추출
            var text = await ExtractTextAsync(filePath, attachment.Extension, ct);
            if (string.IsNullOrEmpty(text))
            {
                attachment.AnalysisStatus = "실패";
                attachment.AnalysisResult = "파일에서 텍스트를 추출할 수 없습니다.";
                return;
            }

            // 3. AI 분석 (스트리밍)
            var prompt = BuildAnalysisPrompt(attachment.FileName, text);
            var stream = await _aiService.StreamCompleteAsync(prompt, ct);
            await foreach (var chunk in stream.WithCancellation(ct))
            {
                attachment.AnalysisResult += chunk;
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
                    var text = await ExtractTextAsync(filePath, att.Extension, ct);
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
            var prompt = BuildAllFilesAnalysisPrompt(combinedText);
            var result = string.Empty;

            foreach (var att in attachments)
            {
                att.AnalysisStatus = "AI 분석 중...";
            }

            var stream = await _aiService.StreamCompleteAsync(prompt, ct);
            await foreach (var chunk in stream.WithCancellation(ct))
            {
                result += chunk;
                // 첫 번째 파일에 실시간 업데이트 표시 (전체 분석 결과)
                attachments[0].AnalysisResult = result;
            }

            // 모든 파일에 동일 결과 설정
            foreach (var att in attachments)
            {
                att.AnalysisResult = result;
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
            if (IsImageExtension(extension))
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

    private static bool IsImageExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tiff" => true,
        _ => false
    };

    private static string BuildAnalysisPrompt(string fileName, string text)
    {
        return $"""
            다음은 '{fileName}' 파일의 내용입니다. 이 파일을 분석하여 다음을 제공해주세요:

            1. **요약**: 파일의 핵심 내용을 3-5문장으로 요약
            2. **주요 포인트**: 중요한 정보나 핵심 사항을 bullet point로 나열
            3. **액션 아이템**: 필요한 후속 조치가 있다면 나열

            파일 내용:
            {text}
            """;
    }

    private static string BuildAllFilesAnalysisPrompt(string combinedText)
    {
        return $"""
            다음은 여러 첨부파일의 내용입니다. 전체 파일을 종합적으로 분석하여 다음을 제공해주세요:

            1. **종합 요약**: 전체 첨부파일의 핵심 내용을 요약
            2. **파일별 주요 포인트**: 각 파일의 중요 정보
            3. **연관성 분석**: 파일 간의 관련성이나 공통 주제
            4. **액션 아이템**: 필요한 후속 조치

            전체 파일 내용:
            {combinedText}
            """;
    }
}
