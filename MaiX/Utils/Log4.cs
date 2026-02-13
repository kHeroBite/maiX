/*
 * ==========================================
 * MaiX 로깅 시스템 - MARS LOG4 클래스 이식
 * ==========================================
 *
 * log4net 라이브러리를 래핑한 통합 로깅 관리 클래스입니다.
 *
 * 주요 기능:
 * - 다양한 로그 레벨 지원 (Info, Warn, Error, Debug, Fatal)
 * - 파일/콘솔 이중 출력
 * - 스택 트레이스 자동 추가
 * - 스레드 안전한 로깅
 *
 * 로그 저장 경로: %APPDATA%\MaiX\logs\yyyyMMdd.log
 */

using log4net;
using log4net.Appender;
using log4net.Config;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

#nullable enable

namespace MaiX.Utils;

/// <summary>
/// log4net 라이브러리를 래핑한 통합 로깅 관리 클래스입니다.
/// </summary>
public static class Log4
{
    // 성능 최적화를 위한 상수 정의
    private const string StarBorder = "★★★★★★★★★★";
    private const string ErrorTraceHeader = "\t==========ERROR TRACE==========";
    private const string FatalTraceHeader = "\t==========FATAL TRACE==========";

    // 스레드 안전성을 위한 lock 객체와 카운터
    private static readonly object _lockObject = new();
    private static uint _log4Count = 0;

    // ILog 인스턴스 재사용으로 성능 향상
    private static readonly ILog _logger = LogManager.GetLogger(typeof(App));

    // 디버그 모드 플래그
    public static bool DebugMode { get; set; } = true;

    /// <summary>
    /// 현재까지 기록된 총 로그 개수 (스레드 안전)
    /// </summary>
    public static uint LogCount
    {
        get
        {
            lock (_lockObject)
            {
                return _log4Count;
            }
        }
    }

