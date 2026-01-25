using System;

namespace mailX.Models;

/// <summary>
/// 녹음 파일 출처 구분
/// </summary>
public enum RecordingSource
{
    /// <summary>
    /// mailX에서 직접 녹음한 파일
    /// </summary>
    MailX,

    /// <summary>
    /// 외부에서 가져온 파일
    /// </summary>
    External
}

/// <summary>
/// 녹음 파일 정보 모델
/// </summary>
public class RecordingInfo
{
    /// <summary>
    /// 파일 경로
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 파일 이름
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>
    /// 녹음 길이
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 녹음 길이 표시 문자열
    /// </summary>
    public string DurationDisplay => Duration.ToString(@"mm\:ss");

    /// <summary>
    /// 파일 출처 (mailX 녹음 vs 외부 파일)
    /// </summary>
    public RecordingSource Source { get; set; } = RecordingSource.External;

    /// <summary>
    /// 출처 표시 텍스트
    /// </summary>
    public string SourceDisplay => Source == RecordingSource.MailX ? "mailX" : "외부";

    /// <summary>
    /// 출처별 아이콘 심볼
    /// </summary>
    public string SourceIcon => Source == RecordingSource.MailX ? "Mic24" : "Document24";

    /// <summary>
    /// 연결된 OneNote 페이지 ID (파일명에서 추출)
    /// 형식: recording_{pageId}_{timestamp}.wav
    /// </summary>
    public string? LinkedPageId { get; set; }

    /// <summary>
    /// 페이지 연결 여부
    /// </summary>
    public bool IsLinkedToPage => !string.IsNullOrEmpty(LinkedPageId);
}

/// <summary>
/// 채팅 메시지 아이템 모델 (AI 에이전트용)
/// </summary>
public class ChatMessageItem
{
    /// <summary>
    /// 메시지 내용
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 사용자 메시지 여부
    /// </summary>
    public bool IsUser { get; set; }

    /// <summary>
    /// 정렬 (사용자: 오른쪽, AI: 왼쪽)
    /// </summary>
    public System.Windows.HorizontalAlignment Alignment { get; set; }

    /// <summary>
    /// 배경색
    /// </summary>
    public System.Windows.Media.Brush? Background { get; set; }

    /// <summary>
    /// 전경색
    /// </summary>
    public System.Windows.Media.Brush? Foreground { get; set; }
}
