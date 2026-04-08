using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;
using mAIx.Utils;

namespace mAIx.Services;

/// <summary>
/// 키보드 단축키 관리 서비스 — Window 레벨 PreviewKeyDown에서 호출
/// </summary>
public class KeyboardShortcutService
{
    /// <summary>단축키 정의</summary>
    public class ShortcutEntry
    {
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;
        public string 설명 { get; set; } = "";
        public string 카테고리 { get; set; } = "일반";
        public Action? 실행 { get; set; }
        public bool 활성화 { get; set; } = true;
    }

    private readonly Dictionary<(Key, ModifierKeys), ShortcutEntry> _단축키맵 = new();
    private readonly string _설정파일경로;
    private bool _활성화 = true;

    /// <summary>등록된 단축키 목록 (도움말 표시용)</summary>
    public IReadOnlyCollection<ShortcutEntry> 등록단축키 => _단축키맵.Values.ToList().AsReadOnly();

    /// <summary>단축키 시스템 전체 활성화/비활성화</summary>
    public bool 활성화
    {
        get => _활성화;
        set => _활성화 = value;
    }

    public KeyboardShortcutService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MaiX");
        Directory.CreateDirectory(appDataDir);
        _설정파일경로 = Path.Combine(appDataDir, "shortcuts.json");
    }

    /// <summary>단축키 등록</summary>
    public void 등록(Key key, string 설명, Action 실행, string 카테고리 = "메일", ModifierKeys modifiers = ModifierKeys.None)
    {
        var entry = new ShortcutEntry
        {
            Key = key,
            Modifiers = modifiers,
            설명 = 설명,
            카테고리 = 카테고리,
            실행 = 실행,
            활성화 = true
        };
        _단축키맵[(key, modifiers)] = entry;
    }

    /// <summary>단축키 해제</summary>
    public void 해제(Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        _단축키맵.Remove((key, modifiers));
    }

    /// <summary>
    /// PreviewKeyDown에서 호출 — 텍스트 입력 중이면 무시, 등록된 단축키면 실행
    /// </summary>
    /// <returns>처리 여부 (true면 e.Handled = true)</returns>
    public bool 처리(KeyEventArgs e)
    {
        if (!_활성화) return false;

        // 텍스트 입력 컨트롤에 포커스 시 단축키 비활성화
        if (텍스트입력중(e.OriginalSource as DependencyObject))
            return false;

        // Modifier 키 단독 입력은 무시
        if (e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LWin || e.Key == Key.RWin)
            return false;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // Ctrl/Alt/Win 조합은 기본 WPF 바인딩에 위임 (단축키 서비스는 단일 키만 처리)
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            return false;

        // ? 키: OemQuestion (Shift 포함)
        if (key == Key.OemQuestion)
        {
            modifiers &= ~ModifierKeys.Shift; // Shift는 ?를 위한 것이므로 제거
        }

        if (_단축키맵.TryGetValue((key, modifiers), out var entry) && entry.활성화 && entry.실행 != null)
        {
            try
            {
                entry.실행();
                return true;
            }
            catch (Exception ex)
            {
                Log4.Error($"단축키 실행 실패 [{entry.설명}]: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>텍스트 입력 컨트롤 여부 확인</summary>
    private static bool 텍스트입력중(DependencyObject? source)
    {
        if (source == null) return false;

        return source is TextBox ||
               source is RichTextBox ||
               source is PasswordBox ||
               (source is FrameworkElement fe && fe.GetType().Name.Contains("WebView"));
    }

    /// <summary>설정 파일에서 커스텀 단축키 로드 (향후 확장용)</summary>
    public void 설정로드()
    {
        try
        {
            if (!File.Exists(_설정파일경로)) return;

            var json = File.ReadAllText(_설정파일경로);
            var 커스텀설정 = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (커스텀설정 == null) return;

            foreach (var kvp in _단축키맵)
            {
                var keyName = $"{kvp.Key.Item1}";
                if (커스텀설정.TryGetValue(keyName, out var 활성))
                {
                    kvp.Value.활성화 = 활성;
                }
            }
            Log4.Info($"단축키 설정 로드 완료: {_설정파일경로}");
        }
        catch (Exception ex)
        {
            Log4.Warn($"단축키 설정 로드 실패: {ex.Message}");
        }
    }

    /// <summary>현재 단축키 활성화 상태를 JSON으로 저장</summary>
    public void 설정저장()
    {
        try
        {
            var 설정 = _단축키맵.ToDictionary(
                kvp => $"{kvp.Key.Item1}",
                kvp => kvp.Value.활성화);

            var json = JsonSerializer.Serialize(설정, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_설정파일경로, json);
            Log4.Info($"단축키 설정 저장 완료: {_설정파일경로}");
        }
        catch (Exception ex)
        {
            Log4.Warn($"단축키 설정 저장 실패: {ex.Message}");
        }
    }
}
