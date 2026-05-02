using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using mAIx.Models;
using mAIx.Services.AI;
using mAIx.Services.Storage;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Wpf.Ui.Controls;

namespace mAIx.Views;

public partial class PromptSettingsWindow : FluentWindow
{
    private static readonly Logger Log4 = LogManager.GetCurrentClassLogger();
    private readonly IServiceProvider _serviceProvider;
    private ObservableCollection<string> _categories = new();
    private ObservableCollection<Prompt> _prompts = new();
    private Prompt? _selectedPrompt;

    public PromptSettingsWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadCategoriesAsync();
        }
        catch (Exception ex)
        {
            Log4.Error(ex, "[PromptSettings] OnLoaded 핸들러 실패");
        }
    }

    private async Task LoadCategoriesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
        var allPrompts = await promptService.GetAllPromptsAsync();

        _categories.Clear();
        var cats = allPrompts.Select(p => p.Category).Distinct().OrderBy(c => c).ToList();
        foreach (var cat in cats)
        {
            if (cat != null) _categories.Add(cat);
        }

        CategoryListBox.ItemsSource = _categories;
        if (_categories.Count > 0)
            CategoryListBox.SelectedIndex = 0;
    }

    private async void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (CategoryListBox.SelectedItem is not string category) return;

            using var scope = _serviceProvider.CreateScope();
            var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
            var prompts = await promptService.GetPromptsByCategoryAsync(category);

            _prompts = new ObservableCollection<Prompt>(prompts);
            PromptListBox.ItemsSource = _prompts;
            if (_prompts.Count > 0)
                PromptListBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PromptSettingsWindow] CategoryListBox_SelectionChanged 핸들러 실패: {ex}");
        }
    }

    private void PromptListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PromptListBox.SelectedItem is not Prompt prompt) return;
        _selectedPrompt = prompt;

        PromptNameText.Text = prompt.Name;
        PromptKeyText.Text = prompt.PromptKey;
        TemplateTextBox.Text = prompt.Template;
        VariablesText.Text = prompt.Variables ?? "없음";
        EnabledToggle.IsChecked = prompt.IsEnabled;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selectedPrompt == null) return;

            _selectedPrompt.Template = TemplateTextBox.Text;
            _selectedPrompt.IsEnabled = EnabledToggle.IsChecked == true;

            using var scope = _serviceProvider.CreateScope();
            var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
            await promptService.SavePromptAsync(_selectedPrompt);

            System.Windows.MessageBox.Show("저장되었습니다.", "AI 프롬프트", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log4.Error($"[PromptSettingsWindow] SaveButton_Click 핸들러 실패: {ex}");
        }
    }

    private void RestoreDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPrompt == null) return;

        var defaultPrompt = DefaultPromptTemplates.GetDefaultByKey(_selectedPrompt.PromptKey);
        if (defaultPrompt != null)
        {
            TemplateTextBox.Text = defaultPrompt.Template;
        }
        else
        {
            System.Windows.MessageBox.Show("기본값을 찾을 수 없습니다.", "AI 프롬프트", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private async void ReloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var 캐시서비스 = _serviceProvider.GetRequiredService<PromptCacheService>();
            var 개수 = await 캐시서비스.ReloadAllAsync();
            System.Windows.MessageBox.Show(
                $"프롬프트 {개수}개가 리로드되었습니다.",
                "프롬프트 리로드",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log4.Error($"[PromptSettingsWindow] ReloadAllButton_Click 핸들러 실패: {ex}");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