    /// <summary>
    /// log4net 초기화 - 앱 시작 시 호출 필요
    /// </summary>
    public static void Initialize()
    {
        try
        {
            // AppData 경로 설정
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logPath = Path.Combine(appDataPath, "MaiX", "logs");

            // 로그 폴더 자동 생성
            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }

            // log4net 설정 파일 로드 (실행 파일 디렉토리 기준)
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var configFilePath = Path.Combine(exeDir, "log4net.config");
            var configFile = new FileInfo(configFilePath);

            System.Diagnostics.Debug.WriteLine($"[Log4] 설정 파일 경로: {configFilePath}");
            System.Diagnostics.Debug.WriteLine($"[Log4] 설정 파일 존재: {configFile.Exists}");

            if (configFile.Exists)
            {
                XmlConfigurator.Configure(configFile);
                System.Diagnostics.Debug.WriteLine("[Log4] XmlConfigurator.Configure 완료");
            }
            else
            {
                // 설정 파일이 없으면 기본 콘솔 출력
                BasicConfigurator.Configure();
                System.Diagnostics.Debug.WriteLine("[Log4] BasicConfigurator.Configure 사용 (config 파일 없음)");
            }

            // RollingFileAppender의 파일 경로를 동적으로 변경
            var repository = LogManager.GetRepository();
            foreach (var appender in repository.GetAppenders())
            {
                if (appender is RollingFileAppender rollingAppender)
                {
                    rollingAppender.File = logPath + Path.DirectorySeparatorChar;
                    rollingAppender.ActivateOptions();
                }
            }

            Info($"★☆★☆★☆★☆★☆★☆★☆★☆★☆★ MaiX 로깅 시스템 초기화 완료 ★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★☆★");
            Info($"로그 저장 경로: {logPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"log4net 설정 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 테스트용 강조 표시 로그를 기록합니다.
    /// </summary>
    public static void Test(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;

        try
        {
            var formattedMessage = $"{StarBorder}{Environment.NewLine}{message}{Environment.NewLine}{StarBorder}";
            _logger.Info(formattedMessage);
            System.Diagnostics.Debug.WriteLine(formattedMessage);
            IncrementLogCount();
        }
        catch (Exception ex)
        {
            HandleLoggingException(ex, message);
        }
    }

    /// <summary>
    /// 일반 정보 로그를 기록합니다.
    /// </summary>
    public static void Info(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;

        try
        {
            _logger.Info(message);
            System.Diagnostics.Debug.WriteLine(message);
            IncrementLogCount();
        }
        catch (Exception ex)
        {
            HandleLoggingException(ex, message);
        }
    }

    /// <summary>
    /// 디버그 로그를 기록합니다.
    /// DebugMode가 true일 때에만 동작합니다.
    /// </summary>
    public static void Debug(string? message) => Debug(message, false);

    /// <summary>
    /// 디버그 로그를 기록합니다.
    /// </summary>
    public static void Debug(
        string? message,
        bool force = false,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (string.IsNullOrEmpty(message)) return;

        try
        {
            // DebugMode가 true일 때에만 디버그 로그 실행
            if (!DebugMode && !force)
                return;

            var currentCount = LogCount;
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var fileName = Path.GetFileName(filePath);

            var formattedMessage = $"{Environment.NewLine}====================[ {currentCount:00000000} ] {timestamp} [ {fileName}:{lineNumber} - {memberName} ]===================================={Environment.NewLine}{message}{Environment.NewLine}--------------------[ {currentCount:00000000} ] {timestamp}--------------------------------------------------------------------------{Environment.NewLine}";

            _logger.Debug(formattedMessage);
            System.Diagnostics.Debug.WriteLine(formattedMessage);

            IncrementLogCount();
        }
        catch (Exception ex)
        {
            HandleLoggingException(ex, message);
        }
    }

    /// <summary>
    /// 강제 디버그 로그를 기록합니다 (DebugMode 무시).
    /// </summary>
    public static void Debug2(
        string? message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        Debug(message, force: true, memberName: memberName, filePath: filePath, lineNumber: lineNumber);
    }

    /// <summary>
    /// 경고 로그를 기록합니다.
    /// </summary>
    public static void Warn(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;

        try
        {
            _logger.Warn(message);
            System.Diagnostics.Debug.WriteLine(message);
            IncrementLogCount();
        }
        catch (Exception ex)
        {
            HandleLoggingException(ex, message);
        }
    }

    /// <summary>
    /// 오류 로그를 기록합니다. 스택 트레이스 자동 포함.
    /// </summary>
    public static void Error(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;

        try
        {
            var stackTrace = new System.Diagnostics.StackTrace(true);
            var enhancedMessage = BuildStackTraceMessage(message, stackTrace, ErrorTraceHeader);

            _logger.Error(enhancedMessage);
            System.Diagnostics.Debug.WriteLine(enhancedMessage);

            IncrementLogCount();
        }
        catch (Exception ex)
        {
            HandleLoggingException(ex, message);
        }
    }

    /// <summary>
    /// 치명적 오류 로그를 기록합니다.
    /// </summary>
    public static void Fatal(Exception? exception)
    {
        if (exception == null) return;

        try
        {
            var stackTrace = new System.Diagnostics.StackTrace(true);
            var message = BuildFatalTraceMessage(exception, stackTrace);

            _logger.Fatal(message);
            System.Diagnostics.Debug.WriteLine(message);

            IncrementLogCount();
        }
        catch (Exception ex)
        {
            HandleLoggingException(ex, exception.Message);
        }
    }

    // 공통 헬퍼 메서드들

    /// <summary>
    /// 스레드 안전하게 로그 카운트를 증가시킵니다.
    /// </summary>
    private static void IncrementLogCount()
    {
        lock (_lockObject)
        {
            _log4Count++;
        }
    }

    /// <summary>
    /// 스택 트레이스 메시지를 구성합니다.
    /// </summary>
    private static string BuildStackTraceMessage(string message, System.Diagnostics.StackTrace stackTrace, string header)
    {
        var result = $"{message}{Environment.NewLine}{header}";

        var frames = stackTrace.GetFrames();
        if (frames != null)
        {
            result += string.Join(Environment.NewLine,
                frames.Select(frame => $"\t파일명: {frame.GetFileName()}, {frame.GetFileLineNumber()} 번째 줄, 함수명: {frame.GetMethod()}"));
        }

        return result;
    }

    /// <summary>
    /// 치명적 오류 트레이스 메시지를 구성합니다.
    /// </summary>
    private static string BuildFatalTraceMessage(Exception exception, System.Diagnostics.StackTrace stackTrace)
    {
        var message = $"{Environment.NewLine}{FatalTraceHeader}";
        message += $"{Environment.NewLine}>>> Message{Environment.NewLine}{exception}";
        message += $"{Environment.NewLine}>>> StackTrace{Environment.NewLine}{exception.StackTrace}";
        message += $"{Environment.NewLine}>>> Exception";

        var frames = stackTrace.GetFrames();
        if (frames != null)
        {
            message += string.Join(Environment.NewLine,
                frames.Select(frame => $"{Environment.NewLine}\t파일명: {frame.GetFileName()}, {frame.GetFileLineNumber()} 번째 줄, 함수명: {frame.GetMethod()}"));
        }

        return message;
    }

    /// <summary>
    /// 로깅 중 발생한 예외를 처리합니다.
    /// </summary>
    private static void HandleLoggingException(Exception exception, string? originalMessage)
    {
        try
        {
            var errorMsg = $"로깅 실패 - 원본 메시지: {originalMessage ?? "(null)"}, 오류: {exception.Message}";
            System.Diagnostics.Debug.WriteLine(errorMsg);
            Console.WriteLine(errorMsg); // 콘솔 폴백
        }
        catch
        {
            // 최후의 폴백
            Console.WriteLine($"심각한 로깅 오류 발생: {exception.Message}");
        }
    }
}
