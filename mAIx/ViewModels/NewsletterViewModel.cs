using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Models;
using mAIx.Services;
using mAIx.Services.Graph;
using mAIx.Utils;

namespace mAIx.ViewModels;

/// <summary>
/// 뉴스레터 관리 ViewModel — 뉴스레터 목록 표시 + 원클릭 구독 취소
/// </summary>
public partial class NewsletterViewModel : ObservableObject
{
    private readonly UnsubscribeService _unsubscribeService;
    private readonly TrackingBlockerService _trackingBlockerService;
    private readonly GraphMailService _graphMailService;

    /// <summary>뉴스레터 이메일 목록</summary>
    public ObservableCollection<Email> Newsletters { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private Email? _selectedNewsletter;

    public NewsletterViewModel(
        UnsubscribeService unsubscribeService,
        TrackingBlockerService trackingBlockerService,
        GraphMailService graphMailService)
    {
        _unsubscribeService = unsubscribeService;
        _trackingBlockerService = trackingBlockerService;
        _graphMailService = graphMailService;
    }

    /// <summary>
    /// 뉴스레터 목록 로드
    /// </summary>
    public async Task LoadNewslettersAsync()
    {
        IsLoading = true;
        StatusMessage = "뉴스레터를 불러오는 중...";

        try
        {
            Newsletters.Clear();
            // 추후 DB 또는 Graph API에서 뉴스레터 필터링하여 로드
            await Task.CompletedTask;
            StatusMessage = $"뉴스레터 {Newsletters.Count}개 로드 완료";
        }
        catch (Exception ex)
        {
            Log4.Error($"[NewsletterViewModel] 목록 로드 실패: {ex.Message}");
            StatusMessage = "목록 로드 실패";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 선택된 뉴스레터 구독 취소
    /// </summary>
    [RelayCommand]
    private async Task UnsubscribeAsync()
    {
        if (SelectedNewsletter?.EntryId == null) return;

        IsLoading = true;
        StatusMessage = "구독 취소 중...";

        try
        {
            var success = await _unsubscribeService.UnsubscribeAsync(
                SelectedNewsletter.EntryId, _graphMailService);

            if (success)
            {
                Newsletters.Remove(SelectedNewsletter);
                StatusMessage = "구독 취소 완료";
                Log4.Info($"[NewsletterViewModel] 구독 취소 성공: {SelectedNewsletter.Subject}");
            }
            else
            {
                StatusMessage = "구독 취소 실패 — 수동으로 처리하세요";
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[NewsletterViewModel] 구독 취소 오류: {ex.Message}");
            StatusMessage = "구독 취소 중 오류 발생";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
