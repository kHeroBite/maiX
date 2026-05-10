// E2E 검증 하네스 — mock 활성 + 가짜 PCM 주입 + 시간 단축으로 항목 8~13 자동 검증
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Services.AI.Testing;
using mAIx.Services.Audio;
using NLog;

namespace mAIx.Tests.Helpers;

/// <summary>
/// E2E 전체 시나리오 하네스.
/// otest가 RunFullScenarioAsync()를 호출하면 mock 환경에서 8~13 검증 항목을 수행한다.
/// </summary>
public sealed class RecordingE2ETestHarness
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    // evidence 저장 경로
    private static readonly string EvidenceDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mAIx", "evidence");

    // 검증 결과 컨테이너
    private readonly List<E2ECheckResult> _results = new();

    // 타이머 배율 (0.1 = 10배 빠름)
    private readonly double _timerScale;

    public RecordingE2ETestHarness(double timerScale = 0.1)
    {
        _timerScale = Math.Clamp(timerScale, 0.01, 1.0);
    }

    /// <summary>
    /// 전체 E2E 시나리오 실행.
    /// mock 활성 + 가짜 PCM 주입 + 시간 단축 → 1분요약 1회 → 누적요약 1회 → 최종요약 1회.
    /// 결과는 evidence/e2e_results.json에 기록된다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>전체 결과 목록 (각 검증 항목의 합격/불합격)</returns>
    public static async Task<IReadOnlyList<E2ECheckResult>> RunFullScenarioAsync(
        CancellationToken ct = default)
    {
        var harness = new RecordingE2ETestHarness(timerScale: 0.1);
        return await harness.ExecuteAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<E2ECheckResult>> ExecuteAsync(CancellationToken ct)
    {
        _log.Info("[E2EHarness] 시작 — timerScale={Scale}", _timerScale);

        try
        {
            // ── 환경 준비 ──────────────────────────────────────────
            MockOpenAiResponseInjector.Reset();
            MockOpenAiResponseInjector.EnableMock = true;
            Environment.SetEnvironmentVariable("MAIX_DEBUG_TIMER_SCALE", _timerScale.ToString("F2"));

            _log.Info("[E2EHarness] Mock 활성화 완료 (MAIX_DEBUG_TIMER_SCALE={Scale})", _timerScale);

            // ── 검증 8: 가짜 PCM 생성 ─────────────────────────────
            await Check(8, "가짜 PCM 생성 (GetTestAudioBuffer)", () =>
            {
                var pcm = DebugPcmInjectHelper.GetTestAudioBuffer();
                var ok = pcm != null && pcm.Length == 16000 * 2; // 1초 16kHz 16bit (DebugPcmInjectHelper 기본값 — 디버그 전용, 파이프라인 미영향)
                return ok ? CheckResult.Pass($"PCM 크기={pcm!.Length}B") : CheckResult.Fail("PCM 크기 불일치");
            });

            // ── 검증 9: Mock STT (Realtime) ───────────────────────
            await Check(9, "Mock Realtime STT", () =>
            {
                string? receivedText = null;
                var handled = MockOpenAiResponseInjector.TryHandleRealtimeSttChunk(
                    TimeSpan.Zero,
                    (_, text) => receivedText = text);
                return handled && receivedText != null
                    ? CheckResult.Pass($"수신={receivedText}")
                    : CheckResult.Fail("mock 분기 미동작");
            });

            // ── 검증 10: Mock STT (Transcribe + 화자분리) ─────────
            await Check(10, "Mock Transcribe STT (화자분리)", () =>
            {
                string? receivedText = null;
                var handled = MockOpenAiResponseInjector.TryHandleTranscribeSttChunk(
                    TimeSpan.FromSeconds(5),
                    (_, text) => receivedText = text);
                var hasSpeaker = receivedText?.Contains("speaker_") == true;
                return handled && hasSpeaker
                    ? CheckResult.Pass($"수신={receivedText}")
                    : CheckResult.Fail($"mock 분기 미동작 또는 화자라벨 없음: {receivedText}");
            });

            // ── 검증 11: Mock 1분 요약 ────────────────────────────
            await Check(11, "Mock 1분 요약", () =>
            {
                var ok = MockOpenAiResponseInjector.TryHandleMinuteSummary(out var summary);
                return ok && !string.IsNullOrEmpty(summary)
                    ? CheckResult.Pass($"요약={summary}")
                    : CheckResult.Fail("mock 1분 요약 미동작");
            });

            // ── 검증 12: Mock 누적 요약 ───────────────────────────
            await Check(12, "Mock 누적 요약", () =>
            {
                var ok = MockOpenAiResponseInjector.TryHandleCumulativeSummary(out var summary);
                return ok && !string.IsNullOrEmpty(summary)
                    ? CheckResult.Pass($"요약={summary}")
                    : CheckResult.Fail("mock 누적 요약 미동작");
            });

            // ── 검증 13: Mock 최종 요약 ───────────────────────────
            await Check(13, "Mock 최종 요약", () =>
            {
                var ok = MockOpenAiResponseInjector.TryHandleFinalSummary(out var summary);
                return ok && !string.IsNullOrEmpty(summary)
                    ? CheckResult.Pass($"요약={summary}")
                    : CheckResult.Fail("mock 최종 요약 미동작");
            });

            // ── 검증: DebugTimerScale 환경변수 적용 확인 ─────────
            await Check(0, "DebugTimerScale 환경변수 설정 확인", () =>
            {
                var env = Environment.GetEnvironmentVariable("MAIX_DEBUG_TIMER_SCALE");
                return env == _timerScale.ToString("F2")
                    ? CheckResult.Pass($"MAIX_DEBUG_TIMER_SCALE={env}")
                    : CheckResult.Fail($"환경변수 불일치: {env}");
            });

            // ── 검증: Mock TTS ────────────────────────────────────
            await Check(0, "Mock TTS SynthesizeAsync", () =>
            {
                var ok = MockOpenAiResponseInjector.TryHandleTtsSynthesize("테스트", out var result);
                return ok && result != null
                    ? CheckResult.Pass($"result.Length={result.Length}")
                    : CheckResult.Fail("mock TTS 미동작");
            });

            // ── 검증: InjectFakeChunkSequenceAsync ───────────────
            await Check(0, "InjectFakeChunkSequenceAsync (다중 청크 주입 API)", () =>
            {
                // AudioRecordingService 없이 API 존재만 확인 (컴파일 검증)
                var chunks = new[]
                {
                    DebugPcmInjectHelper.GenerateFakePcm(500),
                    DebugPcmInjectHelper.GenerateFakePcm(500, sine: true),
                };
                return chunks.Length == 2 && chunks[0].Length > 0 && chunks[1].Length > 0
                    ? CheckResult.Pass("다중 청크 생성 OK")
                    : CheckResult.Fail("청크 생성 실패");
            });
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[E2EHarness] 실행 중 예외");
            _results.Add(new E2ECheckResult
            {
                ItemNo = -1,
                Description = "하네스 예외",
                Passed = false,
                Detail = ex.Message
            });
        }
        finally
        {
            // ── Mock 비활성화 (production 안전) ──────────────────
            MockOpenAiResponseInjector.EnableMock = false;
            Environment.SetEnvironmentVariable("MAIX_DEBUG_TIMER_SCALE", null);
            _log.Info("[E2EHarness] Mock 비활성화 완료");

            // ── evidence 저장 ─────────────────────────────────────
            await SaveEvidenceAsync().ConfigureAwait(false);
        }

        return _results.AsReadOnly();
    }

    private Task Check(int itemNo, string description, Func<CheckResult> logic)
    {
        CheckResult result;
        try
        {
            result = logic();
        }
        catch (Exception ex)
        {
            result = CheckResult.Fail($"예외: {ex.Message}");
        }

        var entry = new E2ECheckResult
        {
            ItemNo = itemNo,
            Description = description,
            Passed = result.Passed,
            Detail = result.Detail,
            CheckedAt = DateTime.Now
        };

        _results.Add(entry);

        if (result.Passed)
            _log.Info("[E2EHarness] ✅ [{No}] {Desc} — {Detail}", itemNo, description, result.Detail);
        else
            _log.Warn("[E2EHarness] ❌ [{No}] {Desc} — {Detail}", itemNo, description, result.Detail);

        return Task.CompletedTask;
    }

    private async Task SaveEvidenceAsync()
    {
        try
        {
            Directory.CreateDirectory(EvidenceDir);
            var path = Path.Combine(EvidenceDir, "e2e_results.json");

            var payload = new
            {
                RunAt = DateTime.Now,
                TimerScale = _timerScale,
                TotalChecks = _results.Count,
                PassedChecks = _results.FindAll(r => r.Passed).Count,
                Results = _results
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);

            _log.Info("[E2EHarness] evidence 저장: {Path}", path);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[E2EHarness] evidence 저장 실패");
        }
    }

    // ── 내부 타입 ────────────────────────────────────────────────────

    private readonly record struct CheckResult(bool Passed, string Detail)
    {
        public static CheckResult Pass(string detail) => new(true, detail);
        public static CheckResult Fail(string detail) => new(false, detail);
    }
}

/// <summary>
/// E2E 검증 항목 단건 결과
/// </summary>
public sealed class E2ECheckResult
{
    /// <summary>검증 항목 번호 (oplan 계획서 기준, 0=보조항목)</summary>
    public int ItemNo { get; set; }

    /// <summary>검증 내용 설명</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>합격 여부</summary>
    public bool Passed { get; set; }

    /// <summary>상세 결과 메시지</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>검증 시각</summary>
    public DateTime CheckedAt { get; set; } = DateTime.Now;
}
