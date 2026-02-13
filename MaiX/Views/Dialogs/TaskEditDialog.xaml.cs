using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MaiX.Utils;
using MaiX.ViewModels;
using MaiX.Services.Graph;
using Wpf.Ui.Controls;

namespace MaiX.Views.Dialogs;

/// <summary>
/// Planner 작업 상세 편집 다이얼로그
/// </summary>
public partial class TaskEditDialog : FluentWindow
{
    private readonly GraphPlannerService _plannerService;
    private readonly TaskItemViewModel _task;
    private readonly ObservableCollection<BucketViewModel> _buckets;

    private ObservableCollection<ChecklistItemViewModel> _checklistItems = new();
    private ObservableCollection<AttachmentViewModel> _attachments = new();
    private ObservableCollection<CommentViewModel> _comments = new();

    private bool _isInitialized = false;
    private bool _hasChanges = false;
    private string? _originalBucketId;

    /// <summary>
    /// 작업이 변경되었는지 여부
    /// </summary>
    public bool HasChanges => _hasChanges;

    /// <summary>
    /// 작업이 삭제되었는지 여부
    /// </summary>
    public bool IsDeleted { get; private set; } = false;

    /// <summary>
    /// 선택된 버킷 ID
    /// </summary>
    public string? SelectedBucketId => (BucketComboBox?.SelectedItem as BucketViewModel)?.Id;

    public TaskEditDialog(
        TaskItemViewModel task,
        ObservableCollection<BucketViewModel> buckets,
        GraphPlannerService plannerService)
    {
        _task = task ?? throw new ArgumentNullException(nameof(task));
        _buckets = buckets ?? throw new ArgumentNullException(nameof(buckets));
        _plannerService = plannerService ?? throw new ArgumentNullException(nameof(plannerService));
        _originalBucketId = task.BucketId;

        InitializeComponent();
        LoadTaskData();
    }

    /// <summary>
    /// 작업 데이터 로드
    /// </summary>
    private void LoadTaskData()
    {
        // 제목
        TitleTextBox.Text = _task.Title;

        // 버킷 콤보박스
        BucketComboBox.ItemsSource = _buckets;
        var currentBucket = _buckets.FirstOrDefault(b => b.Id == _task.BucketId);
        if (currentBucket != null)
        {
            BucketComboBox.SelectedItem = currentBucket;
        }

        // 진행률
        var progressIndex = _task.PercentComplete switch
        {
            0 => 0,
            100 => 2,
            _ => 1
        };
        ProgressComboBox.SelectedIndex = progressIndex;

        // 우선 순위
        var priorityIndex = _task.Priority switch
        {
            1 => 0,  // 긴급
            3 => 1,  // 중요
            5 => 2,  // 중간
            9 => 3,  // 낮음
            _ => 2   // 기본: 중간
        };
        PriorityComboBox.SelectedIndex = priorityIndex;

        // 날짜
        StartDatePicker.SelectedDate = _task.StartDateTime;
        DueDatePicker.SelectedDate = _task.DueDateTime;

        // 완료 버튼 상태
        UpdateCompleteButtonState();

        // 체크리스트, 첨부, 댓글은 비동기로 로드
        ChecklistItemsControl.ItemsSource = _checklistItems;
        AttachmentsItemsControl.ItemsSource = _attachments;
        CommentsItemsControl.ItemsSource = _comments;

        // _isInitialized는 Window_Loaded 완료 후 설정
    }

    /// <summary>
    /// 윈도우 로드됨
    /// </summary>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // TinyMCE 초기화
        await InitializeTinyMCEAsync();

        // 상세 데이터 로드 (체크리스트, 첨부, 댓글)
        await LoadTaskDetailsAsync();

        UpdateStatus($"마지막 수정: {_task.CreatedDateTime:yyyy-MM-dd HH:mm}");

