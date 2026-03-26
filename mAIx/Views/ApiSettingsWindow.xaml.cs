using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MaiX.Services.Storage;
using MaiX.Utils;

namespace MaiX.Views;

/// <summary>
/// API 관리 창 - LLM Provider API 키 및 설정 관리
/// </summary>
public partial class ApiSettingsWindow : FluentWindow
{
    private readonly AppSettingsManager _settingsManager;
    private readonly HttpClient _httpClient;

    // 테스트 성공 여부 추적
    private bool _claudeTestPassed = false;
    private bool _openaiTestPassed = false;
    private bool _geminiTestPassed = false;
    private bool _ollamaTestPassed = false;
    private bool _lmstudioTestPassed = false;

    // 모델 선택 여부 추적
    private bool _claudeModelSelected = false;
    private bool _openaiModelSelected = false;
    private bool _geminiModelSelected = false;
    private bool _ollamaModelSelected = false;
    private bool _lmstudioModelSelected = false;

    public ApiSettingsWindow(AppSettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        InitializeComponent();
        LoadSettings();

        // ESC 키로 창 닫기
        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        };

        // 모델 선택 이벤트 등록
        ClaudeModelComboBox.SelectionChanged += (s, e) => { _claudeModelSelected = ClaudeModelComboBox.SelectedItem != null; UpdateSaveButtonState(); };
        OpenAIModelComboBox.SelectionChanged += (s, e) => { _openaiModelSelected = OpenAIModelComboBox.SelectedItem != null; UpdateSaveButtonState(); };
        GeminiModelComboBox.SelectionChanged += (s, e) => { _geminiModelSelected = GeminiModelComboBox.SelectedItem != null; UpdateSaveButtonState(); };
        OllamaModelComboBox.SelectionChanged += (s, e) => { _ollamaModelSelected = OllamaModelComboBox.SelectedItem != null; UpdateSaveButtonState(); };
        LMStudioModelComboBox.SelectionChanged += (s, e) => { _lmstudioModelSelected = LMStudioModelComboBox.SelectedItem != null; UpdateSaveButtonState(); };

