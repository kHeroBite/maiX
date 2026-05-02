using System;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Utils;

namespace mAIx.Services;

/// <summary>
/// 지연 전송 서비스 — 설정 가능한 딜레이 후 메일 발송, 취소 가능
/// </summary>
public class DelayedSendService
{
    private CancellationTokenSource? _currentCts;
    private int _delaySeconds = 5;

    /// <summary>
    /// 현재 대기 중인 전송 여부
    /// </summary>
    public bool IsPending => _currentCts != null && !_currentCts.IsCancellationRequested;

    /// <summary>
    /// 지연 시간 (초) — 5/10/20/30초 옵션
    /// </summary>
    public int DelaySeconds
    {
        get => _delaySeconds;
        set
        {
            if (value is 5 or 10 or 20 or 30)
                _delaySeconds = value;
            else
                _delaySeconds = 5;
        }
    }

    /// <summary>
    /// 사용 가능한 지연 시간 옵션 (초)
    /// </summary>
    public static int[] DelayOptions => [5, 10, 20, 30];

    /// <summary>
    /// 지연 전송 시작 — CancellationTokenSource 반환
    /// 호출자가 sendAction을 전달하면 딜레이 후 실행
    /// </summary>
    /// <param name="sendAction">실제 전송 작업</param>
    /// <param name="delayOverride">지연 시간 오버라이드 (null이면 DelaySeconds 사용)</param>
    /// <returns>취소용 CancellationTokenSource</returns>
    public async Task<bool> SendWithDelayAsync(Func<Task<bool>> sendAction, int? delayOverride = null)
    {
        // 이전 대기 취소
        CancelPendingSend();

        var delay = delayOverride ?? _delaySeconds;
        _currentCts = new CancellationTokenSource();
        var token = _currentCts.Token;

        try
        {
            Log4.Debug($"[DelayedSendService] {delay}초 지연 전송 시작");
            await Task.Delay(delay * 1000, token).ConfigureAwait(false);

            // 지연 완료 — 실제 전송
            Log4.Debug("[DelayedSendService] 지연 완료, 전송 실행");
            var result = await sendAction().ConfigureAwait(false);
            return result;
        }
        catch (TaskCanceledException)
        {
            Log4.Info("[DelayedSendService] 전송 취소됨");
            return false;
        }
        finally
        {
            _currentCts = null;
        }
    }

    /// <summary>
    /// 대기 중인 전송 취소
    /// </summary>
    public void CancelPendingSend()
    {
        if (_currentCts != null && !_currentCts.IsCancellationRequested)
        {
            _currentCts.Cancel();
            Log4.Info("[DelayedSendService] 대기 중인 전송 취소됨");
        }
        _currentCts = null;
    }

    /// <summary>
    /// 취소용 CancellationToken 반환 (View에서 카운트다운 연동용)
    /// </summary>
    public CancellationToken GetCurrentToken()
    {
        return _currentCts?.Token ?? CancellationToken.None;
    }
}
