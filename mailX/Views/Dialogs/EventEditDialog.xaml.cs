using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Models;
using Wpf.Ui.Controls;
using mailX.Models;
using mailX.Services.Graph;
using mailX.Services.Search;
using mailX.Utils;

using Application = System.Windows.Application;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace mailX.Views.Dialogs;

/// <summary>
/// 일정 편집 다이얼로그 (아웃룩 스타일)
/// </summary>
public partial class EventEditDialog : FluentWindow
{
    private readonly GraphCalendarService? _calendarService;
    private readonly ContactSearchService? _contactSearchService;
    private Event? _existingEvent;
    private bool _isEditMode;
    private DateTime _previewDate;
    private string _currentShowAs = "busy"; // free, tentative, busy, oof, workingElsewhere
    private string _currentCategory = "";

    // 참석자 자동완성 관련
    private CancellationTokenSource? _attendeesSearchCts;
    private string _lastAttendeesSearchTerm = "";
    private ObservableCollection<ContactSuggestion> _attendeesSuggestions = new();

    /// <summary>
    /// 생성된/수정된 일정
    /// </summary>
    public Event? ResultEvent { get; private set; }

    /// <summary>
    /// 일정이 삭제되었는지 여부
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// 새 일정 생성 모드
    /// </summary>
    public EventEditDialog()
    {
        InitializeComponent();
        _calendarService = ((App)Application.Current).GetService<GraphCalendarService>();
        _contactSearchService = ((App)Application.Current).GetService<ContactSearchService>();
        _isEditMode = false;
        _previewDate = DateTime.Today;

        // 기본값 설정
        StartDatePicker.SelectedDate = DateTime.Today;
        EndDatePicker.SelectedDate = DateTime.Today;
        StartTimeTextBox.Text = DateTime.Now.AddHours(1).ToString("HH:00");
        EndTimeTextBox.Text = DateTime.Now.AddHours(2).ToString("HH:00");

        // 미리보기 날짜 초기화
        UpdatePreviewDate();

        // 참석자 자동완성 리스트 바인딩
        AttendeesSuggestionList.ItemsSource = _attendeesSuggestions;

        // Loaded 이벤트에서 시간 슬롯 생성 및 08:00 기준 스크롤
        Loaded += (s, e) =>
        {
            GenerateTimeSlots();
            ScrollTo0800();
        };
    }

    /// <summary>
    /// 기존 일정 수정 모드
    /// </summary>
    public EventEditDialog(Event existingEvent) : this()
    {
        _existingEvent = existingEvent;
        _isEditMode = true;
        DeleteButton.Visibility = Visibility.Visible;
        Title = $"{existingEvent.Subject ?? "일정"} - 편집";

        LoadEventData();
    }

    /// <summary>
    /// 특정 날짜에 새 일정 생성
    /// </summary>
    public EventEditDialog(DateTime targetDate) : this()
    {
        StartDatePicker.SelectedDate = targetDate;
        EndDatePicker.SelectedDate = targetDate;
        _previewDate = targetDate;
        UpdatePreviewDate();
        GenerateTimeSlots();
    }

