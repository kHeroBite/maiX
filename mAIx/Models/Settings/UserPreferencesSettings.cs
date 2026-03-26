using System.Xml.Serialization;

namespace mAIx.Models.Settings;

/// <summary>
/// 사용자 환경 설정 (테마, UI 설정 등)
/// </summary>
[XmlRoot("UserPreferences")]
public class UserPreferencesSettings
{
    /// <summary>
    /// 테마 설정 (Dark, Light)
    /// </summary>
    [XmlElement("Theme")]
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// 다크 모드 여부 (편의 프로퍼티)
    /// </summary>
    [XmlIgnore]
    public bool IsDarkMode
    {
        get => Theme == "Dark";
        set => Theme = value ? "Dark" : "Light";
    }

    /// <summary>
    /// GPU 모드 사용 여부 (기본값: false = CPU 소프트웨어 렌더링)
    /// true: GPU 가속 사용, false: CPU 소프트웨어 렌더링
    /// </summary>
    [XmlElement("UseGpuMode")]
    public bool UseGpuMode { get; set; } = false;

    /// <summary>
    /// 메일 동기화 기간 타입 (Count, Days, Weeks, Months, Years, All)
    /// </summary>
    [XmlElement("MailSyncPeriodType")]
    public string MailSyncPeriodType { get; set; } = "Count";

    /// <summary>
    /// 메일 동기화 기간 값
    /// </summary>
    [XmlElement("MailSyncPeriodValue")]
    public int MailSyncPeriodValue { get; set; } = 5;

    /// <summary>
    /// 메일 동기화 주기 (분 단위) - 하위 호환용, MailSyncIntervalSeconds 우선
    /// </summary>
    [XmlElement("MailSyncIntervalMinutes")]
    public int MailSyncIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// 메일 동기화 주기 (초 단위) - 1초~1시간(3600초) 지원
    /// </summary>
    [XmlElement("MailSyncIntervalSeconds")]
    public int MailSyncIntervalSeconds { get; set; } = 0;

    /// <summary>
    /// 메일 동기화 일시정지 상태
    /// </summary>
    [XmlElement("IsMailSyncPaused")]
    public bool IsMailSyncPaused { get; set; } = false;

    /// <summary>
    /// AI 분석 기간 타입 (Count, Days, Weeks, Months, Years, All)
    /// </summary>
    [XmlElement("AiAnalysisPeriodType")]
    public string AiAnalysisPeriodType { get; set; } = "Count";

    /// <summary>
    /// AI 분석 기간 값
    /// </summary>
    [XmlElement("AiAnalysisPeriodValue")]
    public int AiAnalysisPeriodValue { get; set; } = 5;

    /// <summary>
    /// AI 분석 주기 (초 단위) - 1초~1시간(3600초) 지원
    /// </summary>
    [XmlElement("AiAnalysisIntervalSeconds")]
    public int AiAnalysisIntervalSeconds { get; set; } = 0;

    /// <summary>
    /// AI 분석 일시정지 상태
    /// </summary>
    [XmlElement("IsAiAnalysisPaused")]
    public bool IsAiAnalysisPaused { get; set; } = false;

    /// <summary>
    /// 즐겨찾기 메일 동기화 기간 타입 (Count, Days, Weeks, Months, Years, All)
    /// </summary>
    [XmlElement("FavoriteSyncPeriodType")]
    public string FavoriteSyncPeriodType { get; set; } = "Count";

    /// <summary>
    /// 즐겨찾기 메일 동기화 기간 값
    /// </summary>
    [XmlElement("FavoriteSyncPeriodValue")]
    public int FavoriteSyncPeriodValue { get; set; } = 5;

    /// <summary>
    /// 즐겨찾기 메일 동기화 주기 (초 단위) - 기본 30초
    /// </summary>
    [XmlElement("FavoriteSyncIntervalSeconds")]
    public int FavoriteSyncIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// 전체메일 동기화 주기 (초 단위) - 기본 5분(300초)
    /// </summary>
    [XmlElement("FullSyncIntervalSeconds")]
    public int FullSyncIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// 즐겨찾기 AI 분석 기간 타입 (Count, Days, Weeks, Months, Years, All)
    /// </summary>
    [XmlElement("FavoriteAiPeriodType")]
    public string FavoriteAiPeriodType { get; set; } = "Count";

    /// <summary>
    /// 즐겨찾기 AI 분석 기간 값
    /// </summary>
    [XmlElement("FavoriteAiPeriodValue")]
    public int FavoriteAiPeriodValue { get; set; } = 5;

    /// <summary>
    /// 즐겨찾기 AI 분석 주기 (초 단위) - 기본 30초
    /// </summary>
    [XmlElement("FavoriteAnalysisIntervalSeconds")]
    public int FavoriteAnalysisIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// 전체메일 AI 분석 주기 (초 단위) - 기본 5분(300초)
    /// </summary>
    [XmlElement("FullAnalysisIntervalSeconds")]
    public int FullAnalysisIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// 캘린더 동기화 주기 (초 단위) - 기본 1분(60초)
    /// </summary>
    [XmlElement("CalendarSyncIntervalSeconds")]
    public int CalendarSyncIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 채팅 동기화 주기 (초 단위) - 기본 2분(120초)
    /// </summary>
    [XmlElement("ChatSyncIntervalSeconds")]
    public int ChatSyncIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// 창 위치 X (Left)
    /// </summary>
    [XmlElement("WindowLeft")]
    public double? WindowLeft { get; set; }

    /// <summary>
    /// 창 위치 Y (Top)
    /// </summary>
    [XmlElement("WindowTop")]
    public double? WindowTop { get; set; }

    /// <summary>
    /// 창 너비
    /// </summary>
    [XmlElement("WindowWidth")]
    public double? WindowWidth { get; set; }

    /// <summary>
    /// 창 높이
    /// </summary>
    [XmlElement("WindowHeight")]
    public double? WindowHeight { get; set; }

    /// <summary>
    /// 창 상태 ("Normal", "Maximized")
    /// </summary>
    [XmlElement("WindowState")]
    public string? WindowState { get; set; }

    /// <summary>
    /// 선호 마이크 장치 ID
    /// </summary>
    [XmlElement("PreferredMicrophoneDeviceId")]
    public string? PreferredMicrophoneDeviceId { get; set; }

    /// <summary>
    /// STT 모드 (server: Jarvis 서버)
    /// </summary>
    [XmlElement("SttMode")]
    public string SttMode { get; set; } = "server";

    /// <summary>
    /// TTS 모드 (client: 로컬, server: Jarvis 서버)
    /// </summary>
    [XmlElement("TtsMode")]
    public string TtsMode { get; set; } = "client";

    /// <summary>
    /// 음성 서버 URL (STT/TTS/화자분리 서버 주소)
    /// </summary>
    [XmlElement("SpeechServerUrl")]
    public string SpeechServerUrl { get; set; } = "http://172.10.74.2:18989";

    /// <summary>
    /// 서버 STT 모델 (small, medium, large-v3)
    /// </summary>
    [XmlElement("ServerSttModel")]
    public string ServerSttModel { get; set; } = "small";

    /// <summary>
    /// 녹음 STT 분석 주기 (초 단위)
    /// </summary>
    [XmlElement("STTIntervalSeconds")]
    public float STTIntervalSeconds { get; set; } = 15f;

    /// <summary>
    /// AI 요약 주기 (초 단위)
    /// </summary>
    [XmlElement("SummaryIntervalSeconds")]
    public int SummaryIntervalSeconds { get; set; } = 30;

}