        // TinyMCE API Key 변경 감지
        TinyMCEApiKeyBox.PasswordChanged += (s, e) => UpdateSaveButtonState();
    }

    /// <summary>
    /// 설정값 로드
    /// </summary>
    private void LoadSettings()
    {
        var settings = _settingsManager.AIProviders;

        // 기본 Provider 선택
        foreach (ComboBoxItem item in DefaultProviderComboBox.Items)
        {
            if (item.Tag?.ToString() == settings.DefaultProvider)
            {
                DefaultProviderComboBox.SelectedItem = item;
                break;
            }
        }

        // Claude 설정 - API Key와 Model이 모두 있어야 "설정됨"
        ClaudeApiKeyBox.Password = settings.Claude.ApiKey ?? "";
        ClaudeBaseUrlBox.Text = settings.Claude.BaseUrl ?? "https://api.anthropic.com";
        if (!string.IsNullOrEmpty(settings.Claude.ApiKey) && !string.IsNullOrEmpty(settings.Claude.Model))
        {
            ClaudeModelComboBox.Items.Add(new ComboBoxItem { Content = settings.Claude.Model, Tag = settings.Claude.Model });
            ClaudeModelComboBox.SelectedIndex = 0;
            ClaudeModelComboBox.IsEnabled = true;
            _claudeTestPassed = true;
            _claudeModelSelected = true;
            ClaudeStatusText.Text = "✅ 설정됨";
            ClaudeStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }

        // OpenAI 설정 - API Key와 Model이 모두 있어야 "설정됨"
        OpenAIApiKeyBox.Password = settings.OpenAI.ApiKey ?? "";
        OpenAIBaseUrlBox.Text = settings.OpenAI.BaseUrl ?? "https://api.openai.com/v1";
        if (!string.IsNullOrEmpty(settings.OpenAI.ApiKey) && !string.IsNullOrEmpty(settings.OpenAI.Model))
        {
            OpenAIModelComboBox.Items.Add(new ComboBoxItem { Content = settings.OpenAI.Model, Tag = settings.OpenAI.Model });
            OpenAIModelComboBox.SelectedIndex = 0;
            OpenAIModelComboBox.IsEnabled = true;
            _openaiTestPassed = true;
            _openaiModelSelected = true;
            OpenAIStatusText.Text = "✅ 설정됨";
            OpenAIStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }

        // Gemini 설정 - API Key와 Model이 모두 있어야 "설정됨"
        GeminiApiKeyBox.Password = settings.Gemini.ApiKey ?? "";
        GeminiBaseUrlBox.Text = settings.Gemini.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta";
        if (!string.IsNullOrEmpty(settings.Gemini.ApiKey) && !string.IsNullOrEmpty(settings.Gemini.Model))
        {
            GeminiModelComboBox.Items.Add(new ComboBoxItem { Content = settings.Gemini.Model, Tag = settings.Gemini.Model });
            GeminiModelComboBox.SelectedIndex = 0;
            GeminiModelComboBox.IsEnabled = true;
            _geminiTestPassed = true;
            _geminiModelSelected = true;
            GeminiStatusText.Text = "✅ 설정됨";
            GeminiStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }

        // Ollama 설정 - BaseUrl만 로드 (테스트 통과 후에만 설정됨 표시)
        OllamaBaseUrlBox.Text = settings.Ollama.BaseUrl ?? "http://localhost:11434";
        // Ollama는 로컬 서버이므로 테스트 후에만 모델 선택 가능

        // LM Studio 설정 - BaseUrl만 로드 (테스트 통과 후에만 설정됨 표시)
        LMStudioBaseUrlBox.Text = settings.LMStudio.BaseUrl ?? "http://localhost:1234/v1";
        // LM Studio도 로컬 서버이므로 테스트 후에만 모델 선택 가능

        // TinyMCE 설정
        TinyMCEApiKeyBox.Password = settings.TinyMCE.ApiKey ?? "";
        if (!string.IsNullOrEmpty(settings.TinyMCE.ApiKey))
        {
            TinyMCEStatusText.Text = "✅ 설정됨";
            TinyMCEStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }

        UpdateSaveButtonState();
        Log4.Debug("[ApiSettingsWindow] 설정 로드 완료");
    }

    /// <summary>
    /// 저장 버튼 활성화 상태 업데이트
    /// </summary>
    private void UpdateSaveButtonState()
    {
        // 최소 하나의 Provider가 테스트 통과 + 모델 선택되어야 저장 가능
        bool canSave = (_claudeTestPassed && _claudeModelSelected) ||
                       (_openaiTestPassed && _openaiModelSelected) ||
                       (_geminiTestPassed && _geminiModelSelected) ||
                       (_ollamaTestPassed && _ollamaModelSelected) ||
                       (_lmstudioTestPassed && _lmstudioModelSelected);

        SaveButton.IsEnabled = canSave;

        if (canSave)
        {
            SaveStatusText.Text = "저장 가능합니다.";
        }
        else
        {
            SaveStatusText.Text = "최소 하나의 Provider에서 테스트 후 모델을 선택하세요.";
        }
    }

    #region Claude 테스트

    private async void ClaudeTestButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ClaudeApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus(ClaudeStatusText, "❌ API Key를 입력하세요", false);
            return;
        }

        ClaudeTestButton.IsEnabled = false;
        SetStatus(ClaudeStatusText, "🔄 테스트 중...", null);

        try
        {
            // Claude는 모델 목록 API가 없으므로 미리 정의된 목록 사용
            var models = new List<string>
            {
                "claude-sonnet-4-20250514",
                "claude-opus-4-20250514",
                "claude-3-5-sonnet-20241022",
                "claude-3-5-haiku-20241022",
                "claude-3-opus-20240229",
                "claude-3-sonnet-20240229",
                "claude-3-haiku-20240307"
            };

            // API 키 유효성 테스트 (간단한 요청)
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { model = "claude-3-haiku-20240307", max_tokens = 1, messages = new[] { new { role = "user", content = "hi" } } }),
                System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // BadRequest도 API 키가 유효하다는 의미일 수 있음
                _claudeTestPassed = true;
                SetStatus(ClaudeStatusText, "✅ 연결 성공", true);
                PopulateModelComboBox(ClaudeModelComboBox, models);

                // 테스트 성공 시 API Key 저장
                _settingsManager.AIProviders.Claude.ApiKey = apiKey;
                _settingsManager.AIProviders.Claude.BaseUrl = ClaudeBaseUrlBox.Text;
                _settingsManager.SaveAIProviders();
                Log4.Info("[ApiSettingsWindow] Claude API Key 저장됨");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _claudeTestPassed = false;
                SetStatus(ClaudeStatusText, "❌ API Key 인증 실패", false);
            }
            else
            {
                _claudeTestPassed = false;
                SetStatus(ClaudeStatusText, $"❌ 오류: {response.StatusCode}", false);
            }
        }
        catch (Exception ex)
        {
            _claudeTestPassed = false;
            SetStatus(ClaudeStatusText, $"❌ 연결 실패", false);
            Log4.Error($"[ApiSettingsWindow] Claude 테스트 실패: {ex.Message}");
        }
        finally
        {
            ClaudeTestButton.IsEnabled = true;
            UpdateSaveButtonState();
        }
    }

    #endregion

    #region OpenAI 테스트

    private async void OpenAITestButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = OpenAIApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus(OpenAIStatusText, "❌ API Key를 입력하세요", false);
            return;
        }

        OpenAITestButton.IsEnabled = false;
        SetStatus(OpenAIStatusText, "🔄 테스트 중...", null);

        try
        {
            var baseUrl = OpenAIBaseUrlBox.Text.TrimEnd('/');
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var json = JsonDocument.Parse(content);
                var models = new List<string>();

                if (json.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var model in data.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var id))
                        {
                            var modelId = id.GetString();
                            // GPT 모델만 필터링
                            if (modelId != null && (modelId.Contains("gpt") || modelId.Contains("o1") || modelId.Contains("o3")))
                            {
                                models.Add(modelId);
                            }
                        }
                    }
                }

                if (models.Count > 0)
                {
                    models.Sort();
                    _openaiTestPassed = true;
                    SetStatus(OpenAIStatusText, $"✅ {models.Count}개 모델 발견", true);
                    PopulateModelComboBox(OpenAIModelComboBox, models);

                    // 테스트 성공 시 API Key 저장
                    _settingsManager.AIProviders.OpenAI.ApiKey = apiKey;
                    _settingsManager.AIProviders.OpenAI.BaseUrl = baseUrl;
                    _settingsManager.SaveAIProviders();
                    Log4.Info("[ApiSettingsWindow] OpenAI API Key 저장됨");
                }
                else
                {
                    _openaiTestPassed = false;
                    SetStatus(OpenAIStatusText, "❌ 사용 가능한 모델 없음", false);
                }
            }
            else
            {
                _openaiTestPassed = false;
                SetStatus(OpenAIStatusText, $"❌ 인증 실패", false);
            }
        }
        catch (Exception ex)
        {
            _openaiTestPassed = false;
            SetStatus(OpenAIStatusText, "❌ 연결 실패", false);
            Log4.Error($"[ApiSettingsWindow] OpenAI 테스트 실패: {ex.Message}");
        }
        finally
        {
            OpenAITestButton.IsEnabled = true;
            UpdateSaveButtonState();
        }
    }

    #endregion

    #region Gemini 테스트

    private async void GeminiTestButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = GeminiApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus(GeminiStatusText, "❌ API Key를 입력하세요", false);
            return;
        }

        GeminiTestButton.IsEnabled = false;
        SetStatus(GeminiStatusText, "🔄 테스트 중...", null);

        try
        {
            var baseUrl = GeminiBaseUrlBox.Text.TrimEnd('/');
            var url = $"{baseUrl}/models?key={apiKey}";

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var json = JsonDocument.Parse(content);
                var models = new List<string>();

                if (json.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var name))
                        {
                            var modelName = name.GetString();
                            if (modelName != null && modelName.StartsWith("models/"))
                            {
                                var shortName = modelName.Replace("models/", "");
                                // gemini 모델만 필터링
                                if (shortName.Contains("gemini"))
                                {
                                    models.Add(shortName);
                                }
                            }
                        }
                    }
                }

                if (models.Count > 0)
                {
                    models.Sort();
                    _geminiTestPassed = true;
                    SetStatus(GeminiStatusText, $"✅ {models.Count}개 모델 발견", true);
                    PopulateModelComboBox(GeminiModelComboBox, models);

                    // 테스트 성공 시 API Key 저장
                    _settingsManager.AIProviders.Gemini.ApiKey = apiKey;
                    _settingsManager.AIProviders.Gemini.BaseUrl = baseUrl;
                    _settingsManager.SaveAIProviders();
                    Log4.Info("[ApiSettingsWindow] Gemini API Key 저장됨");
                }
                else
                {
                    _geminiTestPassed = false;
                    SetStatus(GeminiStatusText, "❌ 사용 가능한 모델 없음", false);
                }
            }
            else
            {
                _geminiTestPassed = false;
                SetStatus(GeminiStatusText, "❌ 인증 실패", false);
            }
        }
        catch (Exception ex)
        {
            _geminiTestPassed = false;
            SetStatus(GeminiStatusText, "❌ 연결 실패", false);
            Log4.Error($"[ApiSettingsWindow] Gemini 테스트 실패: {ex.Message}");
        }
        finally
        {
            GeminiTestButton.IsEnabled = true;
            UpdateSaveButtonState();
        }
    }

    #endregion

    #region Ollama 테스트

    private async void OllamaTestButton_Click(object sender, RoutedEventArgs e)
    {
        OllamaTestButton.IsEnabled = false;
        SetStatus(OllamaStatusText, "🔄 테스트 중...", null);

        try
        {
            var baseUrl = OllamaBaseUrlBox.Text.TrimEnd('/');
            var response = await _httpClient.GetAsync($"{baseUrl}/api/tags");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var json = JsonDocument.Parse(content);
                var models = new List<string>();

                if (json.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var name))
                        {
                            var modelName = name.GetString();
                            if (!string.IsNullOrEmpty(modelName))
                            {
                                models.Add(modelName);
                            }
                        }
                    }
                }

                if (models.Count > 0)
                {
                    _ollamaTestPassed = true;
                    SetStatus(OllamaStatusText, $"✅ {models.Count}개 모델 발견", true);
                    PopulateModelComboBox(OllamaModelComboBox, models);

                    // 테스트 성공 시 BaseUrl 저장
                    _settingsManager.AIProviders.Ollama.BaseUrl = baseUrl;
                    _settingsManager.SaveAIProviders();
                    Log4.Info("[ApiSettingsWindow] Ollama 연결 설정 저장됨");
                }
                else
                {
                    _ollamaTestPassed = false;
                    SetStatus(OllamaStatusText, "❌ 설치된 모델 없음", false);
                }
            }
            else
            {
                _ollamaTestPassed = false;
                SetStatus(OllamaStatusText, "❌ 연결 실패", false);
            }
        }
        catch (Exception ex)
        {
            _ollamaTestPassed = false;
            SetStatus(OllamaStatusText, "❌ 서버 연결 실패", false);
            Log4.Error($"[ApiSettingsWindow] Ollama 테스트 실패: {ex.Message}");
        }
        finally
        {
            OllamaTestButton.IsEnabled = true;
            UpdateSaveButtonState();
        }
    }

    #endregion

    #region LM Studio 테스트

    private async void LMStudioTestButton_Click(object sender, RoutedEventArgs e)
    {
        LMStudioTestButton.IsEnabled = false;
        SetStatus(LMStudioStatusText, "🔄 테스트 중...", null);

        try
        {
            var baseUrl = LMStudioBaseUrlBox.Text.TrimEnd('/');
            var response = await _httpClient.GetAsync($"{baseUrl}/models");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var json = JsonDocument.Parse(content);
                var models = new List<string>();

                if (json.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var model in data.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var id))
                        {
                            var modelId = id.GetString();
                            if (!string.IsNullOrEmpty(modelId))
                            {
                                models.Add(modelId);
                            }
                        }
                    }
                }

                if (models.Count > 0)
                {
                    _lmstudioTestPassed = true;
                    SetStatus(LMStudioStatusText, $"✅ {models.Count}개 모델 발견", true);
                    PopulateModelComboBox(LMStudioModelComboBox, models);
                    
                    // 테스트 성공 시 BaseUrl 저장
                    _settingsManager.AIProviders.LMStudio.BaseUrl = baseUrl;
                    _settingsManager.SaveAIProviders();
                    Log4.Info("[ApiSettingsWindow] LM Studio 연결 설정 저장됨");
                }
                else
                {
                    _lmstudioTestPassed = false;
                    SetStatus(LMStudioStatusText, "❌ 로드된 모델 없음", false);
                }
            }
            else
            {
                _lmstudioTestPassed = false;
                SetStatus(LMStudioStatusText, "❌ 연결 실패", false);
            }
        }
        catch (Exception ex)
        {
            _lmstudioTestPassed = false;
            SetStatus(LMStudioStatusText, "❌ 서버 연결 실패", false);
            Log4.Error($"[ApiSettingsWindow] LM Studio 테스트 실패: {ex.Message}");
        }
        finally
        {
            LMStudioTestButton.IsEnabled = true;
            UpdateSaveButtonState();
        }
    }

    #endregion

    #region 헬퍼 메서드

    private void SetStatus(System.Windows.Controls.TextBlock statusText, string message, bool? success)
    {
        statusText.Text = message;
        if (success == true)
        {
            statusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
        }
        else if (success == false)
        {
            statusText.Foreground = new SolidColorBrush(Colors.Tomato);
        }
        else
        {
            statusText.Foreground = new SolidColorBrush(Colors.Orange);
        }
    }

    private void PopulateModelComboBox(ComboBox comboBox, List<string> models)
    {
        comboBox.Items.Clear();
        foreach (var model in models)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = model, Tag = model });
        }
        comboBox.IsEnabled = true;
        if (models.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    #endregion

    #region 이벤트 핸들러

    private void DefaultProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 기본 Provider 변경 시 해당 Provider가 설정되어 있는지 확인
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 설정값 저장
    /// </summary>
    private void SaveSettings()
    {
        var settings = _settingsManager.AIProviders;

        // 기본 Provider 저장
        if (DefaultProviderComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            settings.DefaultProvider = selectedItem.Tag?.ToString() ?? "Claude";
        }

        // Claude 설정 (테스트 통과 + 모델 선택된 경우에만)
        if (_claudeTestPassed && _claudeModelSelected && ClaudeModelComboBox.SelectedItem is ComboBoxItem claudeModel)
        {
            settings.Claude.ApiKey = ClaudeApiKeyBox.Password;
            settings.Claude.Model = claudeModel.Tag?.ToString();
            settings.Claude.BaseUrl = ClaudeBaseUrlBox.Text;
        }

        // OpenAI 설정
        if (_openaiTestPassed && _openaiModelSelected && OpenAIModelComboBox.SelectedItem is ComboBoxItem openaiModel)
        {
            settings.OpenAI.ApiKey = OpenAIApiKeyBox.Password;
            settings.OpenAI.Model = openaiModel.Tag?.ToString();
            settings.OpenAI.BaseUrl = OpenAIBaseUrlBox.Text;
        }

        // Gemini 설정
        if (_geminiTestPassed && _geminiModelSelected && GeminiModelComboBox.SelectedItem is ComboBoxItem geminiModel)
        {
            settings.Gemini.ApiKey = GeminiApiKeyBox.Password;
            settings.Gemini.Model = geminiModel.Tag?.ToString();
            settings.Gemini.BaseUrl = GeminiBaseUrlBox.Text;
        }

        // Ollama 설정
        if (_ollamaTestPassed && _ollamaModelSelected && OllamaModelComboBox.SelectedItem is ComboBoxItem ollamaModel)
        {
            settings.Ollama.Model = ollamaModel.Tag?.ToString();
            settings.Ollama.BaseUrl = OllamaBaseUrlBox.Text;
        }

        // LM Studio 설정
        if (_lmstudioTestPassed && _lmstudioModelSelected && LMStudioModelComboBox.SelectedItem is ComboBoxItem lmstudioModel)
        {
            settings.LMStudio.Model = lmstudioModel.Tag?.ToString();
            settings.LMStudio.BaseUrl = LMStudioBaseUrlBox.Text;
        }

        // TinyMCE 설정
        if (!string.IsNullOrWhiteSpace(TinyMCEApiKeyBox.Password))
        {
            settings.TinyMCE.ApiKey = TinyMCEApiKeyBox.Password;
        }

        _settingsManager.SaveAIProviders();
        Log4.Info("[ApiSettingsWindow] API 설정 저장 완료");
    }

    #endregion

    #region TinyMCE

    /// <summary>
    /// TinyMCE API 키 발급 링크 클릭
    /// </summary>
    private void TinyMCEGetKeyLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    #endregion
}
