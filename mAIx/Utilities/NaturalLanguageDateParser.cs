using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace mAIx.Utilities
{
    /// <summary>
    /// 한국어/영어 자연어 날짜/시간 표현을 파싱하는 유틸리티.
    /// 캘린더 + ToDo 공용.
    /// </summary>
    public class NaturalLanguageDateParser
    {
        /// <summary>
        /// 파싱 결과
        /// </summary>
        public class ParseResult
        {
            public DateTime? StartDateTime { get; set; }
            public DateTime? EndDateTime { get; set; }
            public bool IsRecurring { get; set; }
            /// <summary>
            /// 반복 패턴: "daily", "weekly:mon,fri", "monthly" 등
            /// </summary>
            public string? RecurrencePattern { get; set; }
            public string OriginalText { get; set; } = string.Empty;
            public bool Success { get; set; }
        }

        private static readonly Dictionary<string, int> 한국어요일 = new()
        {
            { "월요일", 1 }, { "월", 1 },
            { "화요일", 2 }, { "화", 2 },
            { "수요일", 3 }, { "수", 3 },
            { "목요일", 4 }, { "목", 4 },
            { "금요일", 5 }, { "금", 5 },
            { "토요일", 6 }, { "토", 6 },
            { "일요일", 0 }, { "일", 0 },
        };

        private static readonly Dictionary<string, DayOfWeek> 영어요일 = new(StringComparer.OrdinalIgnoreCase)
        {
            { "monday", DayOfWeek.Monday }, { "mon", DayOfWeek.Monday },
            { "tuesday", DayOfWeek.Tuesday }, { "tue", DayOfWeek.Tuesday },
            { "wednesday", DayOfWeek.Wednesday }, { "wed", DayOfWeek.Wednesday },
            { "thursday", DayOfWeek.Thursday }, { "thu", DayOfWeek.Thursday },
            { "friday", DayOfWeek.Friday }, { "fri", DayOfWeek.Friday },
            { "saturday", DayOfWeek.Saturday }, { "sat", DayOfWeek.Saturday },
            { "sunday", DayOfWeek.Sunday }, { "sun", DayOfWeek.Sunday },
        };

        /// <summary>
        /// 자연어 텍스트에서 날짜/시간을 파싱한다.
        /// </summary>
        public ParseResult Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new ParseResult { OriginalText = input ?? string.Empty, Success = false };

            var result = new ParseResult { OriginalText = input, Success = false };
            var text = input.Trim();
            var now = DateTime.Now;

            // 반복 패턴 감지
            ParseRecurrence(text, result);

            // 날짜 파싱
            var date = ParseDate(text, now);

            // 시간 파싱
            var time = ParseTime(text);

            if (date.HasValue)
            {
                if (time.HasValue)
                {
                    result.StartDateTime = date.Value.Date + time.Value;
                    // 기본 1시간 이벤트
                    result.EndDateTime = result.StartDateTime.Value.AddHours(1);
                }
                else
                {
                    result.StartDateTime = date.Value.Date;
                    result.EndDateTime = date.Value.Date.AddDays(1).AddSeconds(-1);
                }
                result.Success = true;
            }
            else if (time.HasValue)
            {
                // 날짜 없이 시간만 → 오늘로 간주
                result.StartDateTime = now.Date + time.Value;
                result.EndDateTime = result.StartDateTime.Value.AddHours(1);
                result.Success = true;
            }

            return result;
        }

        /// <summary>
        /// 날짜 부분 파싱 (한국어/영어)
        /// </summary>
        private DateTime? ParseDate(string text, DateTime now)
        {
            // 한국어 상대 날짜
            if (text.Contains("오늘")) return now.Date;
            if (text.Contains("내일")) return now.Date.AddDays(1);
            if (text.Contains("모레") || text.Contains("내일모레")) return now.Date.AddDays(2);
            if (text.Contains("어제")) return now.Date.AddDays(-1);

            // 영어 상대 날짜
            var textLower = text.ToLowerInvariant();
            if (textLower.Contains("today")) return now.Date;
            if (textLower.Contains("tomorrow")) return now.Date.AddDays(1);
            if (textLower.Contains("yesterday")) return now.Date.AddDays(-1);

            // "다음 주 {요일}" / "이번 주 {요일}"
            var nextWeekMatch = Regex.Match(text, @"다음\s*주\s*([월화수목금토일])(?:요일)?");
            if (nextWeekMatch.Success)
            {
                var dayName = nextWeekMatch.Groups[1].Value;
                if (한국어요일.TryGetValue(dayName, out var targetDay))
                    return GetNextWeekDay(now, (DayOfWeek)targetDay, addWeek: true);
            }

            var thisWeekMatch = Regex.Match(text, @"이번\s*주\s*([월화수목금토일])(?:요일)?");
            if (thisWeekMatch.Success)
            {
                var dayName = thisWeekMatch.Groups[1].Value;
                if (한국어요일.TryGetValue(dayName, out var targetDay))
                    return GetNextWeekDay(now, (DayOfWeek)targetDay, addWeek: false);
            }

            // "next {weekday}" / "this {weekday}"
            var nextEnMatch = Regex.Match(textLower, @"next\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tue|wed|thu|fri|sat|sun)");
            if (nextEnMatch.Success && 영어요일.TryGetValue(nextEnMatch.Groups[1].Value, out var nextEnDay))
                return GetNextWeekDay(now, nextEnDay, addWeek: true);

            var thisEnMatch = Regex.Match(textLower, @"this\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tue|wed|thu|fri|sat|sun)");
            if (thisEnMatch.Success && 영어요일.TryGetValue(thisEnMatch.Groups[1].Value, out var thisEnDay))
                return GetNextWeekDay(now, thisEnDay, addWeek: false);

            // 단독 요일 (이번 주 기준)
            foreach (var kv in 한국어요일)
            {
                if (text.Contains(kv.Key) && kv.Key.Length > 1) // "일"은 너무 짧아서 단독 매칭에서 제외
                    return GetNextWeekDay(now, (DayOfWeek)kv.Value, addWeek: false);
            }

            // 절대 날짜: YYYY-MM-DD, YYYY/MM/DD
            var absMatch = Regex.Match(text, @"(\d{4})[-/](\d{1,2})[-/](\d{1,2})");
            if (absMatch.Success)
            {
                if (int.TryParse(absMatch.Groups[1].Value, out var y) &&
                    int.TryParse(absMatch.Groups[2].Value, out var m) &&
                    int.TryParse(absMatch.Groups[3].Value, out var d))
                {
                    try { return new DateTime(y, m, d); } catch { }
                }
            }

            // MM/DD, M월 D일
            var mdMatch = Regex.Match(text, @"(\d{1,2})월\s*(\d{1,2})일");
            if (mdMatch.Success)
            {
                if (int.TryParse(mdMatch.Groups[1].Value, out var m) &&
                    int.TryParse(mdMatch.Groups[2].Value, out var d))
                {
                    try { return new DateTime(now.Year, m, d); } catch { }
                }
            }

            return null;
        }

        /// <summary>
        /// 시간 부분 파싱 (한국어/영어)
        /// </summary>
        private TimeSpan? ParseTime(string text)
        {
            // 한국어: "오후 3시 30분", "오전 10시", "3시", "15시 30분"
            var korTimeMatch = Regex.Match(text, @"(오전|오후)?\s*(\d{1,2})시\s*(\d{1,2})?분?");
            if (korTimeMatch.Success)
            {
                var hour = int.Parse(korTimeMatch.Groups[2].Value);
                var minute = korTimeMatch.Groups[3].Success ? int.Parse(korTimeMatch.Groups[3].Value) : 0;
                var ampm = korTimeMatch.Groups[1].Value;

                if (ampm == "오후" && hour < 12) hour += 12;
                else if (ampm == "오전" && hour == 12) hour = 0;

                if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                    return new TimeSpan(hour, minute, 0);
            }

            // 영어: "3:30 PM", "10:00 AM", "15:30"
            var enTimeMatch = Regex.Match(text, @"(\d{1,2}):(\d{2})\s*(AM|PM|am|pm)?", RegexOptions.IgnoreCase);
            if (enTimeMatch.Success)
            {
                var hour = int.Parse(enTimeMatch.Groups[1].Value);
                var minute = int.Parse(enTimeMatch.Groups[2].Value);
                var ampm = enTimeMatch.Groups[3].Value.ToUpperInvariant();

                if (ampm == "PM" && hour < 12) hour += 12;
                else if (ampm == "AM" && hour == 12) hour = 0;

                if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                    return new TimeSpan(hour, minute, 0);
            }

            return null;
        }

        /// <summary>
        /// 반복 패턴 감지
        /// </summary>
        private void ParseRecurrence(string text, ParseResult result)
        {
            var textLower = text.ToLowerInvariant();

            if (text.Contains("매일") || textLower.Contains("every day") || textLower.Contains("daily"))
            {
                result.IsRecurring = true;
                result.RecurrencePattern = "daily";
                return;
            }

            // "매주 월요일", "매주 월,금"
            var weeklyKorMatch = Regex.Match(text, @"매주\s*([월화수목금토일,\s]+)");
            if (weeklyKorMatch.Success)
            {
                result.IsRecurring = true;
                var days = new List<string>();
                foreach (char c in weeklyKorMatch.Groups[1].Value)
                {
                    if (한국어요일.TryGetValue(c.ToString(), out var dayNum))
                    {
                        var dayName = ((DayOfWeek)dayNum).ToString().ToLower()[..3];
                        if (!days.Contains(dayName)) days.Add(dayName);
                    }
                }
                result.RecurrencePattern = days.Count > 0 ? $"weekly:{string.Join(",", days)}" : "weekly";
                return;
            }

            // "every week", "weekly"
            if (textLower.Contains("every week") || textLower.Contains("weekly"))
            {
                result.IsRecurring = true;
                result.RecurrencePattern = "weekly";
                return;
            }

            if (text.Contains("매월") || textLower.Contains("every month") || textLower.Contains("monthly"))
            {
                result.IsRecurring = true;
                result.RecurrencePattern = "monthly";
                return;
            }
        }

        /// <summary>
        /// 다음 특정 요일 날짜 계산
        /// </summary>
        private DateTime GetNextWeekDay(DateTime from, DayOfWeek target, bool addWeek)
        {
            var daysUntil = ((int)target - (int)from.DayOfWeek + 7) % 7;
            if (daysUntil == 0) daysUntil = 7;
            var result = from.Date.AddDays(daysUntil);
            if (addWeek && daysUntil <= 7) result = result.AddDays(7);
            // addWeek이 false인 경우, 이번 주 해당 요일이 이미 지났으면 다음 주로
            return result;
        }
    }
}
