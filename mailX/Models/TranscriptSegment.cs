using System;
using System.Collections.Generic;

namespace mailX.Models;

/// <summary>
/// STT 전사 세그먼트 (화자별 발화 단위)
/// </summary>
public class TranscriptSegment
{
    /// <summary>
    /// 시작 시간
    /// </summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>
    /// 종료 시간
    /// </summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// 화자 (예: "화자 1", "화자 2")
    /// </summary>
    public string Speaker { get; set; } = "화자 1";

    /// <summary>
    /// 전사된 텍스트
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 신뢰도 (0.0 ~ 1.0)
    /// </summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// 시간 범위 표시 (예: "00:05 - 00:12")
    /// </summary>
    public string TimeRange => $"{StartTime:mm\\:ss} - {EndTime:mm\\:ss}";

    /// <summary>
    /// 시작 시간 표시 (초 단위)
    /// </summary>
    public double StartSeconds => StartTime.TotalSeconds;

    /// <summary>
    /// 종료 시간 표시 (초 단위)
    /// </summary>
    public double EndSeconds => EndTime.TotalSeconds;

    /// <summary>
    /// 세그먼트 길이
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// 화자 이니셜 (아바타용)
    /// </summary>
    public string SpeakerInitial => string.IsNullOrEmpty(Speaker) ? "?" : Speaker[0].ToString();
}

/// <summary>
/// STT 전체 결과
/// </summary>
public class TranscriptResult
{
    /// <summary>
    /// 녹음 파일 경로
    /// </summary>
    public string AudioFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 전사 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 전사 세그먼트 목록
    /// </summary>
    public List<TranscriptSegment> Segments { get; set; } = new();

    /// <summary>
    /// 전체 전사 텍스트
    /// </summary>
    public string FullText => string.Join(" ", Segments.ConvertAll(s => s.Text));

    /// <summary>
    /// 인식된 화자 목록
    /// </summary>
    public List<string> Speakers { get; set; } = new();

    /// <summary>
    /// 오디오 전체 길이
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// 사용된 모델명
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 언어 코드 (예: "ko", "en")
    /// </summary>
    public string Language { get; set; } = "ko";
}
