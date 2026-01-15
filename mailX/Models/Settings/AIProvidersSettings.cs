using System;
using System.Xml.Serialization;

namespace mailX.Models.Settings;

/// <summary>
/// AI Provider 설정
/// </summary>
[Serializable]
[XmlRoot("AIProvidersSettings")]
public class AIProvidersSettings
{
    /// <summary>
    /// 기본 사용 Provider 이름
    /// </summary>
    [XmlElement("DefaultProvider")]
    public string DefaultProvider { get; set; } = "Claude";

    /// <summary>
    /// Claude (Anthropic) 설정
    /// </summary>
    [XmlElement("Claude")]
    public AIProviderConfig Claude { get; set; } = new()
    {
        Model = "claude-sonnet-4-20250514",
        BaseUrl = "https://api.anthropic.com"
    };

    /// <summary>
    /// OpenAI 설정
    /// </summary>
    [XmlElement("OpenAI")]
    public AIProviderConfig OpenAI { get; set; } = new()
    {
        Model = "gpt-4o",
        BaseUrl = "https://api.openai.com/v1"
    };

    /// <summary>
    /// Google Gemini 설정
    /// </summary>
    [XmlElement("Gemini")]
    public AIProviderConfig Gemini { get; set; } = new()
    {
        Model = "gemini-2.0-flash-exp",
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta"
    };

    /// <summary>
    /// Ollama (로컬) 설정
    /// </summary>
    [XmlElement("Ollama")]
    public AIProviderConfig Ollama { get; set; } = new()
    {
        Model = "llama3.3",
        BaseUrl = "http://localhost:11434"
    };

    /// <summary>
    /// LM Studio (로컬) 설정
    /// </summary>
    [XmlElement("LMStudio")]
    public AIProviderConfig LMStudio { get; set; } = new()
    {
        Model = "local-model",
        BaseUrl = "http://localhost:1234/v1"
    };
}

/// <summary>
/// 개별 AI Provider 설정
/// </summary>
[Serializable]
public class AIProviderConfig
{
    /// <summary>
    /// API 키 (로컬 Provider는 불필요)
    /// </summary>
    [XmlElement("ApiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// 사용할 모델명
    /// </summary>
    [XmlElement("Model")]
    public string? Model { get; set; }

    /// <summary>
    /// API 기본 URL
    /// </summary>
    [XmlElement("BaseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// API 키가 설정되었는지 확인 (로컬 Provider는 항상 true)
    /// </summary>
    [XmlIgnore]
    public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);
}
