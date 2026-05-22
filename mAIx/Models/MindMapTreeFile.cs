// LLM 트리 마크다운과 사용자 편집 상태를 디스크에 영속화하는 모델

using System;
using System.Text.Json.Serialization;

namespace mAIx.Models;

/// <summary>
/// {recordingPath}.mindmap.json 파일 스키마.
/// LLM 자동 생성 트리 또는 사용자가 우클릭 메뉴로 편집한 트리를 디스크에 영속화.
/// IsUserEdited=true면 LLM 자동 갱신 skip (사용자 의도 보존).
/// </summary>
public sealed class MindMapTreeFile
{
    /// <summary>markmap 호환 마크다운 트리 (# Root / - L1 / -- L2 / ...)</summary>
    [JsonPropertyName("markdown")]
    public string Markdown { get; set; } = "";

    /// <summary>사용자가 우클릭 편집했으면 true → LLM 갱신 skip</summary>
    [JsonPropertyName("isUserEdited")]
    public bool IsUserEdited { get; set; }

    /// <summary>마지막 갱신 시각 (UTC)</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 이 녹음파일에 사용자가 선택한 마인드맵 렌더링 스타일.
    /// null이면 글로벌 default 스타일 사용.
    /// 유효값: "radial-tree" | "cluster" | "force" | "mindmap-lr" | "sunburst"
    /// </summary>
    [JsonPropertyName("preferredStyle")]
    public string? PreferredStyle { get; set; }
}
