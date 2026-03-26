using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using mAIx.Models.Settings;
using mAIx.Utils;

namespace mAIx.Views.Dialogs;

/// <summary>
/// 서명 관리 다이얼로그 - RichTextBox 에디터 지원
/// </summary>
public partial class SignatureSettingsDialog : FluentWindow
{
    private readonly ObservableCollection<EmailSignature> _signatures = new();
    private EmailSignature? _selectedSignature;
    private bool _isLoading;

    /// <summary>
    /// 다이얼로그 결과 - 서명 설정
    /// </summary>
    public SignatureSettings? ResultSettings { get; private set; }

    /// <summary>
    /// 저장 여부
    /// </summary>
    public bool IsSaved { get; private set; }

    public SignatureSettingsDialog()
    {
        InitializeComponent();
        Log4.Debug("SignatureSettingsDialog 생성");

        SignatureListBox.ItemsSource = _signatures;

        // ESC 키로 창 닫기
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                IsSaved = false;
                DialogResult = false;
                Close();
            }
        };

        // 에디터에 포커스 줄 때 커서 위치 설정
        SignatureEditor.GotFocus += (s, e) =>
        {
            if (SignatureEditor.Document.Blocks.Count == 0)
            {
                SignatureEditor.Document.Blocks.Add(new Paragraph());
            }
        };
    }

    /// <summary>
    /// 현재 설정으로 다이얼로그 초기화
    /// </summary>
    public void LoadSettings(SignatureSettings? settings)
    {
        _isLoading = true;
        try
        {
            _signatures.Clear();

            if (settings != null)
            {
                foreach (var sig in settings.Signatures)
                {
                    _signatures.Add(new EmailSignature
                    {
                        Id = sig.Id,
                        Name = sig.Name,
                        HtmlContent = sig.HtmlContent,
                        PlainTextContent = sig.PlainTextContent,
                        CreatedAt = sig.CreatedAt,
                        ModifiedAt = sig.ModifiedAt
                    });
                }
            }

            // 기본 서명 없으면 샘플 추가
            if (_signatures.Count == 0)
            {
                _signatures.Add(new EmailSignature
                {
                    Name = "기본 서명",
                    PlainTextContent = "감사합니다.\n\n---\n홍길동\n회사명 | 부서명\n전화: 02-1234-5678\n이메일: hong@company.com"
                });
            }

            // 콤보박스 초기화
            UpdateComboBoxes(settings);

            // 첫 번째 서명 선택
            if (_signatures.Count > 0)
            {
                SignatureListBox.SelectedIndex = 0;
            }

            Log4.Debug($"서명 설정 로드 완료 - {_signatures.Count}개");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateComboBoxes(SignatureSettings? settings)
    {
        var items = new List<ComboBoxItem>
        {
            new ComboBoxItem { Content = "(서명 없음)", Tag = null }
        };

        foreach (var sig in _signatures)
        {
            items.Add(new ComboBoxItem { Content = sig.Name, Tag = sig.Id });
        }

        DefaultSignatureCombo.ItemsSource = items.ToList();
        ReplyForwardSignatureCombo.ItemsSource = items.ToList();

        // 현재 설정 선택
        if (settings != null)
        {
            SelectComboItem(DefaultSignatureCombo, settings.DefaultSignatureId);
            SelectComboItem(ReplyForwardSignatureCombo, settings.ReplyForwardSignatureId);
        }
        else
        {
            DefaultSignatureCombo.SelectedIndex = 0;
            ReplyForwardSignatureCombo.SelectedIndex = 0;
        }
    }

    private void SelectComboItem(ComboBox combo, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            combo.SelectedIndex = 0;
            return;
        }

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == id)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void SignatureListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        // 이전 서명 내용 저장
        SaveCurrentSignatureContent();

        _selectedSignature = SignatureListBox.SelectedItem as EmailSignature;

        if (_selectedSignature != null)
        {
            _isLoading = true;
            SignatureNameTextBox.Text = _selectedSignature.Name;

            // RichTextBox에 내용 로드
            LoadContentToEditor(_selectedSignature.PlainTextContent ?? string.Empty);

            _isLoading = false;
        }
        else
        {
            SignatureNameTextBox.Text = string.Empty;
            SignatureEditor?.Document?.Blocks?.Clear();
        }
    }

    private void LoadContentToEditor(string content)
    {
        if (SignatureEditor?.Document == null) return;

        SignatureEditor.Document.Blocks.Clear();

        if (string.IsNullOrEmpty(content))
        {
            SignatureEditor.Document.Blocks.Add(new Paragraph());
            return;
        }

        // 줄 단위로 분리하여 Paragraph 추가
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var para = new Paragraph(new Run(line));
            SignatureEditor.Document.Blocks.Add(para);
        }
    }

    private string GetEditorContent()
    {
        if (SignatureEditor?.Document == null) return string.Empty;

        var textRange = new TextRange(SignatureEditor.Document.ContentStart, SignatureEditor.Document.ContentEnd);
        return textRange.Text.TrimEnd();
    }

    private string GetEditorHtmlContent()
    {
        if (SignatureEditor?.Document == null) return string.Empty;

        // RichTextBox 내용을 간단한 HTML로 변환
        var lines = new List<string>();
        foreach (var block in SignatureEditor.Document.Blocks)
        {
            if (block is Paragraph para)
            {
                var lineHtml = new System.Text.StringBuilder();
                foreach (var inline in para.Inlines)
                {
                    var text = new TextRange(inline.ContentStart, inline.ContentEnd).Text;
                    var escaped = System.Web.HttpUtility.HtmlEncode(text);

                    // 서식 적용
                    if (inline.FontWeight == FontWeights.Bold)
                        escaped = $"<b>{escaped}</b>";
                    if (inline.FontStyle == FontStyles.Italic)
                        escaped = $"<i>{escaped}</i>";
                    if (inline is Run run && run.TextDecorations?.Contains(TextDecorations.Underline[0]) == true)
                        escaped = $"<u>{escaped}</u>";

                    if (inline is Hyperlink link)
                    {
                        var url = link.NavigateUri?.ToString() ?? "#";
                        escaped = $"<a href=\"{System.Web.HttpUtility.HtmlEncode(url)}\">{escaped}</a>";
                    }

                    lineHtml.Append(escaped);
                }

                // 정렬 처리
                var align = para.TextAlignment switch
                {
                    TextAlignment.Center => " style=\"text-align:center\"",
                    TextAlignment.Right => " style=\"text-align:right\"",
                    _ => ""
                };

                lines.Add($"<p{align}>{lineHtml}</p>");
            }
        }

        return string.Join("\n", lines);
    }

    private void SaveCurrentSignatureContent()
    {
        if (_selectedSignature != null)
        {
            _selectedSignature.PlainTextContent = GetEditorContent();
            _selectedSignature.HtmlContent = GetEditorHtmlContent();
            _selectedSignature.ModifiedAt = DateTime.Now;
        }
    }

    private void SignatureNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedSignature == null) return;

        _selectedSignature.Name = SignatureNameTextBox.Text;
        _selectedSignature.ModifiedAt = DateTime.Now;

        // ListBox 갱신
        SignatureListBox.Items.Refresh();
    }


    // ========== 서식 버튼 이벤트 핸들러 ==========

    private void FormatBold_Click(object sender, RoutedEventArgs e)
    {
        var selection = SignatureEditor.Selection;
        if (!selection.IsEmpty)
        {
            var currentWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
            var newWeight = (currentWeight != DependencyProperty.UnsetValue && (FontWeight)currentWeight == FontWeights.Bold)
                ? FontWeights.Normal
                : FontWeights.Bold;
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, newWeight);
        }
        SignatureEditor.Focus();
    }

    private void FormatItalic_Click(object sender, RoutedEventArgs e)
    {
        var selection = SignatureEditor.Selection;
        if (!selection.IsEmpty)
        {
            var currentStyle = selection.GetPropertyValue(TextElement.FontStyleProperty);
            var newStyle = (currentStyle != DependencyProperty.UnsetValue && (FontStyle)currentStyle == FontStyles.Italic)
                ? FontStyles.Normal
                : FontStyles.Italic;
            selection.ApplyPropertyValue(TextElement.FontStyleProperty, newStyle);
        }
        SignatureEditor.Focus();
    }

    private void FormatUnderline_Click(object sender, RoutedEventArgs e)
    {
        var selection = SignatureEditor.Selection;
        if (!selection.IsEmpty)
        {
            var currentDecoration = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            var hasUnderline = currentDecoration != DependencyProperty.UnsetValue
                && currentDecoration is TextDecorationCollection decorations
                && decorations.Contains(TextDecorations.Underline[0]);

            selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                hasUnderline ? null : TextDecorations.Underline);
        }
        SignatureEditor.Focus();
    }

    private void AlignLeft_Click(object sender, RoutedEventArgs e)
    {
        SetParagraphAlignment(TextAlignment.Left);
    }

    private void AlignCenter_Click(object sender, RoutedEventArgs e)
    {
        SetParagraphAlignment(TextAlignment.Center);
    }

    private void AlignRight_Click(object sender, RoutedEventArgs e)
    {
        SetParagraphAlignment(TextAlignment.Right);
    }

    private void SetParagraphAlignment(TextAlignment alignment)
    {
        var selection = SignatureEditor.Selection;
        if (selection.Start.Paragraph != null)
        {
            selection.Start.Paragraph.TextAlignment = alignment;
        }
        SignatureEditor.Focus();
    }

    private void InsertLink_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LinkInputDialog { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Url))
        {
            var selection = SignatureEditor.Selection;
            var displayText = string.IsNullOrWhiteSpace(dialog.DisplayText) ? dialog.Url : dialog.DisplayText;

            // 현재 선택 위치에 하이퍼링크 삽입
            var hyperlink = new Hyperlink(new Run(displayText))
            {
                NavigateUri = new Uri(dialog.Url),
                Foreground = Brushes.DodgerBlue
            };

            if (!selection.IsEmpty)
            {
                selection.Text = string.Empty;
            }

            var insertPosition = selection.Start;
            if (insertPosition.Paragraph != null)
            {
                insertPosition.Paragraph.Inlines.Add(hyperlink);
            }
        }
        SignatureEditor.Focus();
    }

    private void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.gif;*.bmp|모든 파일|*.*",
            Title = "이미지 선택"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(openFileDialog.FileName);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    MaxWidth = 400,
                    Stretch = Stretch.Uniform
                };

                var container = new InlineUIContainer(image);
                var selection = SignatureEditor.Selection;

                if (selection.Start.Paragraph != null)
                {
                    selection.Start.Paragraph.Inlines.Add(container);
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"이미지 삽입 오류: {ex.Message}");
                System.Windows.MessageBox.Show("이미지를 삽입할 수 없습니다.", "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        SignatureEditor.Focus();
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        // InitializeComponent() 중에 IsSelected="True"로 인해 호출될 수 있음
        if (_isLoading || SignatureEditor == null) return;

        if (FontSizeCombo.SelectedItem is ComboBoxItem item && item.Content is string sizeStr)
        {
            // "12pt" 형식에서 숫자만 추출
            var sizeText = sizeStr.Replace("pt", "");
            if (double.TryParse(sizeText, out var size))
            {
                var selection = SignatureEditor.Selection;
                if (!selection.IsEmpty)
                {
                    selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                }
            }
        }
        SignatureEditor.Focus();
    }

    // ========== 서명 관리 버튼 ==========

    private void AddSignature_Click(object sender, RoutedEventArgs e)
    {
        var newSig = new EmailSignature
        {
            Name = $"새 서명 {_signatures.Count + 1}",
            PlainTextContent = "서명 내용을 입력하세요."
        };

        _signatures.Add(newSig);
        SignatureListBox.SelectedItem = newSig;

        Log4.Info($"새 서명 추가: {newSig.Name}");
    }

    private void DeleteSignature_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSignature == null)
        {
            System.Windows.MessageBox.Show("삭제할 서명을 선택해주세요.", "알림", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"'{_selectedSignature.Name}' 서명을 삭제하시겠습니까?",
            "서명 삭제",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            var index = _signatures.IndexOf(_selectedSignature);
            Log4.Info($"서명 삭제: {_selectedSignature.Name}");
            _signatures.Remove(_selectedSignature);

            // 다음 항목 선택
            if (_signatures.Count > 0)
            {
                SignatureListBox.SelectedIndex = Math.Min(index, _signatures.Count - 1);
            }
        }
    }

    private void DefaultSignatureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 저장 시 처리
    }

    private void ReplyForwardSignatureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 저장 시 처리
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsSaved = false;
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 현재 에디터 내용을 선택된 서명에 저장
        SaveCurrentSignatureContent();

        // 결과 설정 생성
        ResultSettings = new SignatureSettings
        {
            Signatures = _signatures.ToList(),
            AutoAddToNewMail = true,
            AutoAddToReplyForward = true
        };

        // 기본 서명 ID
        if (DefaultSignatureCombo.SelectedItem is ComboBoxItem defaultItem)
        {
            ResultSettings.DefaultSignatureId = defaultItem.Tag?.ToString();
        }

        // 답장/전달 기본 서명 ID
        if (ReplyForwardSignatureCombo.SelectedItem is ComboBoxItem replyItem)
        {
            ResultSettings.ReplyForwardSignatureId = replyItem.Tag?.ToString();
        }

        Log4.Info($"서명 설정 저장: {ResultSettings.Signatures.Count}개, 기본: {ResultSettings.DefaultSignatureId}");

        IsSaved = true;
        DialogResult = true;
        Close();
    }
}
