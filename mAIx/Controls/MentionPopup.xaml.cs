using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using mAIx.ViewModels;
using Serilog;

namespace mAIx.Controls;

/// <summary>
/// @멘션 자동완성 팝업
/// </summary>
public partial class MentionPopup : UserControl
{
    private static readonly ILogger _logger = Log.ForContext<MentionPopup>();

    /// <summary>
    /// 멘션 후보 목록
    /// </summary>
    public ObservableCollection<MentionCandidate>? Candidates
    {
        get => (ObservableCollection<MentionCandidate>?)GetValue(CandidatesProperty);
        set => SetValue(CandidatesProperty, value);
    }

    public static readonly DependencyProperty CandidatesProperty =
        DependencyProperty.Register(nameof(Candidates), typeof(ObservableCollection<MentionCandidate>), typeof(MentionPopup),
            new PropertyMetadata(null, OnCandidatesChanged));

    private static void OnCandidatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MentionPopup popup)
        {
            popup.MentionListBox.ItemsSource = e.NewValue as ObservableCollection<MentionCandidate>;
            popup.UpdateNoResultsVisibility();
        }
    }

    /// <summary>
    /// 멘션 선택 이벤트
    /// </summary>
    public event EventHandler<MentionCandidate>? MentionSelected;

    public MentionPopup()
    {
        InitializeComponent();
    }

    private void MentionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MentionListBox.SelectedItem is MentionCandidate candidate)
        {
            MentionSelected?.Invoke(this, candidate);
            MentionListBox.SelectedItem = null;
        }
    }

    private void UpdateNoResultsVisibility()
    {
        NoResultsText.Visibility = (Candidates == null || Candidates.Count == 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
