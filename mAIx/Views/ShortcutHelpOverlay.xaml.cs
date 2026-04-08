using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.Services;

namespace mAIx.Views;

/// <summary>
/// 단축키 도움말 오버레이 — ? 키로 표시, 아무 키/클릭으로 닫힘
/// </summary>
public partial class ShortcutHelpOverlay : UserControl
{
    /// <summary>도움말 표시용 데이터 클래스</summary>
    public class 단축키항목
    {
        public string 키표시 { get; set; } = "";
        public string 설명 { get; set; } = "";
    }

    public ShortcutHelpOverlay()
    {
        InitializeComponent();
    }

    /// <summary>단축키 목록을 받아 표시하고 오버레이를 보여줌</summary>
    public void 표시(IEnumerable<KeyboardShortcutService.ShortcutEntry> 단축키들)
    {
        var 항목들 = 단축키들
            .Where(s => s.활성화)
            .Select(s => new 단축키항목
            {
                키표시 = 키이름변환(s.Key, s.Modifiers),
                설명 = s.설명
            })
            .ToList();

        ShortcutList.ItemsSource = 항목들;
        Visibility = Visibility.Visible;
        Focusable = true;
        Focus();
    }

    /// <summary>오버레이 숨기기</summary>
    public void 숨기기()
    {
        Visibility = Visibility.Collapsed;
    }

    public bool 표시중 => Visibility == Visibility.Visible;

    private void Overlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        숨기기();
        e.Handled = true;
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        숨기기();
        e.Handled = true;
    }

    private static string 키이름변환(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");

        var keyName = key switch
        {
            Key.OemQuestion => "?",
            Key.J => "J",
            Key.K => "K",
            Key.E => "E",
            Key.R => "R",
            Key.A => "A",
            Key.F => "F",
            Key.D => "D",
            Key.U => "U",
            Key.S => "S",
            _ => key.ToString()
        };
        parts.Add(keyName);
        return string.Join("+", parts);
    }
}
