namespace MaiX.Models;

/// <summary>
/// 검색 폴더 선택 옵션 아이템
/// </summary>
public class SearchFolderItem
{
    /// <summary>
    /// 폴더 ID (null = 모든 폴더)
    /// </summary>
    public string? FolderId { get; set; }

    /// <summary>
    /// 표시 이름
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 폴더 아이콘
    /// </summary>
    public string Icon { get; set; } = "📁";

    /// <summary>
    /// 구분선 여부 (ComboBox에서 시각적 구분용)
    /// </summary>
    public bool IsSeparator { get; set; }

    /// <summary>
    /// Folder 객체 참조 (실제 폴더인 경우)
    /// </summary>
    public Folder? Folder { get; set; }

    public override string ToString() => DisplayName;
}
