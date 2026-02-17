using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MaiX.Models;
using MaiX.Services.Storage;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace MaiX.Views;

public partial class PromptSettingsWindow : FluentWindow
{
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
        await LoadCategoriesAsync();
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
        if (CategoryListBox.SelectedItem is not string category) return;

        using var scope = _serviceProvider.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
        var prompts = await promptService.GetPromptsByCategoryAsync(category);

        _prompts = new ObservableCollection<Prompt>(prompts);
        PromptListBox.ItemsSource = _prompts;
        if (_prompts.Count > 0)
            PromptListBox.SelectedIndex = 0;
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
        if (_selectedPrompt == null) return;

        _selectedPrompt.Template = TemplateTextBox.Text;
        _selectedPrompt.IsEnabled = EnabledToggle.IsChecked == true;

        using var scope = _serviceProvider.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
        await promptService.SavePromptAsync(_selectedPrompt);

        System.Windows.MessageBox.Show("저장되었습니다.", "AI 프롬프트", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
