using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MaiX.Services.Audio;
using MaiX.Services.Storage;
using MaiX.Utils;

namespace MaiX.Views;

/// <summary>
/// 마이크 설정 창 - 장치 선택, 볼륨 조절, 실시간 레벨 모니터링, 테스트 녹음/재생
/// </summary>
public partial class MicrophoneSettingsWindow : FluentWindow
{
    private readonly MicrophoneTestService _micService;
    private readonly AppSettingsManager _settingsManager;
    private string? _selectedDeviceId;
    private string? _testFilePath;
    private bool _isRecording;
    private System.Timers.Timer? _recordTimer;
    private int _recordSeconds;

    public MicrophoneSettingsWindow(AppSettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _micService = new MicrophoneTestService();
        InitializeComponent();

        _micService.VolumeLevelChanged += OnVolumeLevelChanged;
        _micService.DecibelLevelChanged += OnDecibelLevelChanged;
        _micService.TestRecordingCompleted += OnTestRecordingCompleted;
        _micService.TestPlaybackCompleted += OnTestPlaybackCompleted;

        Closing += MicrophoneSettingsWindow_Closing;

        MicrophoneComboBox.SelectionChanged += MicrophoneComboBox_SelectionChanged;
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        RefreshDevicesButton.Click += (s, e) => LoadDevices();
        RecordButton.Click += RecordButton_Click;
        PlayButton.Click += PlayButton_Click;
        SaveButton.Click += SaveButton_Click;
        CloseButton.Click += (s, e) => Close();

        // ESC 키로 창 닫기
        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        };

        LoadDevices();
    }

    private void LoadDevices()
    {
        var devices = MicrophoneTestService.GetAvailableDevices();
        MicrophoneComboBox.ItemsSource = devices;

        if (devices.Count == 0)
        {
            DeviceStatusText.Text = "마이크 장치를 찾을 수 없습니다.";
            DeviceStatusText.Visibility = Visibility.Visible;
            MicrophoneComboBox.IsEnabled = false;
            RecordButton.IsEnabled = false;
            return;
        }

        DeviceStatusText.Visibility = Visibility.Collapsed;
        MicrophoneComboBox.IsEnabled = true;
        RecordButton.IsEnabled = true;

        // 저장된 장치 복원
        var savedId = _settingsManager.UserPreferences.PreferredMicrophoneDeviceId;
        var savedDevice = devices.FirstOrDefault(d => d.DeviceId == savedId);
        MicrophoneComboBox.SelectedItem = savedDevice ?? devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
    }

    private void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneComboBox.SelectedItem is AudioDeviceInfo device)
        {
            _selectedDeviceId = device.DeviceId;
            _micService.StopMonitoring();
            _micService.StartMonitoring(device.DeviceId);

            var vol = _micService.GetDeviceVolume(device.DeviceId);
            VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
            VolumeSlider.Value = vol * 100;
            VolumeValueText.Text = $"{(int)(vol * 100)}%";
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_selectedDeviceId != null)
        {
            _micService.SetDeviceVolume(_selectedDeviceId, (float)(VolumeSlider.Value / 100.0));
            VolumeValueText.Text = $"{(int)VolumeSlider.Value}%";
        }
    }

    private void OnVolumeLevelChanged(float level)
    {
        Dispatcher.Invoke(() =>
        {
            VolumeLevelBar.Value = level * 100;
            var brush = level < 0.7f ? Brushes.Green
                      : level < 0.9f ? Brushes.Orange
                      : Brushes.Red;
            VolumeLevelBar.Foreground = brush;
        });
    }

    private void OnDecibelLevelChanged(float db)
    {
        Dispatcher.Invoke(() =>
        {
            DecibelMeter.Value = Math.Max(0, (db + 60) / 60.0 * 100);
            DecibelValueText.Text = $"{db:F1} dB";
        });
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            if (_selectedDeviceId == null) return;
            _isRecording = true;
            RecordButton.Content = "녹음 중지";
            RecordStatusText.Text = "녹음 중...";
            PlayButton.IsEnabled = false;
            _recordSeconds = 0;
            RecordTimeText.Text = "00:00";
            _recordTimer = new System.Timers.Timer(1000);
            _recordTimer.Elapsed += (s, ev) => Dispatcher.Invoke(() =>
            {
                _recordSeconds++;
                RecordTimeText.Text = $"{_recordSeconds / 60:D2}:{_recordSeconds % 60:D2}";
            });
            _recordTimer.Start();
            _micService.StartTestRecording(_selectedDeviceId);
        }
        else
        {
            _isRecording = false;
            _recordTimer?.Stop();
            _recordTimer?.Dispose();
            _recordTimer = null;
            RecordButton.Content = "녹음 시작";
            RecordStatusText.Text = "처리 중...";
            _micService.StopTestRecording();
        }
    }

    private void OnTestRecordingCompleted(string filePath)
    {
        _testFilePath = filePath;
        Dispatcher.Invoke(() =>
        {
            PlayButton.IsEnabled = true;
            RecordStatusText.Text = "녹음 완료";
            // 녹음 완료 후 모니터링 재시작
            if (_selectedDeviceId != null)
            {
                _micService.StartMonitoring(_selectedDeviceId);
            }
        });
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_testFilePath != null)
        {
            RecordStatusText.Text = "재생 중...";
            _micService.PlayTestRecording(_testFilePath);
        }
    }

    private void OnTestPlaybackCompleted()
    {
        Dispatcher.Invoke(() => RecordStatusText.Text = "재생 완료");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDeviceId != null)
        {
            _settingsManager.UserPreferences.PreferredMicrophoneDeviceId = _selectedDeviceId;
            _settingsManager.SaveUserPreferences();
            SaveStatusText.Text = "마이크 설정이 저장되었습니다.";
            SaveStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            Log4.Info($"[마이크설정] 장치 저장: {_selectedDeviceId}");
        }
    }

    private void MicrophoneSettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _recordTimer?.Stop();
        _recordTimer?.Dispose();
        _micService.StopMonitoring();
        _micService.Dispose();
    }
}
