using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.ViewModels;

namespace mAIx.Controls;

/// <summary>
/// 연락처 상세 컨트롤 코드비하인드
/// </summary>
public partial class ContactDetailControl : UserControl
{
    public ContactDetailControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 연락처 데이터 표시/숨김
    /// </summary>
    public void ShowContact(ContactItemModel? contact)
    {
        if (contact == null)
        {
            EmptyState.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            DataContext = null;
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            DataContext = contact;
        }
    }

    /// <summary>
    /// 이메일 보내기 요청 이벤트
    /// </summary>
    public event EventHandler<string>? SendEmailRequested;

    /// <summary>
    /// 연락처 삭제 요청 이벤트
    /// </summary>
    public event EventHandler<ContactItemModel>? DeleteRequested;

    private void SendEmailButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ContactItemModel contact && !string.IsNullOrEmpty(contact.Email))
        {
            SendEmailRequested?.Invoke(this, contact.Email);
        }
    }

    private void CallButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ContactItemModel contact && !string.IsNullOrEmpty(contact.Phone))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"tel:{contact.Phone}",
                    UseShellExecute = true
                });
            }
            catch
            {
                // tel: URI 미지원 시 무시
            }
        }
    }

    private void CalendarButton_Click(object sender, RoutedEventArgs e)
    {
        // 일정 생성 — MainWindow에서 처리
    }

    private void Email_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ContactItemModel contact && !string.IsNullOrEmpty(contact.Email))
        {
            SendEmailRequested?.Invoke(this, contact.Email);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ContactItemModel contact)
        {
            DeleteRequested?.Invoke(this, contact);
        }
    }
}
