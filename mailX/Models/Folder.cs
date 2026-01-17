using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mailX.Models;

/// <summary>
/// 폴더 모델 - Outlook 메일 폴더 정보
/// </summary>
public class Folder
{
    /// <summary>
    /// Graph API 폴더 ID (문자열 PK)
    /// </summary>
    [Key]
    [MaxLength(500)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 폴더 표시 이름
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 상위 폴더 ID
    /// </summary>
    [MaxLength(500)]
    public string? ParentFolderId { get; set; }

    /// <summary>
    /// 전체 아이템 수
    /// </summary>
    public int TotalItemCount { get; set; }

    /// <summary>
    /// 읽지 않은 아이템 수
    /// </summary>
    public int UnreadItemCount { get; set; }

    /// <summary>
    /// 소속 계정 이메일
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// 하위 폴더 컬렉션 (트리 구조용, DB 비저장)
    /// </summary>
    [NotMapped]
    public ObservableCollection<Folder> Children { get; set; } = new();

    /// <summary>
    /// 폴더 깊이 (트리 표시용, DB 비저장)
    /// </summary>
    [NotMapped]
    public int Depth { get; set; }

    /// <summary>
    /// 확장 여부 (TreeView용, DB 비저장)
    /// </summary>
    [NotMapped]
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// 즐겨찾기 여부 (DB 저장)
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// 즐겨찾기 순서 (DB 저장, 낮을수록 위쪽)
    /// </summary>
    public int FavoriteOrder { get; set; }
}
