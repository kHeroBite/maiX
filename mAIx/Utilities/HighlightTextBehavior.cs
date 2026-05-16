// STT 화면 TextBlock에 주제어 키워드 형광펜 하이라이트를 적용하는 첨부속성 헬퍼
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace mAIx.Utilities;

/// <summary>
/// TextBlock에 첨부 — Text + Keywords + IsEnabled 바인딩으로 키워드 매칭 위치만 노란 배경으로 표시.
/// </summary>
public static class HighlightTextBehavior
{
    // 라이트 모드: 밝은 노란 형광펜 (진한 글자 위 가독성 최상)
    private static readonly Brush HighlightBrushLight = new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0x9D));
    private static readonly Brush HighlightForegroundLight = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));

    // 다크 모드: 채도 낮춘 진한 황금색 + 어두운 글자 (눈부심 방지 + 대비 확보)
    private static readonly Brush HighlightBrushDark = new SolidColorBrush(Color.FromRgb(0xC9, 0x9A, 0x2B));
    private static readonly Brush HighlightForegroundDark = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

    private static bool IsDarkTheme()
    {
        try
        {
            var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            return theme == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        }
        catch
        {
            return false;
        }
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(HighlightTextBehavior),
        new PropertyMetadata(string.Empty, OnAnyChanged));

    public static readonly DependencyProperty KeywordsProperty = DependencyProperty.RegisterAttached(
        "Keywords", typeof(IEnumerable<string>), typeof(HighlightTextBehavior),
        new PropertyMetadata(null, OnAnyChanged));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(HighlightTextBehavior),
        new PropertyMetadata(true, OnAnyChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string v) => d.SetValue(TextProperty, v);

    public static IEnumerable<string>? GetKeywords(DependencyObject d) => (IEnumerable<string>?)d.GetValue(KeywordsProperty);
    public static void SetKeywords(DependencyObject d, IEnumerable<string>? v) => d.SetValue(KeywordsProperty, v);

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        ApplyHighlight(tb);
    }

    private static void ApplyHighlight(TextBlock tb)
    {
        var text = GetText(tb) ?? string.Empty;
        var keywords = GetKeywords(tb);
        var enabled = GetIsEnabled(tb);

        tb.Inlines.Clear();

        if (string.IsNullOrEmpty(text))
            return;

        // 비활성 또는 키워드 없음 → 일반 텍스트만
        var kwList = keywords?.Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 2).Distinct().ToList();
        if (!enabled || kwList == null || kwList.Count == 0)
        {
            tb.Inlines.Add(new Run(text));
            return;
        }

        // 키워드 길이 내림차순 (긴 단어 우선 매칭으로 부분일치 충돌 회피)
        var ordered = kwList.OrderByDescending(k => k.Length).ToList();

        // 각 위치별 가장 긴 매칭 키워드 결정
        var matches = new List<(int start, int length)>();
        for (int i = 0; i < text.Length; )
        {
            var matched = ordered.FirstOrDefault(k =>
                i + k.Length <= text.Length &&
                text.IndexOf(k, i, k.Length, StringComparison.OrdinalIgnoreCase) == i &&
                IsWordBoundary(text, i, k.Length));
            if (matched != null)
            {
                matches.Add((i, matched.Length));
                i += matched.Length;
            }
            else
            {
                i++;
            }
        }

        if (matches.Count == 0)
        {
            tb.Inlines.Add(new Run(text));
            return;
        }

        var isDark = IsDarkTheme();
        var bg = isDark ? HighlightBrushDark : HighlightBrushLight;
        var fg = isDark ? HighlightForegroundDark : HighlightForegroundLight;

        int cursor = 0;
        foreach (var (start, length) in matches)
        {
            if (start > cursor)
                tb.Inlines.Add(new Run(text.Substring(cursor, start - cursor)));

            var keywordRun = new Run(text.Substring(start, length))
            {
                Background = bg,
                Foreground = fg,
                FontWeight = FontWeights.SemiBold
            };
            tb.Inlines.Add(keywordRun);
            cursor = start + length;
        }
        if (cursor < text.Length)
            tb.Inlines.Add(new Run(text.Substring(cursor)));
    }

    /// <summary>
    /// text[start..start+len] 구간이 단어 경계에서 시작/끝나는지 검사.
    /// 앞뒤 문자가 키워드와 동일한 문자 종류(한글↔한글, 영숫자↔영숫자)이면
    /// 더 긴 단어의 일부로 보고 false 반환 (부분 매칭 차단).
    /// </summary>
    private static bool IsWordBoundary(string text, int start, int len)
    {
        // 앞 경계 검사
        if (start > 0)
        {
            var prev = text[start - 1];
            var first = text[start];
            if (IsSameCharClass(prev, first)) return false;
        }
        // 뒤 경계 검사
        var end = start + len;
        if (end < text.Length)
        {
            var last = text[end - 1];
            var next = text[end];
            if (IsSameCharClass(last, next)) return false;
        }
        return true;
    }

    /// <summary>
    /// 두 문자가 같은 단어 구성 종류인지 판단.
    /// 한글 음절끼리 또는 영숫자끼리이면 true (단어 내부 연속으로 간주).
    /// </summary>
    private static bool IsSameCharClass(char a, char b)
    {
        static bool IsKorean(char c) => c >= '\uAC00' && c <= '\uD7A3';
        static bool IsAlphaNum(char c) => char.IsLetterOrDigit(c) && c < 128;
        return (IsKorean(a) && IsKorean(b)) || (IsAlphaNum(a) && IsAlphaNum(b));
    }
}
