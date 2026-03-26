using System;
using System.IO;
using NAudio.Wave;
using Serilog;

namespace mAIx.Services.Audio;

/// <summary>
/// 내장 오디오 플레이어 서비스 (NAudio 사용)
/// WMA, MP3, WAV 등 다양한 형식 지원
/// </summary>
public class AudioPlayerService : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<AudioPlayerService>();

    private WaveOutEvent? _waveOut;
    private WaveStream? _audioStream;        // WMA/MP3 등 다양한 형식 지원
    private IWaveProvider? _waveProvider;    // 볼륨 조절용
    private System.Timers.Timer? _positionTimer;
    private bool _isDisposed;
    private float _volume = 1.0f;

    /// <summary>
    /// 현재 재생 위치
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get => _audioStream?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_audioStream != null && value >= TimeSpan.Zero && value <= TotalDuration)
            {
                _audioStream.CurrentTime = value;
            }
        }
    }

    /// <summary>
    /// 전체 길이
    /// </summary>
    public TimeSpan TotalDuration => _audioStream?.TotalTime ?? TimeSpan.Zero;

    /// <summary>
    /// 현재 재생 상태
    /// </summary>
    public PlaybackState State => _waveOut?.PlaybackState ?? PlaybackState.Stopped;

    /// <summary>
    /// 재생 중 여부
    /// </summary>
    public bool IsPlaying => State == PlaybackState.Playing;

    /// <summary>
    /// 일시정지 여부
    /// </summary>
    public bool IsPaused => State == PlaybackState.Paused;

    /// <summary>
    /// 정지 여부
    /// </summary>
    public bool IsStopped => State == PlaybackState.Stopped;

    /// <summary>
    /// 볼륨 (0.0 ~ 1.0)
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_waveOut != null)
            {
                _waveOut.Volume = _volume;
            }
        }
    }

    /// <summary>
    /// 현재 로드된 파일 경로
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    /// <summary>
    /// 재생 위치 변경 이벤트 (100ms 간격)
    /// </summary>
    public event Action<TimeSpan>? PositionChanged;

    /// <summary>
    /// 재생 완료/중지 이벤트
    /// </summary>
    public event Action? PlaybackStopped;

    /// <summary>
    /// 재생 상태 변경 이벤트
    /// </summary>
    public event Action<PlaybackState>? StateChanged;

    /// <summary>
    /// 오디오 파일 로드
    /// WMA, MP3, WAV, AIFF 등 다양한 형식 지원
    /// </summary>
    /// <param name="filePath">오디오 파일 경로</param>
    public void Load(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("오디오 파일을 찾을 수 없습니다.", filePath);

        try
        {
            // 기존 재생 정리
            Stop();
            Cleanup();

            // 확장자에 따라 적절한 리더 선택
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            _audioStream = extension switch
            {
                // WMA, WMV - MediaFoundationReader 사용
                ".wma" or ".wmv" or ".asf" => new MediaFoundationReader(filePath),
                // MP3 - Mp3FileReader 또는 MediaFoundationReader
                ".mp3" => new Mp3FileReader(filePath),
                // AAC, M4A - MediaFoundationReader 사용
                ".aac" or ".m4a" or ".mp4" => new MediaFoundationReader(filePath),
                // WAV - WaveFileReader 사용
                ".wav" => new WaveFileReader(filePath),
                // AIFF - AiffFileReader 사용
                ".aiff" or ".aif" => new AiffFileReader(filePath),
                // 그 외 - MediaFoundationReader로 시도
                _ => new MediaFoundationReader(filePath)
            };

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioStream);
            _waveOut.Volume = _volume;
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            CurrentFilePath = filePath;

            // 위치 업데이트 타이머 설정
            _positionTimer = new System.Timers.Timer(100); // 100ms 간격
            _positionTimer.Elapsed += OnPositionTimerElapsed;

            _logger.Debug("오디오 파일 로드: {FilePath}, 길이: {Duration}, 형식: {Format}",
                filePath, TotalDuration, extension);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "오디오 파일 로드 실패: {FilePath}", filePath);
            Cleanup();
            throw;
        }
    }

    /// <summary>
    /// 재생 시작
    /// </summary>
    public void Play()
    {
        if (_waveOut == null || _audioStream == null)
        {
            _logger.Warning("재생할 오디오가 로드되지 않았습니다.");
            return;
        }

        try
        {
            _waveOut.Play();
            _positionTimer?.Start();
            StateChanged?.Invoke(PlaybackState.Playing);
            _logger.Debug("재생 시작: {Position}/{Duration}", CurrentPosition, TotalDuration);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "재생 시작 실패");
        }
    }

    /// <summary>
    /// 일시정지
    /// </summary>
    public void Pause()
    {
        if (_waveOut == null)
            return;

        try
        {
            _waveOut.Pause();
            _positionTimer?.Stop();
            StateChanged?.Invoke(PlaybackState.Paused);
            _logger.Debug("일시정지: {Position}", CurrentPosition);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일시정지 실패");
        }
    }

    /// <summary>
    /// 재생/일시정지 토글
    /// </summary>
    public void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    /// <summary>
    /// 정지
    /// </summary>
    public void Stop()
    {
        if (_waveOut == null)
            return;

        try
        {
            _positionTimer?.Stop();
            _waveOut.Stop();

            if (_audioStream != null)
            {
                _audioStream.CurrentTime = TimeSpan.Zero;
            }

            StateChanged?.Invoke(PlaybackState.Stopped);
            _logger.Debug("재생 정지");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "정지 실패");
        }
    }

    /// <summary>
    /// 특정 위치로 이동
    /// </summary>
    /// <param name="position">이동할 위치</param>
    public void Seek(TimeSpan position)
    {
        if (_audioStream == null)
            return;

        try
        {
            var clampedPosition = TimeSpan.FromTicks(
                Math.Clamp(position.Ticks, 0, TotalDuration.Ticks));

            _audioStream.CurrentTime = clampedPosition;
            PositionChanged?.Invoke(clampedPosition);
            _logger.Debug("위치 이동: {Position}", clampedPosition);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "위치 이동 실패: {Position}", position);
        }
    }

    /// <summary>
    /// 상대 위치로 이동 (초 단위)
    /// </summary>
    /// <param name="seconds">이동할 초 (양수: 앞으로, 음수: 뒤로)</param>
    public void SeekRelative(double seconds)
    {
        var newPosition = CurrentPosition + TimeSpan.FromSeconds(seconds);
        Seek(newPosition);
    }

    /// <summary>
    /// 5초 뒤로
    /// </summary>
    public void SeekBackward() => SeekRelative(-5);

    /// <summary>
    /// 5초 앞으로
    /// </summary>
    public void SeekForward() => SeekRelative(5);

    private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_audioStream == null || _isDisposed)
            return;

        try
        {
            PositionChanged?.Invoke(CurrentPosition);
        }
        catch
        {
            // 타이머 콜백에서 예외 무시
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        _positionTimer?.Stop();

        if (e.Exception != null)
        {
            _logger.Error(e.Exception, "재생 중 오류 발생");
        }

        // 재생이 끝까지 완료되었는지 확인
        if (_audioStream != null && CurrentPosition >= TotalDuration - TimeSpan.FromMilliseconds(100))
        {
            _audioStream.CurrentTime = TimeSpan.Zero;
        }

        StateChanged?.Invoke(PlaybackState.Stopped);
        PlaybackStopped?.Invoke();
    }

    private void Cleanup()
    {
        _positionTimer?.Stop();
        _positionTimer?.Dispose();
        _positionTimer = null;

        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }

        _audioStream?.Dispose();
        _audioStream = null;
        _waveProvider = null;

        CurrentFilePath = null;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Stop();
        Cleanup();
    }
}
