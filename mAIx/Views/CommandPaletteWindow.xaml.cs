using System.Windows;
using System.Windows.Input;
using mAIx.Models;
using mAIx.Services;

namespace mAIx.Views;

/// <summary>
/// 커맨드 팔레트 창 (Ctrl+K)
/// </summary>
public partial class CommandPaletteWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly CommandPaletteService _service;

    public CommandPaletteWindow(CommandPaletteService service)
    {
        _service = service;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            RefreshList("");
        };
    }

    private void RefreshList(string query)
    {
        var results = _service.Search(query);
        ResultList.ItemsSource = results;

        HintText.Visibility = results.Count == 0 && string.IsNullOrWhiteSpace(query)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (results.Count > 0)
            ResultList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshList(SearchBox.Text);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (ResultList.Items.Count > 0)
                {
                    ResultList.SelectedIndex = Math.Min(
                        (ResultList.SelectedIndex < 0 ? -1 : ResultList.SelectedIndex) + 1,
                        ResultList.Items.Count - 1);
                    ResultList.ScrollIntoView(ResultList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (ResultList.Items.Count > 0)
                {
                    ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex - 1, 0);
                    ResultList.ScrollIntoView(ResultList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
    }

    private void ResultList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // 선택 변경 시 특별 처리 불필요
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ExecuteSelected()
    {
        if (ResultList.SelectedItem is CommandPaletteItem item)
        {
            Close();
            item.Execute?.Invoke();
        }
    }
}
