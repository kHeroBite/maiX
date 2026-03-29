using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using mAIx.Models;
using mAIx.Services.Rules;
using mAIx.Utils;
using Wpf.Ui.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace mAIx.Views.Dialogs;

/// <summary>
/// 메일 규칙 표시용 뷰모델 (ListView DisplayMemberBinding 지원)
/// </summary>
public class MailRuleViewModel
{
    public MailRule Rule { get; }

    public int Id => Rule.Id;
    public string Name => Rule.Name;
    public string? ConditionValue => Rule.ConditionValue;
    public string? ActionValue => Rule.ActionValue;
    public int Priority => Rule.Priority;
    public bool IsEnabled => Rule.IsEnabled;

    public string ConditionTypeDisplay => Rule.ConditionType switch
    {
        "FromContains" => "발신자 포함",
        "SubjectContains" => "제목 포함",
        "HasAttachment" => "첨부파일 있음",
        "AiCategoryEquals" => "AI 카테고리",
        "ToContains" => "수신자 포함",
        _ => Rule.ConditionType
    };

    public string ActionTypeDisplay => Rule.ActionType switch
    {
        "MoveToFolder" => "폴더 이동",
        "SetCategory" => "카테고리 설정",
        "SetFlag" => "플래그",
        "MarkAsRead" => "읽음 처리",
        "Delete" => "삭제",
        _ => Rule.ActionType
    };

    public MailRuleViewModel(MailRule rule)
    {
        Rule = rule;
    }
}

/// <summary>
/// 메일 규칙 관리 다이얼로그
/// </summary>
public partial class MailRuleSettingsDialog : FluentWindow
{
    private readonly MailRuleService _mailRuleService;
    private readonly ObservableCollection<MailRuleViewModel> _rules = new();
    private MailRuleViewModel? _selectedRule;
    private bool _isNewRule;

    public MailRuleSettingsDialog()
    {
        InitializeComponent();
        Log4.Debug("MailRuleSettingsDialog 생성");

        _mailRuleService = ((App)Application.Current).GetService<MailRuleService>()
            ?? throw new InvalidOperationException("MailRuleService를 찾을 수 없습니다.");

        RulesListView.ItemsSource = _rules;

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        Loaded += async (s, e) => await LoadRulesAsync();
    }

    /// <summary>
    /// 규칙 목록 로드
    /// </summary>
    private async Task LoadRulesAsync()
    {
        try
        {
            _rules.Clear();
            var rules = await _mailRuleService.GetRulesAsync(string.Empty);
            foreach (var rule in rules)
                _rules.Add(new MailRuleViewModel(rule));

            Log4.Debug($"[MailRuleSettingsDialog] 규칙 {_rules.Count}개 로드 완료");
            UpdateStatus($"규칙 {_rules.Count}개 로드됨");
        }
        catch (Exception ex)
        {
            Log4.Error($"[MailRuleSettingsDialog] 규칙 로드 실패: {ex.Message}");
            UpdateStatus("규칙 목록을 불러오지 못했습니다.");
        }
    }

    private void RulesListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedRule = RulesListView.SelectedItem as MailRuleViewModel;
        if (_selectedRule == null)
        {
            ClearForm();
            return;
        }

