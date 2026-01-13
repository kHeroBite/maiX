using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;
using mailX.Models;

namespace mailX.Services.Analysis;

/// <summary>
/// 할일 추출기 - AI 응답에서 액션 아이템 파싱
/// </summary>
public class TodoExtractor
{
    private readonly ILogger _logger;

    // 날짜 파싱용 정규식
    private static readonly Regex DatePattern = new(
        @"(\d{4})[.\-/](\d{1,2})[.\-/](\d{1,2})",
        RegexOptions.Compiled);

    // 상대적 날짜 패턴
    private static readonly Regex RelativeDatePattern = new(
        @"(오늘|내일|모레|이번\s*주|다음\s*주|금주|차주|(\d+)\s*(일|주|개월)\s*(후|내)|(\d+)\s*(월)\s*(\d+)\s*(일))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 카테고리 키워드 매핑
    private static readonly Dictionary<string, List<string>> CategoryKeywords = new()
    {
        ["회신"] = new() { "답장", "회신", "답변", "reply", "respond", "응답" },
        ["검토"] = new() { "검토", "확인", "리뷰", "review", "check", "verify", "살펴" },
        ["작성"] = new() { "작성", "준비", "만들", "create", "write", "prepare", "draft" },
        ["보고"] = new() { "보고", "공유", "전달", "report", "share", "notify", "알려" },
        ["승인"] = new() { "승인", "결재", "허가", "approve", "sign", "authorize" },
        ["미팅"] = new() { "미팅", "회의", "meeting", "conference", "call", "참석" }
    };

    public TodoExtractor()
    {
        _logger = Log.ForContext<TodoExtractor>();
    }

    /// <summary>
    /// AI 응답에서 할일 목록 파싱
    /// </summary>
    /// <param name="response">AI 응답 (JSON 배열 형식 기대)</param>
    /// <returns>할일 목록</returns>
    public List<TodoResult> Parse(string? response)
    {
        var todos = new List<TodoResult>();

        if (string.IsNullOrWhiteSpace(response))
        {
            return todos;
        }

        try
        {
            // JSON 배열 시작/끝 찾기
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                // 단일 객체로 시도
                jsonStart = response.IndexOf('{');
                jsonEnd = response.LastIndexOf('}');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var singleJson = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var singleItem = ParseSingleTodo(singleJson);
                    if (singleItem != null)
                    {
                        todos.Add(singleItem);
                    }
                }

                return todos;
            }

            var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var json = JsonDocument.Parse(jsonString);

            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.Warning("할일 JSON이 배열 형식이 아님");
                return todos;
            }

            foreach (var item in json.RootElement.EnumerateArray())
            {
                var todo = ParseTodoItem(item);
                if (todo != null)
                {
                    todos.Add(todo);
                }
            }

