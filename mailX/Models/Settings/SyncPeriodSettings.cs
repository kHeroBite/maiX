using System;
using System.Text.Json.Serialization;

namespace mailX.Models.Settings;

/// <summary>
/// 동기화 기간 설정 타입
/// </summary>
public enum SyncPeriodType
{
    /// <summary>
    /// 건수 제한 (최근 N건)
    /// </summary>
    Count,

    /// <summary>
    /// 일 수 (최근 N일)
    /// </summary>
    Days,

    /// <summary>
    /// 주 (최근 N주)
    /// </summary>
    Weeks,

    /// <summary>
    /// 월 (최근 N개월)
    /// </summary>
    Months,

    /// <summary>
    /// 년 (최근 N년)
    /// </summary>
    Years,

    /// <summary>
    /// 기간 지정 (시작일 ~ 종료일)
    /// </summary>
    DateRange,

    /// <summary>
    /// 전체 (제한 없음)
    /// </summary>
    All
}

/// <summary>
/// 동기화 기간 설정
/// </summary>
public class SyncPeriodSettings
{
    /// <summary>
    /// 기간 타입
    /// </summary>
    public SyncPeriodType PeriodType { get; set; } = SyncPeriodType.Count;

    /// <summary>
    /// 값 (건수, 일수, 주, 월, 년)
    /// </summary>
    public int Value { get; set; } = 100;

    /// <summary>
    /// 시작일 (DateRange 타입일 때 사용)
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// 종료일 (DateRange 타입일 때 사용)
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// 기본 설정 (최근 100건)
    /// </summary>
    public static SyncPeriodSettings Default => new()
    {
        PeriodType = SyncPeriodType.Count,
        Value = 100
    };

    /// <summary>
    /// 설정에 따른 시작 날짜 계산
    /// </summary>
    public DateTime? GetStartDateTime()
    {
        var now = DateTime.Now;

        return PeriodType switch
        {
            SyncPeriodType.Days => now.AddDays(-Value),
            SyncPeriodType.Weeks => now.AddDays(-Value * 7),
            SyncPeriodType.Months => now.AddMonths(-Value),
            SyncPeriodType.Years => now.AddYears(-Value),
            SyncPeriodType.DateRange => StartDate,
            SyncPeriodType.All => null, // 전체
            SyncPeriodType.Count => null, // 건수 제한은 날짜가 아님
            _ => null
        };
    }

    /// <summary>
    /// 설정에 따른 종료 날짜 계산
    /// </summary>
    public DateTime? GetEndDateTime()
    {
        return PeriodType switch
        {
            SyncPeriodType.DateRange => EndDate,
            _ => DateTime.Now
        };
    }

    /// <summary>
    /// 건수 제한 반환 (Count 타입일 때만)
    /// </summary>
    public int? GetCountLimit()
    {
        return PeriodType == SyncPeriodType.Count ? Value : null;
    }

    /// <summary>
    /// 설정을 문자열로 표시
    /// </summary>
    public string ToDisplayString()
    {
        return PeriodType switch
        {
            SyncPeriodType.Count => $"최근 {Value}건",
            SyncPeriodType.Days => $"최근 {Value}일",
            SyncPeriodType.Weeks => $"최근 {Value}주",
            SyncPeriodType.Months => $"최근 {Value}개월",
            SyncPeriodType.Years => $"최근 {Value}년",
            SyncPeriodType.DateRange => StartDate.HasValue && EndDate.HasValue
                ? $"{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}"
                : "기간 미설정",
            SyncPeriodType.All => "전체",
            _ => "알 수 없음"
        };
    }

    /// <summary>
    /// 설정 복사
    /// </summary>
    public SyncPeriodSettings Clone()
    {
        return new SyncPeriodSettings
        {
            PeriodType = PeriodType,
            Value = Value,
            StartDate = StartDate,
            EndDate = EndDate
        };
    }
}

/// <summary>
/// 앱 전체 동기화 설정
/// </summary>
public class AppSyncSettings
{
    /// <summary>
    /// 메일 동기화 기간 설정
    /// </summary>
    public SyncPeriodSettings MailSyncPeriod { get; set; } = SyncPeriodSettings.Default;

    /// <summary>
    /// AI 분석 기간 설정
    /// </summary>
    public SyncPeriodSettings AiAnalysisPeriod { get; set; } = SyncPeriodSettings.Default;
}