        _isNewRule = false;
        FillForm(_selectedRule.Rule);
        UpdateStatus($"'{_selectedRule.Name}' 편집 중");
    }

    private void FillForm(MailRule rule)
    {
        NameTextBox.Text = rule.Name;
        ConditionValueTextBox.Text = rule.ConditionValue ?? string.Empty;
        ActionValueTextBox.Text = rule.ActionValue ?? string.Empty;
        PriorityTextBox.Text = rule.Priority.ToString();
        IsEnabledCheckBox.IsChecked = rule.IsEnabled;

        SelectComboByTag(ConditionTypeComboBox, rule.ConditionType);
        SelectComboByTag(ActionTypeComboBox, rule.ActionType);
    }

    private void ClearForm()
    {
        NameTextBox.Text = string.Empty;
        ConditionValueTextBox.Text = string.Empty;
        ActionValueTextBox.Text = string.Empty;
        PriorityTextBox.Text = "0";
        IsEnabledCheckBox.IsChecked = true;
        ConditionTypeComboBox.SelectedIndex = 0;
        ActionTypeComboBox.SelectedIndex = 0;
    }

    private void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                item.Tag?.ToString() == tag)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private string GetSelectedTag(System.Windows.Controls.ComboBox combo)
    {
        if (combo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            return item.Tag?.ToString() ?? string.Empty;
        return string.Empty;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        _isNewRule = true;
        _selectedRule = null;
        RulesListView.SelectedItem = null;
        ClearForm();
        NameTextBox.Focus();
        UpdateStatus("새 규칙을 입력하세요.");
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRule == null)
        {
            WpfMessageBox.Show("삭제할 규칙을 선택해주세요.", "알림",
                WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            return;
        }

        var result = WpfMessageBox.Show(
            $"'{_selectedRule.Name}' 규칙을 삭제하시겠습니까?",
            "규칙 삭제",
            WpfMessageBoxButton.YesNo,
            WpfMessageBoxImage.Question);

        if (result != WpfMessageBoxResult.Yes) return;

        try
        {
            await _mailRuleService.DeleteRuleAsync(_selectedRule.Id);
            Log4.Info($"[MailRuleSettingsDialog] 규칙 삭제: {_selectedRule.Name}");
            await LoadRulesAsync();
            ClearForm();
        }
        catch (Exception ex)
        {
            Log4.Error($"[MailRuleSettingsDialog] 규칙 삭제 실패: {ex.Message}");
            WpfMessageBox.Show($"규칙 삭제에 실패했습니다.\n{ex.Message}", "오류",
                WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }

    private async void SaveRuleButton_Click(object sender, RoutedEventArgs e)
    {
        // 유효성 검증
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            WpfMessageBox.Show("규칙 이름을 입력해주세요.", "유효성 오류",
                WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            NameTextBox.Focus();
            return;
        }

        var conditionType = GetSelectedTag(ConditionTypeComboBox);
        var actionType = GetSelectedTag(ActionTypeComboBox);

        if (string.IsNullOrEmpty(conditionType) || string.IsNullOrEmpty(actionType))
        {
            WpfMessageBox.Show("조건 타입과 액션 타입을 선택해주세요.", "유효성 오류",
                WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PriorityTextBox.Text, out int priority))
            priority = 0;

        var rule = _isNewRule
            ? new MailRule()
            : _selectedRule!.Rule;

        rule.Name = NameTextBox.Text.Trim();
        rule.ConditionType = conditionType;
        rule.ConditionValue = string.IsNullOrWhiteSpace(ConditionValueTextBox.Text)
            ? null : ConditionValueTextBox.Text.Trim();
        rule.ActionType = actionType;
        rule.ActionValue = string.IsNullOrWhiteSpace(ActionValueTextBox.Text)
            ? null : ActionValueTextBox.Text.Trim();
        rule.Priority = priority;
        rule.IsEnabled = IsEnabledCheckBox.IsChecked == true;

        try
        {
            await _mailRuleService.SaveRuleAsync(rule);
            Log4.Info($"[MailRuleSettingsDialog] 규칙 저장: {rule.Name} (Id={rule.Id})");
            await LoadRulesAsync();
            UpdateStatus($"'{rule.Name}' 저장 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"[MailRuleSettingsDialog] 규칙 저장 실패: {ex.Message}");
            WpfMessageBox.Show($"규칙 저장에 실패했습니다.\n{ex.Message}", "오류",
                WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateStatus(string message)
    {
        if (StatusText != null)
            StatusText.Text = message;
    }
}
