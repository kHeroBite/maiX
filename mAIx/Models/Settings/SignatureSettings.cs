using System.Collections.Generic;

namespace mAIx.Models.Settings;

/// <summary>
/// 이메일 서명 설정
/// </summary>
public class SignatureSettings
{
    /// <summary>
    /// 서명 목록
    /// </summary>
    public List<EmailSignature> Signatures { get; set; } = new();

    /// <summary>
    /// 기본 서명 ID (새 메일용)
    /// </summary>
    public string? DefaultSignatureId { get; set; }

    /// <summary>
    /// 답장/전달 시 기본 서명 ID
    /// </summary>
    public string? ReplyForwardSignatureId { get; set; }

    /// <summary>
    /// 새 메일에 자동으로 서명 추가
    /// </summary>
    public bool AutoAddToNewMail { get; set; } = true;

    /// <summary>
    /// 답장/전달에 자동으로 서명 추가
    /// </summary>
    public bool AutoAddToReplyForward { get; set; } = true;
}

/// <summary>
/// 이메일 서명
/// </summary>
public class EmailSignature
{
    /// <summary>
    /// 고유 ID
    /// </summary>
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>
    /// 서명 이름 (예: "업무용", "개인용")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// HTML 형식 서명 내용
    /// </summary>
    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>
    /// 텍스트 형식 서명 내용
    /// </summary>
    public string PlainTextContent { get; set; } = string.Empty;

    /// <summary>
    /// 생성 시간
    /// </summary>
    public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;

    /// <summary>
    /// 수정 시간
    /// </summary>
    public System.DateTime ModifiedAt { get; set; } = System.DateTime.Now;
}
