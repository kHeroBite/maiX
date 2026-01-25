using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace mailX.Services.AI;

/// <summary>
/// 녹음 AI 요약 서비스
/// STT 결과를 기반으로 AI 요약 생성
/// </summary>
public class RecordingSummaryService
{
    private static readonly ILogger _logger = Log.ForContext<RecordingSummaryService>();

    private readonly AIService _aiService;

    public RecordingSummaryService(AIService aiService)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    }

    /// <summary>
    /// 요약 진행률 변경 이벤트
    /// </summary>
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// 실시간 요약 업데이트 이벤트
    /// </summary>
    public event Action<string>? SummaryUpdated;

    /// <summary>
    /// STT 결과에서 전체 요약 생성
    /// </summary>
    public async Task<Models.RecordingSummary> SummarizeFullAsync(
        Models.TranscriptResult transcriptResult,
        CancellationToken cancellationToken = default)
    {
        if (transcriptResult == null || transcriptResult.Segments.Count == 0)
        {
            throw new ArgumentException("전사 결과가 비어있습니다.");
        }

        _logger.Information("전체 요약 생성 시작: {SegmentCount}개 세그먼트", transcriptResult.Segments.Count);
        ProgressChanged?.Invoke(0);

        var result = new Models.RecordingSummary
        {
            AudioFilePath = transcriptResult.AudioFilePath,
            CreatedAt = DateTime.Now,
            ModelName = _aiService.CurrentProviderName,
            SourceSTTPath = null // 필요시 설정
        };

        try
        {
            // 전사 텍스트 구성
            var transcriptText = BuildTranscriptText(transcriptResult);
            ProgressChanged?.Invoke(0.1);

            // 요약 프롬프트 생성
            var prompt = BuildSummaryPrompt(transcriptText);

            // AI 요약 요청
            var summaryResponse = await _aiService.CompleteAsync(prompt, cancellationToken);
            ProgressChanged?.Invoke(0.6);

            // 응답 파싱
            ParseSummaryResponse(summaryResponse, result);
            ProgressChanged?.Invoke(0.9);

            // 참여자 정보 (STT에서 가져옴)
            result.Participants = transcriptResult.Speakers;

            ProgressChanged?.Invoke(1.0);
            _logger.Information("전체 요약 생성 완료");

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "요약 생성 실패");
            throw;
        }
    }

    /// <summary>
    /// 증분 요약 (실시간 요약용)
    /// </summary>
    public async Task<string> SummarizeIncrementalAsync(
        List<Models.TranscriptSegment> newSegments,
        string previousSummary,
        CancellationToken cancellationToken = default)
    {
        if (newSegments == null || newSegments.Count == 0)
            return previousSummary;

        var newText = new StringBuilder();
        foreach (var segment in newSegments)
        {
            newText.AppendLine($"[{segment.Speaker}]: {segment.Text}");
        }

        var prompt = $@"다음은 진행 중인 회의/녹음의 이전 요약입니다:
{(string.IsNullOrEmpty(previousSummary) ? "(아직 요약 없음)" : previousSummary)}

새로운 발언 내용:
{newText}

위 새로운 내용을 반영하여 전체 요약을 업데이트해주세요.
간결하게 핵심만 요약해주세요. (3-5문장)";

        try
        {
            var response = await _aiService.CompleteAsync(prompt, cancellationToken);
            SummaryUpdated?.Invoke(response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "증분 요약 실패");
            return previousSummary;
        }
    }

    /// <summary>
    /// 전사 텍스트 구성
    /// </summary>
    private string BuildTranscriptText(Models.TranscriptResult transcriptResult)
    {
        var sb = new StringBuilder();

        foreach (var segment in transcriptResult.Segments)
        {
            sb.AppendLine($"[{segment.TimeRange}] {segment.Speaker}: {segment.Text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 요약 프롬프트 생성
    /// </summary>
    private string BuildSummaryPrompt(string transcriptText)
    {
        return $@"다음은 녹음된 회의/대화의 전사 내용입니다:

{transcriptText}

위 내용을 분석하여 다음 형식의 JSON으로 응답해주세요:

{{
  ""summary"": ""전체 내용을 3-5문장으로 요약"",
  ""keyPoints"": [""핵심 포인트 1"", ""핵심 포인트 2"", ...],
  ""actionItems"": [
    {{""description"": ""해야 할 일"", ""assignee"": ""담당자 (언급된 경우)"", ""dueDate"": ""기한 (언급된 경우)"", ""priority"": ""높음/중간/낮음""}}
  ],
  ""topics"": [""주요 주제1"", ""주요 주제2"", ...],
  ""recordingType"": ""회의/강의/인터뷰/일반 대화 등"",
  ""sentiment"": ""긍정적/부정적/중립적/건설적 등""
}}

반드시 유효한 JSON 형식으로만 응답하세요.";
    }

    /// <summary>
    /// AI 응답 파싱
    /// </summary>
    private void ParseSummaryResponse(string response, Models.RecordingSummary result)
    {
        try
        {
            // JSON 블록 추출
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                // 요약
                if (root.TryGetProperty("summary", out var summaryProp))
                {
                    result.Summary = summaryProp.GetString() ?? string.Empty;
                }

                // 핵심 포인트
                if (root.TryGetProperty("keyPoints", out var keyPointsProp) && keyPointsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in keyPointsProp.EnumerateArray())
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            result.KeyPoints.Add(text);
                        }
                    }
                }

                // 액션 아이템
                if (root.TryGetProperty("actionItems", out var actionItemsProp) && actionItemsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in actionItemsProp.EnumerateArray())
                    {
                        var actionItem = new Models.ActionItem
                        {
                            Description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                            Assignee = item.TryGetProperty("assignee", out var assignee) ? assignee.GetString() : null,
                            DueDate = item.TryGetProperty("dueDate", out var due) ? due.GetString() : null,
                            Priority = item.TryGetProperty("priority", out var priority) ? priority.GetString() ?? "중간" : "중간"
                        };

                        if (!string.IsNullOrEmpty(actionItem.Description))
                        {
                            result.ActionItems.Add(actionItem);
                        }
                    }
                }

                // 주제
                if (root.TryGetProperty("topics", out var topicsProp) && topicsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in topicsProp.EnumerateArray())
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            result.Topics.Add(text);
                        }
                    }
                }

                // 녹음 유형
                if (root.TryGetProperty("recordingType", out var typeProp))
                {
                    result.RecordingType = typeProp.GetString() ?? string.Empty;
                }

                // 분위기
                if (root.TryGetProperty("sentiment", out var sentimentProp))
                {
                    result.Sentiment = sentimentProp.GetString() ?? string.Empty;
                }
            }
            else
            {
                // JSON 형식이 아닌 경우 전체를 요약으로 사용
                result.Summary = response;
            }
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "요약 응답 JSON 파싱 실패, 전체 텍스트를 요약으로 사용");
            result.Summary = response;
        }
    }

    /// <summary>
    /// 요약 결과를 JSON 파일로 저장
    /// </summary>
    public async Task SaveResultAsync(Models.RecordingSummary result, string outputPath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(result, options);
        await File.WriteAllTextAsync(outputPath, json);

        _logger.Information("요약 결과 저장: {Path}", outputPath);
    }

    /// <summary>
    /// 저장된 요약 결과 로드
    /// </summary>
    public async Task<Models.RecordingSummary?> LoadResultAsync(string path)
    {
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Models.RecordingSummary>(json);
    }
}
