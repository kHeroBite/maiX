using System;
using mAIx.Services.Graph;

namespace mAIx.Models;

/// <summary>
/// SharePoint 휴지통 아이템 모델
/// </summary>
public class OneDriveRecycleBinItem
{
    /// <summary>
    /// 휴지통 아이템 ID (GUID)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 아이템 제목
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 파일/폴더 이름
    /// </summary>
    public string LeafName { get; set; } = string.Empty;

    /// <summary>
    /// 원래 경로 (디렉토리 이름)
    /// </summary>
    public string DirName { get; set; } = string.Empty;

    /// <summary>
    /// 삭제한 사용자
    /// </summary>
    public string DeletedByName { get; set; } = string.Empty;

    /// <summary>
    /// 삭제 날짜
    /// </summary>
    public DateTime DeletedDate { get; set; }

    /// <summary>
    /// 파일 크기 (바이트)
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 아이템 타입 (SharePoint RecycleBinItemType: 1=File, 2=Folder, 5=ListItem 등)
    /// </summary>
    public int ItemType { get; set; }

    /// <summary>
    /// 폴더 여부 (ItemType == 2는 Folder)
    /// </summary>
    public bool IsFolder => ItemType == 2;

    /// <summary>
    /// 원본 작성자
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// 파일 크기 표시용
    /// </summary>
    public string SizeDisplay => GraphOneDriveService.FormatFileSize(Size);

    /// <summary>
    /// 삭제 날짜 표시용
    /// </summary>
    public string DeletedDateDisplay => DeletedDate == DateTime.MinValue ? "" : DeletedDate.ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// 파일 타입 아이콘
    /// </summary>
    public string IconType
    {
        get
        {
            if (IsFolder) return "Folder";
            var ext = System.IO.Path.GetExtension(LeafName).ToLowerInvariant();
            return ext switch
            {
                ".doc" or ".docx" => "Word",
                ".xls" or ".xlsx" => "Excel",
                ".ppt" or ".pptx" => "PowerPoint",
                ".pdf" => "PDF",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "Image",
                ".mp4" or ".avi" or ".mov" or ".wmv" => "Video",
                ".mp3" or ".wav" or ".flac" => "Audio",
                ".zip" or ".rar" or ".7z" => "Archive",
                ".txt" => "Text",
                _ => "File"
            };
        }
    }
}
