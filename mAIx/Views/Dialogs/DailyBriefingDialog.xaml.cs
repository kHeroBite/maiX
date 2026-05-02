using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;
using mAIx.Models;
using mAIx.Services.AI;
using mAIx.Utils;

namespace mAIx.Views.Dialogs;

/// <summary>
/// AI 일일 브리핑 다이얼로그 — 오늘 수신 메일 기반 브리핑 생성
/// </summary>
public partial class DailyBriefingDialog : FluentWindow
{
    private readonly AiMailService _aiMailService;
    private readonly List<Email> _todayEmails;
    private CancellationTokenSource? _cts;

    public DailyBriefingDialog(AiMailService aiMailService, List<Email> todayEmails)
    {
        InitializeComponent();
        _aiMailService = aiMailService;
        _todayEmails = todayEmails;

        Loaded += async (s, e) =>
        {
            try { await GenerateBriefingAsync(); }
            catch (Exception ex) { Log4.Error($"[DailyBriefingDialog] Loaded 핸들러 실패: {ex}"); }
        };
        Closed += (s, e) => _cts?.Cancel();

        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        };
    }

    private async Task GenerateBriefingAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        ResultScrollViewer.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        _cts = new CancellationTokenSource();

        try
        {
            Log4.Debug($"[DailyBriefingDialog] 브리핑 생성 시작 - 메일 수={_todayEmails.Count}");
            var briefing = await _aiMailService.GenerateDailyBriefingAsync(_todayEmails, _cts.Token);

            LoadingPanel.Visibility = Visibility.Collapsed;
            BriefingTextBlock.Text = briefing;
            ResultScrollViewer.Visibility = Visibility.Visible;
            Log4.Info("[DailyBriefingDialog] 브리핑 생성 완료");
        }
        catch (OperationCanceledException)
        {
            Log4.Debug("[DailyBriefingDialog] 브리핑 생성 취소됨");
        }
        catch (Exception ex)
        {
            Log4.Error($"[DailyBriefingDialog] 브리핑 생성 실패: {ex.Message}");
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorTextBlock.Text = $"브리핑 생성에 실패했습니다.\n{ex.Message}";
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