        // 모든 초기화 완료 후 변경 추적 활성화
        _isInitialized = true;
        _hasChanges = false;
    }

    /// <summary>
    /// TinyMCE 에디터 초기화 (로컬 self-hosted 방식 사용)
    /// </summary>
    private async Task InitializeTinyMCEAsync()
    {
        try
        {
            await NotesWebView.EnsureCoreWebView2Async();

            // 로컬 TinyMCE 폴더 경로 설정 (Self-hosted)
            // CDN은 WebView2 NavigateToString에서 referer 헤더가 없어 도메인 확인 불가
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var tinymcePath = System.IO.Path.Combine(appDir, "Assets", "tinymce");

            // WebView2에서 로컬 파일에 접근할 수 있도록 가상 호스트 매핑
            NotesWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "tinymce.local", tinymcePath,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

            // TinyMCE에서 콘텐츠 변경 시 알림 받기
            NotesWebView.CoreWebView2.WebMessageReceived += (s, args) =>
            {
                try
                {
                    var message = System.Text.Json.JsonDocument.Parse(args.WebMessageAsJson);
                    if (message.RootElement.TryGetProperty("type", out var typeElement))
                    {
                        var type = typeElement.GetString();
                        if (type == "content-changed" && _isInitialized)
                        {
                            Dispatcher.Invoke(() => MarkAsChanged());
                        }
                    }
                }
                catch { }
            };

            // TinyMCE HTML
            var html = GetTinyMCEHtml();
            NotesWebView.NavigateToString(html);

            // 메모 내용 설정은 WebView2 로드 완료 후
            NotesWebView.NavigationCompleted += async (s, args) =>
            {
                if (args.IsSuccess && !string.IsNullOrEmpty(_task.Notes))
                {
                    // 마크다운 테이블을 HTML 테이블로 변환
                    var htmlContent = ConvertMarkdownTableToHtml(_task.Notes);
                    var escapedNotes = htmlContent.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
                    await NotesWebView.CoreWebView2.ExecuteScriptAsync(
                        $"if (typeof tinymce !== 'undefined' && tinymce.activeEditor) tinymce.activeEditor.setContent('{escapedNotes}');");
                }
            };
        }
        catch (Exception ex)
        {
            Log4.Error($"[TaskEditDialog] TinyMCE 초기화 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// TinyMCE HTML 템플릿 (로컬 self-hosted)
    /// </summary>
    private string GetTinyMCEHtml()
    {
        var backgroundColor = "#1e1e1e";
        var textColor = "#ffffff";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <script src='https://tinymce.local/tinymce.min.js'></script>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{
            height: 100%;
            background-color: {backgroundColor};
        }}
        .tox-tinymce {{ border: none !important; }}
    </style>
</head>
<body>
    <textarea id='editor'></textarea>
    <script>
        let editor;

        tinymce.init({{
            selector: '#editor',
            height: '100%',
            width: '100%',
            menubar: false,
            statusbar: false,
            base_url: 'https://tinymce.local',
            suffix: '.min',
            plugins: 'lists link table',
            toolbar: 'bold italic underline | bullist numlist | table | link',
            skin: 'oxide-dark',
            skin_url: 'https://tinymce.local/skins/ui/oxide-dark',
            content_css: 'dark',
            content_style: 'body {{ font-family: Segoe UI, sans-serif; font-size: 14px; color: {textColor}; background-color: {backgroundColor}; padding: 8px; }} table {{ border-collapse: collapse; width: 100%; }} th, td {{ border: 1px solid #555; padding: 8px; }}',
            branding: false,
            browser_spellcheck: true,
            contextmenu: false,
            table_toolbar: 'tableprops tabledelete | tableinsertrowbefore tableinsertrowafter tabledeleterow | tableinsertcolbefore tableinsertcolafter tabledeletecol',
            table_appearance_options: true,
            table_default_attributes: {{ border: '1' }},
            table_default_styles: {{ 'border-collapse': 'collapse', 'width': '100%' }},
            setup: function(ed) {{
                editor = ed;
                ed.on('change', function() {{
                    window.chrome.webview.postMessage({{ type: 'content-changed' }});
                }});
                ed.on('init', function() {{
                    window.chrome.webview.postMessage({{ type: 'ready' }});
                }});
            }}
        }});

        window.getContent = function() {{
            return editor ? editor.getContent() : '';
        }};

        window.setContent = function(html) {{
            if (editor) {{
                editor.setContent(html || '');
            }}
        }};
    </script>
</body>
</html>";
    }

    /// <summary>
    /// 마크다운 테이블을 HTML 테이블로 변환
    /// </summary>
    private string ConvertMarkdownTableToHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var lines = text.Split('\n');
        var result = new System.Text.StringBuilder();
        var inTable = false;
        var tableRows = new System.Collections.Generic.List<string[]>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // 파이프로 시작하거나 끝나는 라인은 테이블 행으로 간주
            if (line.StartsWith("|") && line.EndsWith("|"))
            {
                // 구분선(---|---|---)은 건너뛰기
                if (line.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Length == 0)
                    continue;

                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                tableRows.Add(cells);
                inTable = true;
            }
            else
            {
                // 테이블이 끝났으면 HTML로 변환
                if (inTable && tableRows.Count > 0)
                {
                    result.Append(BuildHtmlTable(tableRows));
                    tableRows.Clear();
                    inTable = false;
                }

                // 일반 텍스트 추가
                if (!string.IsNullOrWhiteSpace(line))
                {
                    result.AppendLine($"<p>{WebUtility.HtmlEncode(line)}</p>");
                }
            }
        }

        // 마지막에 테이블이 남아있으면 변환
        if (tableRows.Count > 0)
        {
            result.Append(BuildHtmlTable(tableRows));
        }

        return result.ToString();
    }

    /// <summary>
    /// 테이블 행 데이터를 HTML 테이블로 변환
    /// </summary>
    private string BuildHtmlTable(System.Collections.Generic.List<string[]> rows)
    {
        if (rows.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<table style='border-collapse: collapse; width: 100%;'>");

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var tag = i == 0 ? "th" : "td"; // 첫 번째 행은 헤더
            var style = "border: 1px solid #555; padding: 8px;";

            sb.AppendLine("<tr>");
            foreach (var cell in row)
            {
                var cellValue = string.IsNullOrEmpty(cell) || cell == "---" ? "&nbsp;" : WebUtility.HtmlEncode(cell);
                sb.AppendLine($"<{tag} style='{style}'>{cellValue}</{tag}>");
            }
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        return sb.ToString();
    }

    /// <summary>
    /// 작업 상세 데이터 로드 (체크리스트, 첨부, 댓글)
    /// </summary>
    private async Task LoadTaskDetailsAsync()
    {
        try
        {
            var details = await _plannerService.GetTaskDetailsAsync(_task.Id);
            if (details == null) return;

            // 체크리스트
            if (details.Checklist?.AdditionalData != null)
            {
                foreach (var item in details.Checklist.AdditionalData)
                {
                    if (item.Value is System.Text.Json.JsonElement jsonElement)
                    {
                        var checkItem = new ChecklistItemViewModel
                        {
                            Id = item.Key,
                            Title = jsonElement.GetProperty("title").GetString() ?? "",
                            IsChecked = jsonElement.GetProperty("isChecked").GetBoolean()
                        };
                        _checklistItems.Add(checkItem);
                    }
                }
            }

            // 메모
            if (!string.IsNullOrEmpty(details.Description))
            {
                _task.Notes = details.Description;
                ShowNotesOnCardCheckBox.IsChecked = _task.HasDescription;

                // TinyMCE 에디터에 콘텐츠 설정
                await SetNotesContentAsync(_task.Notes);
            }

            // 참조/첨부 파일은 plannerTaskDetails.references에서 로드
            if (details.References?.AdditionalData != null)
            {
                foreach (var item in details.References.AdditionalData)
                {
                    if (item.Value is System.Text.Json.JsonElement jsonElement)
                    {
                        var attachment = new AttachmentViewModel
                        {
                            Id = item.Key,
                            Name = jsonElement.TryGetProperty("alias", out var alias) ? alias.GetString() ?? "" : item.Key,
                            Url = item.Key,
                            Type = jsonElement.TryGetProperty("type", out var type) ? type.GetString() ?? "" : ""
                        };
                        _attachments.Add(attachment);
                    }
                }
            }

            Log4.Debug($"[TaskEditDialog] 작업 상세 로드 완료: 체크리스트 {_checklistItems.Count}개, 첨부 {_attachments.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"[TaskEditDialog] 작업 상세 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 완료 버튼 상태 업데이트
    /// </summary>
    private void UpdateCompleteButtonState()
    {
        if (_task.IsComplete)
        {
            CompleteIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
            CompleteText.Text = "완료 취소";
            CompleteButton.Appearance = ControlAppearance.Success;
        }
        else
        {
            CompleteIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Circle24;
            CompleteText.Text = "완료로 표시";
            CompleteButton.Appearance = ControlAppearance.Secondary;
        }
    }

    /// <summary>
    /// 상태 텍스트 업데이트
    /// </summary>
    private void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    /// <summary>
    /// 변경 사항 표시
    /// </summary>
    private void MarkAsChanged()
    {
        if (_isInitialized)
        {
            _hasChanges = true;
            UpdateStatus("변경 사항이 있습니다. 저장하려면 Ctrl+S를 누르세요.");
        }
    }

    #region 이벤트 핸들러

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SaveButton_Click(sender, e);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_hasChanges && !IsDeleted)
        {
            var result = System.Windows.MessageBox.Show(
                "저장되지 않은 변경 사항이 있습니다. 저장하시겠습니까?",
                "변경 사항 저장",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _ = SaveChangesAsync();
            }
            else if (result == System.Windows.MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveChangesAsync();
    }

    private async Task SaveChangesAsync()
    {
        try
        {
            UpdateStatus("저장 중...");

            // 제목 업데이트
            _task.Title = TitleTextBox.Text;

            // 진행률
            if (ProgressComboBox.SelectedItem is ComboBoxItem progressItem)
            {
                _task.PercentComplete = int.Parse(progressItem.Tag?.ToString() ?? "0");
            }

            // 우선 순위
            if (PriorityComboBox.SelectedItem is ComboBoxItem priorityItem)
            {
                _task.Priority = int.Parse(priorityItem.Tag?.ToString() ?? "5");
            }

            // 날짜
            _task.StartDateTime = StartDatePicker.SelectedDate;
            _task.DueDateTime = DueDatePicker.SelectedDate;

            // API로 작업 업데이트 - PlannerTask 객체 생성하여 한 번에 업데이트
            var updatedTask = new Microsoft.Graph.Models.PlannerTask
            {
                Title = _task.Title,
                PercentComplete = _task.PercentComplete,
                Priority = _task.Priority
            };

            if (_task.StartDateTime.HasValue)
            {
                updatedTask.StartDateTime = new DateTimeOffset(DateTime.SpecifyKind(_task.StartDateTime.Value, DateTimeKind.Utc));
            }
            if (_task.DueDateTime.HasValue)
            {
                updatedTask.DueDateTime = new DateTimeOffset(DateTime.SpecifyKind(_task.DueDateTime.Value, DateTimeKind.Utc));
            }

            var response = await _plannerService.UpdateTaskAsync(_task.Id, _task.ETag ?? "", updatedTask);

            // ETag 업데이트
            if (response?.AdditionalData?.TryGetValue("@odata.etag", out var newEtag) == true)
            {
                _task.ETag = newEtag?.ToString();
            }

            // 버킷 이동 (변경된 경우)
            if (BucketComboBox.SelectedItem is BucketViewModel selectedBucket &&
                selectedBucket.Id != _originalBucketId)
            {
                await _plannerService.MoveTaskToBucketAsync(_task.Id, _task.ETag ?? "", selectedBucket.Id);
                _task.BucketId = selectedBucket.Id;
            }

            // 메모 업데이트
            var notesContent = await GetNotesContentAsync();
            if (!string.IsNullOrEmpty(notesContent))
            {
                await _plannerService.UpdateTaskDetailsAsync(_task.Id, description: notesContent);
            }

            _hasChanges = false;
            UpdateStatus("저장 완료!");
            Log4.Info($"[TaskEditDialog] 작업 저장 완료: {_task.Title}");

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Log4.Error($"[TaskEditDialog] 저장 실패: {ex.Message}");
            UpdateStatus($"저장 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"저장 중 오류가 발생했습니다:\n{ex.Message}", "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task<string> GetNotesContentAsync()
    {
        try
        {
            var result = await NotesWebView.CoreWebView2.ExecuteScriptAsync("window.getContent()");
            // JSON 문자열에서 따옴표 제거 및 이스케이프 처리
            if (result.StartsWith("\"") && result.EndsWith("\""))
            {
                result = result[1..^1];
            }
            return result.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// TinyMCE 에디터에 콘텐츠 설정
    /// </summary>
    private async Task SetNotesContentAsync(string? notes)
    {
        if (string.IsNullOrEmpty(notes))
            return;

        try
        {
            // 마크다운 테이블을 HTML 테이블로 변환
            var htmlContent = ConvertMarkdownTableToHtml(notes);
            var escapedNotes = htmlContent.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
            await NotesWebView.CoreWebView2.ExecuteScriptAsync(
                $"if (typeof tinymce !== 'undefined' && tinymce.activeEditor) tinymce.activeEditor.setContent('{escapedNotes}');");
        }
        catch (Exception ex)
        {
            Log4.Error($"[TaskEditDialog] Notes 설정 실패: {ex.Message}");
        }
    }

    private async void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newPercent = _task.IsComplete ? 0 : 100;
            _task.PercentComplete = newPercent;
            // IsComplete는 PercentComplete의 계산 프로퍼티이므로 자동 업데이트됨

            // 진행률 콤보박스 업데이트
            ProgressComboBox.SelectedIndex = newPercent == 100 ? 2 : 0;

            await _plannerService.UpdateTaskPercentCompleteAsync(_task.Id, _task.ETag ?? "", newPercent);
            UpdateCompleteButtonState();
            UpdateStatus(_task.IsComplete ? "완료로 표시됨" : "미완료로 변경됨");
        }
        catch (Exception ex)
        {
            Log4.Error($"[TaskEditDialog] 완료 상태 변경 실패: {ex.Message}");
            UpdateStatus($"오류: {ex.Message}");
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            $"'{_task.Title}' 작업을 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
            "작업 삭제",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                await _plannerService.DeleteTaskAsync(_task.Id, _task.ETag ?? "");
                IsDeleted = true;
                _hasChanges = false;
                Log4.Info($"[TaskEditDialog] 작업 삭제됨: {_task.Title}");
                Close();
            }
            catch (Exception ex)
            {
                Log4.Error($"[TaskEditDialog] 삭제 실패: {ex.Message}");
                System.Windows.MessageBox.Show($"삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e) => MarkAsChanged();
    private void BucketComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkAsChanged();
    private void ProgressComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkAsChanged();
    private void PriorityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkAsChanged();
    private void StartDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => MarkAsChanged();
    private void DueDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => MarkAsChanged();
    private void ShowNotesOnCardCheckBox_Changed(object sender, RoutedEventArgs e) => MarkAsChanged();

    #endregion

    #region 체크리스트 이벤트

    private async void NewChecklistItemTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(NewChecklistItemTextBox.Text))
        {
            await AddChecklistItemAsync();
        }
    }

    private async void AddChecklistItem_Click(object sender, RoutedEventArgs e)
    {
        await AddChecklistItemAsync();
    }

    private async Task AddChecklistItemAsync()
    {
        var title = NewChecklistItemTextBox.Text.Trim();
        if (string.IsNullOrEmpty(title)) return;

        try
        {
            // TODO: API로 체크리스트 아이템 추가 (추후 구현)
            var itemId = Guid.NewGuid().ToString();
            // await _plannerService.AddChecklistItemAsync(_task.Id, itemId, title);

            _checklistItems.Add(new ChecklistItemViewModel
            {
                Id = itemId,
                Title = title,
                IsChecked = false
            });

            NewChecklistItemTextBox.Clear();
            UpdateStatus("항목이 추가되었습니다. (로컬만)");
            MarkAsChanged();
        }
        catch (Exception ex)
        {
            Log4.Error($"[TaskEditDialog] 체크리스트 추가 실패: {ex.Message}");
            UpdateStatus($"오류: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    private void ChecklistItem_CheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is ChecklistItemViewModel item)
        {
            try
            {
                // TODO: API로 체크리스트 상태 업데이트 (추후 구현)
                // await _plannerService.UpdateChecklistItemAsync(_task.Id, item.Id, item.IsChecked);
                MarkAsChanged();
            }
            catch (Exception ex)
            {
                Log4.Error($"[TaskEditDialog] 체크리스트 상태 변경 실패: {ex.Message}");
            }
        }
    }

    private void DeleteChecklistItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button button && button.Tag is ChecklistItemViewModel item)
        {
            try
            {
                // TODO: API로 체크리스트 삭제 (추후 구현)
                // await _plannerService.DeleteChecklistItemAsync(_task.Id, item.Id);
                _checklistItems.Remove(item);
                UpdateStatus("항목이 삭제되었습니다. (로컬만)");
                MarkAsChanged();
            }
            catch (Exception ex)
            {
                Log4.Error($"[TaskEditDialog] 체크리스트 삭제 실패: {ex.Message}");
                UpdateStatus($"오류: {ex.Message}");
            }
        }
    }

    #endregion

    #region 첨부 파일 이벤트

    private async void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "첨부 파일 선택",
            Filter = "모든 파일 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var filePath in dialog.FileNames)
            {
                try
                {
                    // OneDrive에 업로드 후 참조 추가 (추후 구현)
                    UpdateStatus($"파일 업로드 기능은 추후 지원 예정입니다: {System.IO.Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    Log4.Error($"[TaskEditDialog] 파일 업로드 실패: {ex.Message}");
                }
            }
        }
    }

    private void Attachment_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AttachmentViewModel attachment)
        {
            try
            {
                if (!string.IsNullOrEmpty(attachment.Url))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = attachment.Url,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"[TaskEditDialog] 첨부 파일 열기 실패: {ex.Message}");
            }
        }
    }

    private void DeleteAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button button && button.Tag is AttachmentViewModel attachment)
        {
            try
            {
                // TODO: API로 첨부 파일 삭제 (추후 구현)
                // await _plannerService.DeleteReferenceAsync(_task.Id, attachment.Id);
                _attachments.Remove(attachment);
                UpdateStatus("첨부 파일이 삭제되었습니다. (로컬만)");
                MarkAsChanged();
            }
            catch (Exception ex)
            {
                Log4.Error($"[TaskEditDialog] 첨부 삭제 실패: {ex.Message}");
                UpdateStatus($"오류: {ex.Message}");
            }
        }
    }

    #endregion

    #region 댓글 이벤트

    private async void NewCommentTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            await AddCommentAsync();
        }
    }

    private async void AddComment_Click(object sender, RoutedEventArgs e)
    {
        await AddCommentAsync();
    }

    private async Task AddCommentAsync()
    {
        var content = NewCommentTextBox.Text.Trim();
        if (string.IsNullOrEmpty(content)) return;

        try
        {
            // 댓글 기능은 Microsoft Graph의 Planner API에서 직접 지원하지 않음
            // 실제로는 Teams 채널 연동 또는 별도 구현 필요
            UpdateStatus("댓글 기능은 추후 지원 예정입니다.");
            NewCommentTextBox.Clear();
        }
        catch (Exception ex)
        {
            Log4.Error($"[TaskEditDialog] 댓글 추가 실패: {ex.Message}");
            UpdateStatus($"오류: {ex.Message}");
        }
    }

    #endregion
}

#region ViewModels for Dialog

/// <summary>
/// 체크리스트 아이템 ViewModel
/// </summary>
public class ChecklistItemViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private bool _isChecked;

    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}

/// <summary>
/// 첨부 파일 ViewModel
/// </summary>
public class AttachmentViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _url = string.Empty;
    private string _type = string.Empty;

    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Url { get => _url; set => SetProperty(ref _url, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
}

/// <summary>
/// 댓글 ViewModel
/// </summary>
public class CommentViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _id = string.Empty;
    private string _content = string.Empty;
    private string _userName = string.Empty;
    private string _userInitial = string.Empty;
    private DateTime _createdAt;

    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string Content { get => _content; set => SetProperty(ref _content, value); }
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }
    public string UserInitial { get => _userInitial; set => SetProperty(ref _userInitial, value); }
    public DateTime CreatedAt { get => _createdAt; set => SetProperty(ref _createdAt, value); }
}

#endregion
