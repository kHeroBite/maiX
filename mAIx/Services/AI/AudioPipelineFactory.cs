// 옵션 기반 파이프라인 인스턴스 생성 - 모드 변경 즉시 적용
using System;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using mAIx.Services.Storage;

namespace mAIx.Services.AI;

/// <summary>
/// AppSettingsManager.OaiRecording.AudioPipelineMode에 따라 적절한 IRealtimeAudioPipeline 구현체를 생성하는 팩토리.
/// Wave 1: 인터페이스/팩토리 정의. 구현체(LegacyAudioPipeline, UnifiedRealtimeAudioPipeline)는 Wave 2에서 제공.
/// </summary>
public class AudioPipelineFactory
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _serviceProvider;
    private readonly AppSettingsManager _settings;

    public AudioPipelineFactory(IServiceProvider serviceProvider, AppSettingsManager settings)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// 현재 AppSettingsManager 기반으로 파이프라인 인스턴스 생성.
    /// </summary>
    public IRealtimeAudioPipeline Create()
    {
        var mode = _settings.OaiRecording.AudioPipelineMode;
        _log.Info("파이프라인 생성 요청 — 모드: {0}", mode);

        return mode switch
        {
            AudioPipelineMode.Unified => CreateUnified(),
            _ => CreateLegacy(),
        };
    }

    /// <summary>
    /// Legacy 모드 파이프라인 인스턴스 생성 (폴백 시 직접 호출).
    /// </summary>
    public IRealtimeAudioPipeline CreateLegacyFallback()
    {
        _log.Info("Legacy 폴백 파이프라인 생성");
        return CreateLegacy();
    }

    private IRealtimeAudioPipeline CreateLegacy()
    {
        // Wave 2 (odev-2)에서 LegacyAudioPipeline이 DI에 등록되면 resolve
        var pipeline = _serviceProvider.GetService<LegacyAudioPipeline>();
        if (pipeline is null)
            throw new InvalidOperationException("LegacyAudioPipeline이 DI에 등록되지 않았습니다. App.xaml.cs를 확인하세요.");
        return pipeline;
    }

    private IRealtimeAudioPipeline CreateUnified()
    {
        // Wave 2 (odev-3)에서 UnifiedRealtimeAudioPipeline이 DI에 등록되면 resolve
        var pipeline = _serviceProvider.GetService<UnifiedRealtimeAudioPipeline>();
        if (pipeline is null)
            throw new InvalidOperationException("UnifiedRealtimeAudioPipeline이 DI에 등록되지 않았습니다. App.xaml.cs를 확인하세요.");
        return pipeline;
    }
}