    /// <summary>
    /// 기존 일정 데이터 로드
    /// </summary>
    private void LoadEventData()
    {
        if (_existingEvent == null) return;

        SubjectTextBox.Text = _existingEvent.Subject ?? string.Empty;
        LocationTextBox.Text = _existingEvent.Location?.DisplayName ?? string.Empty;
        BodyTextBox.Text = _existingEvent.Body?.Content ?? string.Empty;

        // 종일 일정 여부
        IsAllDayCheckBox.IsChecked = _existingEvent.IsAllDay ?? false;

        // 시작/종료 시간
        if (_existingEvent.Start?.DateTime != null)
        {
            var startDt = DateTime.Parse(_existingEvent.Start.DateTime);
            StartDatePicker.SelectedDate = startDt.Date;
            StartTimeTextBox.Text = startDt.ToString("HH:mm");
            _previewDate = startDt.Date;
        }

        if (_existingEvent.End?.DateTime != null)
        {
            var endDt = DateTime.Parse(_existingEvent.End.DateTime);
            EndDatePicker.SelectedDate = endDt.Date;
            EndTimeTextBox.Text = endDt.ToString("HH:mm");
        }

        // 알림 설정
        if (_existingEvent.ReminderMinutesBeforeStart.HasValue)
        {
            var minutes = _existingEvent.ReminderMinutesBeforeStart.Value;
            for (int i = 0; i < ReminderComboBox.Items.Count; i++)
            {
                if (ReminderComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == minutes.ToString())
                {
                    ReminderComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        // 온라인 회의
        IsOnlineMeetingToggle.IsChecked = _existingEvent.IsOnlineMeeting ?? false;

        // 참석자
        if (_existingEvent.Attendees != null && _existingEvent.Attendees.Any())
        {
            var requiredAttendees = _existingEvent.Attendees
                .Where(a => a.EmailAddress?.Address != null && a.Type == AttendeeType.Required)
                .Select(a => a.EmailAddress!.Address);
            AttendeesTextBox.Text = string.Join(", ", requiredAttendees);

            var optionalAttendees = _existingEvent.Attendees
                .Where(a => a.EmailAddress?.Address != null && a.Type == AttendeeType.Optional)
                .Select(a => a.EmailAddress!.Address);
            if (optionalAttendees.Any())
            {
                OptionalAttendeesRow.Visibility = Visibility.Visible;
                OptionalAttendeesTextBox.Text = string.Join(", ", optionalAttendees);
            }
        }

        // 상태 (ShowAs)
        if (_existingEvent.ShowAs.HasValue)
        {
            _currentShowAs = _existingEvent.ShowAs.Value.ToString().ToLower();
            UpdateShowAsDisplay();
        }

        // 카테고리
        if (_existingEvent.Categories != null && _existingEvent.Categories.Any())
        {
            _currentCategory = _existingEvent.Categories.First();
        }

        // 시간 입력 필드 활성화/비활성화
        UpdateTimeFieldsVisibility();
        UpdatePreviewDate();
        GenerateTimeSlots();
    }

    /// <summary>
    /// 키보드 이벤트 핸들러 (ESC 키 처리)
    /// </summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // 변경 사항 확인
            if (HasUnsavedChanges())
            {
                var result = System.Windows.MessageBox.Show(
                    "이 이벤트를 취소하시겠습니까?",
                    "이벤트 취소",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    DialogResult = false;
                    Close();
                }
            }
            else
            {
                DialogResult = false;
                Close();
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 변경 사항 있는지 확인
    /// </summary>
    private bool HasUnsavedChanges()
    {
        // 새 일정 생성 모드에서 내용이 있으면 변경됨
        if (!_isEditMode)
        {
            return !string.IsNullOrWhiteSpace(SubjectTextBox.Text) ||
                   !string.IsNullOrWhiteSpace(LocationTextBox.Text) ||
                   !string.IsNullOrWhiteSpace(BodyTextBox.Text) ||
                   !string.IsNullOrWhiteSpace(AttendeesTextBox.Text);
        }

        // 수정 모드에서는 원래 값과 비교
        if (_existingEvent == null) return false;

        return SubjectTextBox.Text != (_existingEvent.Subject ?? string.Empty) ||
               LocationTextBox.Text != (_existingEvent.Location?.DisplayName ?? string.Empty) ||
               BodyTextBox.Text != (_existingEvent.Body?.Content ?? string.Empty);
    }

    /// <summary>
    /// 되풀이 설정 버튼 클릭
    /// </summary>
    private void RecurrenceButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 되풀이 설정 다이얼로그 구현 (Phase 4)
        System.Windows.MessageBox.Show(
            "되풀이 일정 설정 기능은 향후 업데이트에서 제공될 예정입니다.",
            "기능 준비 중",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// 상태(다른 용무 중) 버튼 클릭
    /// </summary>
    private void ShowAsButton_Click(object sender, RoutedEventArgs e)
    {
        var contextMenu = new ContextMenu();

        var menuItems = new[]
        {
            ("free", "사용 가능", "#00B294"),
            ("tentative", "미정", "#FFC107"),
            ("busy", "다른 용무 중", "#0078D4"),
            ("oof", "부재 중", "#B4009E"),
            ("workingElsewhere", "다른 곳에서 작업 중", "#107C10")
        };

        foreach (var (value, text, color) in menuItems)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = text,
                Tag = value,
                IsChecked = _currentShowAs == value
            };

            var indicator = new System.Windows.Shapes.Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = (SolidColorBrush)new BrushConverter().ConvertFrom(color)!,
                RadiusX = 2,
                RadiusY = 2
            };
            menuItem.Icon = indicator;
            menuItem.Click += ShowAsMenuItem_Click;
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.PlacementTarget = ShowAsButton;
        contextMenu.IsOpen = true;
    }

    private void ShowAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string value)
        {
            _currentShowAs = value;
            UpdateShowAsDisplay();
        }
    }

    private void UpdateShowAsDisplay()
    {
        var (text, color) = _currentShowAs switch
        {
            "free" => ("사용 가능", "#00B294"),
            "tentative" => ("미정", "#FFC107"),
            "busy" => ("다른 용무 중", "#0078D4"),
            "oof" => ("부재 중", "#B4009E"),
            "workingElsewhere" => ("다른 곳에서 작업 중", "#107C10"),
            _ => ("다른 용무 중", "#0078D4")
        };

        ShowAsText.Text = text;
        ShowAsIndicator.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom(color)!;
    }

    /// <summary>
    /// 분류 버튼 클릭
    /// </summary>
    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var contextMenu = new ContextMenu();

        var categories = new[]
        {
            ("", "없음", "Transparent"),
            ("빨강 범주", "빨강 범주", "#E74856"),
            ("주황 범주", "주황 범주", "#FF8C00"),
            ("노랑 범주", "노랑 범주", "#F7B500"),
            ("녹색 범주", "녹색 범주", "#00B294"),
            ("파랑 범주", "파랑 범주", "#0078D4"),
            ("자주 범주", "자주 범주", "#B4009E")
        };

        foreach (var (value, text, color) in categories)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = text,
                Tag = value,
                IsChecked = _currentCategory == value
            };

