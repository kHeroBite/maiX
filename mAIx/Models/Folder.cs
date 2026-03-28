using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mAIx.Models;

/// <summary>
/// 폴더 모델 - Outlook 메일 폴더 정보
/// DB 엔티티 + INotifyPropertyChanged 구현 (EF Core 직접 바인딩 지원)
/// </summary>
public class Folder : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
    private int _unreadItemCount;
    public int UnreadItemCount
    {
        get => _unreadItemCount;
        set { if (_unreadItemCount != value) { _unreadItemCount = value; OnPropertyChanged(nameof(UnreadItemCount)); } }
    }

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
    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(nameof(IsFavorite)); } }
    }

    /// <summary>
    /// 즐겨찾기 순서 (DB 저장, 낮을수록 위쪽)
    /// </summary>
    private int _favoriteOrder;
    public int FavoriteOrder
    {
        get => _favoriteOrder;
        set { if (_favoriteOrder != value) { _favoriteOrder = value; OnPropertyChanged(nameof(FavoriteOrder)); } }
    }
}
