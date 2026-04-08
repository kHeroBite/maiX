using System.Windows;
using System.Windows.Controls;
using mAIx.ViewModels;

namespace mAIx.Controls;

/// <summary>
/// 연락처 목록 컨트롤 코드비하인드
/// </summary>
public partial class ContactListControl : UserControl
{
    public ContactListControl()
    {
        InitializeComponent();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ContactsViewModel vm)
        {
            vm.SearchCommand.Execute(null);
        }
    }

    private void GroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is string group
            && DataContext is ContactsViewModel vm)
        {
            vm.SelectGroupCommand.Execute(group);
        }
    }

    private void ContactListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is ContactsViewModel vm)
        {
            vm.SelectContactCommand.Execute(listBox.SelectedItem as ContactItemModel);
        }
    }
}