            if (color != "Transparent")
            {
                var indicator = new System.Windows.Shapes.Rectangle
                {
                    Width = 12,
                    Height = 12,
                    Fill = (SolidColorBrush)new BrushConverter().ConvertFrom(color)!,
                    RadiusX = 2,
                    RadiusY = 2
                };
                menuItem.Icon = indicator;
            }

            menuItem.Click += CategoryMenuItem_Click;
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.PlacementTarget = CategoryButton;
        contextMenu.IsOpen = true;
    }

    private void CategoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string value)
        {
            _currentCategory = value;
        }
    }

    /// <summary>
    /// 선택적 참석자 버튼 클릭
    /// </summary>
    private void OptionalAttendeesButton_Click(object sender, RoutedEventArgs e)
    {
        // 선택적 참석자 행 토글
        OptionalAttendeesRow.Visibility = OptionalAttendeesRow.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// 스케줄러 버튼 클릭
    /// </summary>
    private void SchedulerButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "일정 도우미 기능은 향후 업데이트에서 제공될 예정입니다.",
            "기능 준비 중",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// 장소 검색 버튼 클릭
    /// </summary>
    private void LocationSearchButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "장소 검색 기능은 향후 업데이트에서 제공될 예정입니다.",
            "기능 준비 중",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// 이전 날짜 버튼 클릭
    /// </summary>
    private void PrevDay_Click(object sender, RoutedEventArgs e)
    {
        _previewDate = _previewDate.AddDays(-1);
        UpdatePreviewDate();
        GenerateTimeSlots();
    }

    /// <summary>
    /// 다음 날짜 버튼 클릭
    /// </summary>
    private void NextDay_Click(object sender, RoutedEventArgs e)
    {
        _previewDate = _previewDate.AddDays(1);
        UpdatePreviewDate();
        GenerateTimeSlots();
    }

    /// <summary>
    /// 미리보기 확장 버튼 클릭
    /// </summary>
    private void ExpandPreview_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "일정 확장 보기 기능은 향후 업데이트에서 제공될 예정입니다.",
            "기능 준비 중",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// 미리보기 날짜 텍스트 업데이트
    /// </summary>
    private void UpdatePreviewDate()
    {
        var culture = new CultureInfo("ko-KR");
        PreviewDateText.Text = _previewDate.ToString("ddd, yyyy, M월 d", culture);
    }

    /// <summary>
    /// 시간대별 슬롯 생성
    /// </summary>
    private void GenerateTimeSlots()
    {
        TimeSlotPanel.Children.Clear();

        // 시작/종료 시간 가져오기
        TimeSpan? eventStart = null;
        TimeSpan? eventEnd = null;

        if (TimeSpan.TryParse(StartTimeTextBox.Text, out var start))
        {
            eventStart = start;
        }
        if (TimeSpan.TryParse(EndTimeTextBox.Text, out var end))
        {
            eventEnd = end;
        }

        // 시작 날짜가 미리보기 날짜와 같은지 확인
        bool isEventDate = StartDatePicker.SelectedDate?.Date == _previewDate.Date;

        // 오전 6시부터 오후 11시까지 30분 간격으로 슬롯 생성
        for (int hour = 6; hour <= 23; hour++)
        {
            for (int minute = 0; minute < 60; minute += 30)
            {
                var slotTime = new TimeSpan(hour, minute, 0);
                var slotEnd = slotTime.Add(TimeSpan.FromMinutes(30));

                // 현재 일정과 겹치는지 확인
                bool isEventSlot = false;
                if (isEventDate && eventStart.HasValue && eventEnd.HasValue)
                {
                    isEventSlot = slotTime < eventEnd.Value && slotEnd > eventStart.Value;
                }

                var slotPanel = new Border
                {
                    Height = 25,
                    BorderBrush = TryFindResource("ControlElevationBorderBrush") as SolidColorBrush ?? Brushes.LightGray,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(12, 0, 12, 0)
                };

                var slotGrid = new Grid();
                slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(60) });
                slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // 시간 텍스트 (30분 단위에서는 정각만 표시)
                if (minute == 0)
                {
                    var timeText = new System.Windows.Controls.TextBlock
                    {
                        Text = slotTime.ToString(@"hh\:mm"),
                        FontSize = 11,
                        Foreground = TryFindResource("TextFillColorSecondaryBrush") as SolidColorBrush ?? Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(timeText, 0);
                    slotGrid.Children.Add(timeText);
                }

                // 일정 표시 (현재 편집 중인 일정과 겹치면)
                if (isEventSlot)
                {
                    var eventIndicator = new Border
                    {
                        Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#0078D4")!,
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(4, 2, 4, 2)
                    };

                    // 시작 시간 슬롯에만 제목 표시
                    if (eventStart.HasValue && slotTime == eventStart.Value)
                    {
                        var eventText = new System.Windows.Controls.TextBlock
                        {
                            Text = SubjectTextBox.Text.Length > 0 ? SubjectTextBox.Text : "새 일정",
                            FontSize = 11,
                            Foreground = Brushes.White,
                            Margin = new Thickness(4, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };
                        eventIndicator.Child = eventText;
                    }

                    Grid.SetColumn(eventIndicator, 1);
                    slotGrid.Children.Add(eventIndicator);
                }

                slotPanel.Child = slotGrid;
                TimeSlotPanel.Children.Add(slotPanel);
            }
        }
    }

    /// <summary>
    /// 종일 일정 체크박스 변경
    /// </summary>
    private void IsAllDayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTimeFieldsVisibility();
    }

    /// <summary>
    /// 시간 입력 필드 표시/숨김
    /// </summary>
    private void UpdateTimeFieldsVisibility()
    {
        var isAllDay = IsAllDayCheckBox.IsChecked ?? false;
        StartTimeTextBox.IsEnabled = !isAllDay;
        EndTimeTextBox.IsEnabled = !isAllDay;

        if (isAllDay)
        {
            StartTimeTextBox.Text = "00:00";
            EndTimeTextBox.Text = "23:59";
        }
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(SubjectTextBox.Text))
        {
            System.Windows.MessageBox.Show("제목을 입력해주세요.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SubjectTextBox.Focus();
            return;
        }

        if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
        {
            System.Windows.MessageBox.Show("날짜를 선택해주세요.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 시간 파싱
        if (!TimeSpan.TryParse(StartTimeTextBox.Text, out var startTime))
        {
            startTime = new TimeSpan(9, 0, 0);
        }
        if (!TimeSpan.TryParse(EndTimeTextBox.Text, out var endTime))
        {
            endTime = startTime.Add(TimeSpan.FromHours(1));
        }

        var startDateTime = StartDatePicker.SelectedDate.Value.Date + startTime;
        var endDateTime = EndDatePicker.SelectedDate.Value.Date + endTime;

        // 종료 시간이 시작 시간보다 빠른 경우
        if (endDateTime <= startDateTime)
        {
            System.Windows.MessageBox.Show("종료 시간이 시작 시간보다 늦어야 합니다.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 알림 시간
        int reminderMinutes = 15;
        if (ReminderComboBox.SelectedItem is ComboBoxItem selectedReminder &&
            int.TryParse(selectedReminder.Tag?.ToString(), out var mins))
        {
            reminderMinutes = mins;
        }

        // 필수 참석자 파싱
        var attendees = new List<string>();
        if (!string.IsNullOrWhiteSpace(AttendeesTextBox.Text))
        {
            attendees = AttendeesTextBox.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Contains('@'))
                .ToList();
        }

        // 선택적 참석자 파싱
        var optionalAttendees = new List<string>();
        if (!string.IsNullOrWhiteSpace(OptionalAttendeesTextBox.Text))
        {
            optionalAttendees = OptionalAttendeesTextBox.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Contains('@'))
                .ToList();
        }

        // 요청 객체 생성
        var request = new EventCreateRequest
        {
            Subject = SubjectTextBox.Text.Trim(),
            Location = LocationTextBox.Text.Trim(),
            Body = BodyTextBox.Text.Trim(),
            StartDateTime = startDateTime,
            EndDateTime = endDateTime,
            IsAllDay = IsAllDayCheckBox.IsChecked ?? false,
            IsOnlineMeeting = IsOnlineMeetingToggle.IsChecked ?? false,
            ReminderMinutesBefore = reminderMinutes,
            Attendees = attendees.Any() ? attendees : null,
            OptionalAttendees = optionalAttendees.Any() ? optionalAttendees : null,
            ShowAs = _currentShowAs,
            Categories = string.IsNullOrEmpty(_currentCategory) ? null : new List<string> { _currentCategory }
        };

        SaveButton.IsEnabled = false;

        try
        {
            if (_calendarService == null)
            {
                System.Windows.MessageBox.Show("캘린더 서비스에 연결할 수 없습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Event? result;
            if (_isEditMode && _existingEvent?.Id != null)
            {
                // 수정
                Log4.Debug($"일정 수정: {_existingEvent.Id}");
                result = await _calendarService.UpdateEventAsync(_existingEvent.Id, request);
            }
            else
            {
                // 생성
                Log4.Debug($"새 일정 생성: {request.Subject}");
                result = await _calendarService.CreateEventAsync(request);
            }

            if (result != null)
            {
                ResultEvent = result;
                Log4.Info($"일정 저장 완료: {result.Subject}");
                DialogResult = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show("일정 저장에 실패했습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"일정 저장 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"일정 저장 중 오류가 발생했습니다.\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_existingEvent?.Id == null) return;

        var result = System.Windows.MessageBox.Show(
            $"'{_existingEvent.Subject}' 일정을 삭제하시겠습니까?",
            "일정 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        DeleteButton.IsEnabled = false;

        try
        {
            if (_calendarService == null)
            {
                System.Windows.MessageBox.Show("캘린더 서비스에 연결할 수 없습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Log4.Debug($"일정 삭제: {_existingEvent.Id}");
            var success = await _calendarService.DeleteEventAsync(_existingEvent.Id);

            if (success)
            {
                IsDeleted = true;
                Log4.Info($"일정 삭제 완료: {_existingEvent.Subject}");
                DialogResult = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show("일정 삭제에 실패했습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"일정 삭제 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"일정 삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DeleteButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 취소 버튼 클릭 (구 CancelButton, 이제는 ESC로만 취소)
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #region 08:00 자동 스크롤

    /// <summary>
    /// 일정 미리보기 패널을 08:00 기준으로 자동 스크롤
    /// 06:00부터 시작하므로 08:00은 (8-6) × 2슬롯 × 25px = 100px 위치
    /// </summary>
    private void ScrollTo0800()
    {
        if (TimeSlotScrollViewer == null) return;

        // 06:00부터 시작, 08:00 = (8-6) × 2슬롯 × 25px = 100px
        var scrollOffset = (8 - 6) * 2 * 25; // 100px
        TimeSlotScrollViewer.ScrollToVerticalOffset(scrollOffset);
    }

    #endregion

    #region 시간 입력 마우스 휠 조절

    /// <summary>
    /// 시간 입력 필드 마우스 휠 처리 (30분 단위 조절)
    /// </summary>
    private void TimeTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.TextBox textBox) return;

        // 현재 시간 파싱
        if (!TimeSpan.TryParse(textBox.Text, out var currentTime))
        {
            currentTime = new TimeSpan(9, 0, 0); // 기본값
        }

        // 30분 단위 조절
        var delta = e.Delta > 0 ? 30 : -30;
        var newTime = currentTime.Add(TimeSpan.FromMinutes(delta));

        // 범위 제한 (00:00 ~ 23:30)
        if (newTime.TotalMinutes < 0)
            newTime = new TimeSpan(23, 30, 0);
        else if (newTime.TotalHours >= 24)
            newTime = new TimeSpan(0, 0, 0);

        textBox.Text = newTime.ToString(@"hh\:mm");
        e.Handled = true;

        // 미리보기 업데이트
        GenerateTimeSlots();
    }

    /// <summary>
    /// 시간 입력 필드 포커스 해제 시 미리보기 업데이트
    /// </summary>
    private void TimeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        GenerateTimeSlots();
    }

    #endregion

    #region 참석자 자동완성

    /// <summary>
    /// 참석자 텍스트 변경 시 자동완성 검색
    /// </summary>
    private async void AttendeesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_contactSearchService == null) return;

        // 현재 입력 중인 이메일/이름 추출 (쉼표로 구분된 마지막 항목)
        var text = AttendeesTextBox.Text;
        var lastCommaIndex = text.LastIndexOf(',');
        var currentTerm = lastCommaIndex >= 0
            ? text.Substring(lastCommaIndex + 1).Trim()
            : text.Trim();

        // 2자 미만이면 팝업 닫기
        if (string.IsNullOrWhiteSpace(currentTerm) || currentTerm.Length < 2)
        {
            CloseAttendeesPopup();
            return;
        }

        // 중복 검색 방지
        if (currentTerm == _lastAttendeesSearchTerm) return;
        _lastAttendeesSearchTerm = currentTerm;

        // 이전 검색 취소
        _attendeesSearchCts?.Cancel();
        _attendeesSearchCts = new CancellationTokenSource();
        var token = _attendeesSearchCts.Token;

        try
        {
            // 300ms 디바운싱
            await Task.Delay(300, token);

            if (token.IsCancellationRequested) return;

            // 검색 실행
            var results = await _contactSearchService.SearchContactsAsync(currentTerm, token);

            if (token.IsCancellationRequested) return;

            // 결과가 있으면 팝업 표시
            if (results.Any())
            {
                _attendeesSuggestions.Clear();
                foreach (var contact in results)
                {
                    _attendeesSuggestions.Add(contact);
                }

                AttendeesSuggestionPopup.IsOpen = true;
            }
            else
            {
                CloseAttendeesPopup();
            }
        }
        catch (OperationCanceledException)
        {
            // 취소됨 - 무시
        }
        catch (Exception ex)
        {
            Log4.Error($"참석자 검색 실패: {ex.Message}");
            CloseAttendeesPopup();
        }
    }

    /// <summary>
    /// 참석자 텍스트박스 키보드 이벤트 처리
    /// </summary>
    private void AttendeesTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!AttendeesSuggestionPopup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                // 다음 항목 선택
                if (AttendeesSuggestionList.SelectedIndex < _attendeesSuggestions.Count - 1)
                {
                    AttendeesSuggestionList.SelectedIndex++;
                    AttendeesSuggestionList.ScrollIntoView(AttendeesSuggestionList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                // 이전 항목 선택
                if (AttendeesSuggestionList.SelectedIndex > 0)
                {
                    AttendeesSuggestionList.SelectedIndex--;
                    AttendeesSuggestionList.ScrollIntoView(AttendeesSuggestionList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Tab:
            case Key.Enter:
                // 선택 적용
                if (AttendeesSuggestionList.SelectedItem is ContactSuggestion selected)
                {
                    ApplyAttendeeSuggestion(selected);
                    e.Handled = true;
                }
                else if (_attendeesSuggestions.Count > 0)
                {
                    // 선택된 항목이 없으면 첫 번째 항목 적용
                    ApplyAttendeeSuggestion(_attendeesSuggestions[0]);
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                // 팝업 닫기
                CloseAttendeesPopup();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 참석자 선택 적용
    /// </summary>
    private void ApplyAttendeeSuggestion(ContactSuggestion contact)
    {
        var text = AttendeesTextBox.Text;
        var lastCommaIndex = text.LastIndexOf(',');

        // 마지막 쉼표 이후 텍스트 교체
        if (lastCommaIndex >= 0)
        {
            AttendeesTextBox.Text = text.Substring(0, lastCommaIndex + 1) + " " + contact.Email + ", ";
        }
        else
        {
            AttendeesTextBox.Text = contact.Email + ", ";
        }

        // 커서를 끝으로 이동
        AttendeesTextBox.CaretIndex = AttendeesTextBox.Text.Length;

        CloseAttendeesPopup();
        _lastAttendeesSearchTerm = "";

        // 포커스 유지
        AttendeesTextBox.Focus();
    }

    /// <summary>
    /// 참석자 자동완성 팝업 닫기
    /// </summary>
    private void CloseAttendeesPopup()
    {
        AttendeesSuggestionPopup.IsOpen = false;
        _attendeesSuggestions.Clear();
    }

    /// <summary>
    /// 참석자 리스트 선택 변경
    /// </summary>
    private void AttendeesSuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 선택 변경 시 별도 처리 필요 없음 (키보드로 처리)
    }

    /// <summary>
    /// 참석자 리스트 더블클릭
    /// </summary>
    private void AttendeesSuggestionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AttendeesSuggestionList.SelectedItem is ContactSuggestion selected)
        {
            ApplyAttendeeSuggestion(selected);
        }
    }

    #endregion
}
