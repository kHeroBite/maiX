using System;
using System.Windows.Interop;
using System.Windows.Media;
using MaiX.Services.Storage;
using MaiX.Utils;

namespace MaiX.Services.Theme;

/// <summary>
/// 렌더링 모드 관리 서비스 (GPU/CPU 모드 전환)
/// GPU 모드: 하드웨어 가속 사용 (기본 WPF 동작)
/// CPU 모드: 소프트웨어 렌더링 (원격 데스크톱 환경에서 유용)
/// </summary>
public class RenderModeService
{
    /// <summary>
    /// 싱글톤 인스턴스
    /// </summary>
    public static RenderModeService Instance { get; } = new();

    /// <summary>
    /// 설정 매니저 참조
    /// </summary>
    public AppSettingsManager? SettingsManager { get; set; }

    /// <summary>
    /// 현재 GPU 모드 여부
    /// true: GPU 가속 사용, false: CPU 소프트웨어 렌더링
    /// </summary>
    public bool IsGpuMode { get; private set; }

    /// <summary>
    /// 렌더링 모드 변경 이벤트
    /// </summary>
    public event EventHandler? RenderModeChanged;

    private RenderModeService()
    {
        // 기본값: CPU 모드 (소프트웨어 렌더링)
        IsGpuMode = false;
    }

    /// <summary>
    /// 저장된 렌더링 모드 로드 및 적용
    /// </summary>
    /// <param name="commandLineOverride">명령줄에서 강제 지정된 값 (null이면 저장된 설정 사용)</param>
    public void LoadSavedRenderMode(bool? commandLineOverride = null)
    {
        if (commandLineOverride.HasValue)
        {
            // 명령줄 인수로 강제 지정
            IsGpuMode = commandLineOverride.Value;
            Log4.Info($"렌더링 모드 (명령줄 강제): {(IsGpuMode ? "GPU" : "CPU")}");
        }
        else if (SettingsManager != null)
        {
            // 저장된 설정에서 로드
            IsGpuMode = SettingsManager.UserPreferences.UseGpuMode;
            Log4.Info($"렌더링 모드 (저장된 설정): {(IsGpuMode ? "GPU" : "CPU")}");
        }
        else
        {
            // 기본값: CPU 모드
            IsGpuMode = false;
            Log4.Info("렌더링 모드 (기본값): CPU");
        }

        ApplyRenderMode();
    }

    /// <summary>
    /// 현재 설정된 렌더링 모드를 WPF에 적용
    /// 주의: 이 설정은 앱 시작 시점에만 효과적임
    /// </summary>
    public void ApplyRenderMode()
    {
        try
        {
            RenderOptions.ProcessRenderMode = IsGpuMode
                ? RenderMode.Default      // GPU 가속 (하드웨어 렌더링)
                : RenderMode.SoftwareOnly; // CPU 렌더링 (소프트웨어 렌더링)

            Log4.Debug($"RenderOptions.ProcessRenderMode 설정: {RenderOptions.ProcessRenderMode}");
        }
        catch (Exception ex)
        {
            Log4.Error($"렌더링 모드 적용 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// GPU 모드 토글 (CPU ↔ GPU)
    /// </summary>
    public void ToggleGpuMode()
    {
        IsGpuMode = !IsGpuMode;
        ApplyRenderMode();
        SaveRenderModeSetting();
        RenderModeChanged?.Invoke(this, EventArgs.Empty);

        Log4.Info($"렌더링 모드 변경: {(IsGpuMode ? "GPU" : "CPU")} (다음 시작 시 완전 적용)");
    }

    /// <summary>
    /// GPU 모드 직접 설정
    /// </summary>
    /// <param name="useGpuMode">true: GPU 모드, false: CPU 모드</param>
    public void SetGpuMode(bool useGpuMode)
    {
        if (IsGpuMode == useGpuMode) return;

        IsGpuMode = useGpuMode;
        ApplyRenderMode();
        SaveRenderModeSetting();
        RenderModeChanged?.Invoke(this, EventArgs.Empty);

        Log4.Info($"렌더링 모드 설정: {(IsGpuMode ? "GPU" : "CPU")} (다음 시작 시 완전 적용)");
    }

    /// <summary>
    /// 렌더링 모드 설정 저장
    /// </summary>
    private void SaveRenderModeSetting()
    {
        if (SettingsManager != null)
        {
            SettingsManager.UserPreferences.UseGpuMode = IsGpuMode;
            SettingsManager.SaveUserPreferences();
            Log4.Debug($"렌더링 모드 저장: {(IsGpuMode ? "GPU" : "CPU")}");
        }
    }

    /// <summary>
    /// 현재 렌더링 모드 문자열 반환
    /// </summary>
    public string GetCurrentModeString() => IsGpuMode ? "GPU" : "CPU";
}