            _logger.Information("할일 {Count}개 추출 완료", todos.Count);
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "할일 JSON 파싱 실패: {Response}", response);
        }

        return todos;
    }

    /// <summary>
    /// 단일 JSON 문자열에서 할일 파싱
    /// </summary>
    private TodoResult? ParseSingleTodo(string jsonString)
    {
        try
        {
            var json = JsonDocument.Parse(jsonString);
            return ParseTodoItem(json.RootElement);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// JSON 요소에서 할일 파싱
    /// </summary>
    private TodoResult? ParseTodoItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var todo = new TodoResult();

        // 내용 (필수)
        if (item.TryGetProperty("content", out var content))
        {
            todo.Content = content.GetString() ?? string.Empty;
        }
        else if (item.TryGetProperty("task", out var task))
        {
            todo.Content = task.GetString() ?? string.Empty;
        }
        else if (item.TryGetProperty("action", out var action))
        {
            todo.Content = action.GetString() ?? string.Empty;
        }

        // 내용이 없으면 무시
        if (string.IsNullOrWhiteSpace(todo.Content))
        {
            return null;
        }

        // 마감일
        if (item.TryGetProperty("due_date", out var dueDate))
        {
            var dueDateStr = dueDate.GetString();
            todo.DueDateText = dueDateStr;
            todo.DueDate = ParseDate(dueDateStr);
        }
        else if (item.TryGetProperty("deadline", out var deadline))
        {
            var deadlineStr = deadline.GetString();
            todo.DueDateText = deadlineStr;
            todo.DueDate = ParseDate(deadlineStr);
        }

        // 우선순위
        if (item.TryGetProperty("priority", out var priority))
        {
            todo.Priority = ParsePriority(priority);
        }
        else
        {
            // 기본 우선순위 (마감일 기반)
            todo.Priority = DeterminePriority(todo.DueDate);
        }

        // 담당자
        if (item.TryGetProperty("assignee", out var assignee))
        {
            todo.Assignee = assignee.GetString();
        }

        // 카테고리
        if (item.TryGetProperty("category", out var category))
        {
            todo.Category = category.GetString();
        }
        else
        {
            // 내용 기반 카테고리 자동 추론
            todo.Category = InferCategory(todo.Content);
        }

        return todo;
    }

    /// <summary>
    /// 날짜 문자열 파싱
    /// </summary>
    private DateTime? ParseDate(string? dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText))
            return null;

        // 표준 날짜 형식 시도
        if (DateTime.TryParse(dateText, out var parsed))
        {
            return parsed;
        }

        // YYYY-MM-DD, YYYY.MM.DD, YYYY/MM/DD 형식
        var match = DatePattern.Match(dateText);
        if (match.Success)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);

            try
            {
                return new DateTime(year, month, day);
            }
            catch
            {
                // 유효하지 않은 날짜
            }
        }

        // 상대적 날짜 파싱
        return ParseRelativeDate(dateText);
    }

    /// <summary>
    /// 상대적 날짜 파싱 (오늘, 내일, 다음주 등)
    /// </summary>
    private DateTime? ParseRelativeDate(string dateText)
    {
        var today = DateTime.Today;
        var lowerText = dateText.ToLower();

        if (lowerText.Contains("오늘") || lowerText.Contains("today"))
        {
            return today;
        }

        if (lowerText.Contains("내일") || lowerText.Contains("tomorrow"))
        {
            return today.AddDays(1);
        }

        if (lowerText.Contains("모레"))
        {
            return today.AddDays(2);
        }

        if (lowerText.Contains("이번 주") || lowerText.Contains("금주") || lowerText.Contains("this week"))
        {
            // 이번 주 금요일
            var daysUntilFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(daysUntilFriday);
        }

        if (lowerText.Contains("다음 주") || lowerText.Contains("차주") || lowerText.Contains("next week"))
        {
            // 다음 주 금요일
            var daysUntilFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7 + 7;
            return today.AddDays(daysUntilFriday);
        }

        // "N일 후" 패턴
        var daysMatch = Regex.Match(lowerText, @"(\d+)\s*일\s*(후|내)");
        if (daysMatch.Success)
        {
            var days = int.Parse(daysMatch.Groups[1].Value);
            return today.AddDays(days);
        }

        // "N주 후" 패턴
        var weeksMatch = Regex.Match(lowerText, @"(\d+)\s*주\s*(후|내)");
        if (weeksMatch.Success)
        {
            var weeks = int.Parse(weeksMatch.Groups[1].Value);
            return today.AddDays(weeks * 7);
        }

        // "M월 D일" 패턴
        var mdMatch = Regex.Match(lowerText, @"(\d+)\s*월\s*(\d+)\s*일");
        if (mdMatch.Success)
        {
            var month = int.Parse(mdMatch.Groups[1].Value);
            var day = int.Parse(mdMatch.Groups[2].Value);
            var year = today.Year;

            // 이미 지난 날짜면 내년
            if (month < today.Month || (month == today.Month && day < today.Day))
            {
                year++;
            }

            try
            {
                return new DateTime(year, month, day);
            }
            catch
            {
                // 유효하지 않은 날짜
            }
        }

        return null;
    }

    /// <summary>
    /// 우선순위 파싱
    /// </summary>
    private int ParsePriority(JsonElement priority)
    {
        if (priority.ValueKind == JsonValueKind.Number)
        {
            var value = priority.GetInt32();
            return Math.Clamp(value, 1, 5);
        }

        if (priority.ValueKind == JsonValueKind.String)
        {
            var text = priority.GetString()?.ToLower();
            return text switch
            {
                "critical" or "매우높음" or "1" => 1,
                "high" or "높음" or "2" => 2,
                "medium" or "보통" or "normal" or "3" => 3,
                "low" or "낮음" or "4" => 4,
                "very_low" or "매우낮음" or "5" => 5,
                _ => 3
            };
        }

        return 3;
    }

    /// <summary>
    /// 마감일 기반 우선순위 결정
    /// </summary>
    private int DeterminePriority(DateTime? dueDate)
    {
        if (!dueDate.HasValue)
            return 3; // 기본

        var daysRemaining = (dueDate.Value - DateTime.Today).Days;

        return daysRemaining switch
        {
            <= 0 => 1,      // 마감 지남/당일
            <= 1 => 1,      // 내일
            <= 3 => 2,      // 3일 이내
            <= 7 => 3,      // 일주일 이내
            <= 14 => 4,     // 2주 이내
            _ => 5          // 그 이상
        };
    }

    /// <summary>
    /// 내용 기반 카테고리 추론
    /// </summary>
    private string InferCategory(string content)
    {
        var lowerContent = content.ToLower();

        foreach (var (category, keywords) in CategoryKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (lowerContent.Contains(keyword.ToLower()))
                {
                    return category;
                }
            }
        }

        return "기타";
    }

    /// <summary>
    /// TodoResult 목록을 Todo 엔티티 목록으로 변환
    /// </summary>
    public List<Todo> ToEntities(List<TodoResult> results, int emailId)
    {
        var entities = new List<Todo>();

        foreach (var result in results)
        {
            entities.Add(new Todo
            {
                EmailId = emailId,
                Content = result.Content,
                DueDate = result.DueDate,
                Status = "pending",
                Priority = result.Priority
            });
        }

        return entities;
    }
}
