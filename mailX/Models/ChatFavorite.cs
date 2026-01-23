using System;
using System.ComponentModel.DataAnnotations;

namespace mailX.Models;

/// <summary>
/// 채팅 즐겨찾기 정보 (로컬 전용 - Graph API 미지원)
/// </summary>
public class ChatFavorite
{
    /// <summary>
    /// 채팅방 ID (Primary Key)
    /// </summary>
    [Key]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// 계정 이메일
    /// </summary>
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// 표시 이름 (캐시용)
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 채팅 유형 (OneOnOne, Group, Meeting)
    /// </summary>
    public string? ChatType { get; set; }

    /// <summary>
    /// 즐겨찾기 추가 시간
    /// </summary>
    public DateTime FavoritedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 정렬 순서 (낮을수록 상단)
    /// </summary>
    public int SortOrder { get; set; }
}
