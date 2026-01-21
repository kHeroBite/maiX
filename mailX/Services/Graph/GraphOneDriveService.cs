using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mailX.Utils;

namespace mailX.Services.Graph;

/// <summary>
/// Microsoft OneDrive 연동 서비스
/// </summary>
public class GraphOneDriveService
{
    private readonly GraphAuthService _authService;
    private string? _driveId;

    public GraphOneDriveService(GraphAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// 사용자의 Drive ID 가져오기
    /// </summary>
    private async Task<string> GetDriveIdAsync()
    {
        if (!string.IsNullOrEmpty(_driveId))
            return _driveId;

        var client = _authService.GetGraphClient();
        var drive = await client.Me.Drive.GetAsync();
        _driveId = drive?.Id ?? throw new InvalidOperationException("Drive ID를 가져올 수 없습니다.");
        return _driveId;
    }

    /// <summary>
    /// 루트 폴더의 아이템 목록 조회
    /// </summary>
    /// <returns>파일/폴더 목록</returns>
    public async Task<IEnumerable<DriveItem>> GetRootItemsAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var response = await client.Drives[driveId].Items["root"].Children.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = new[] { "name" };
            });

            Log4.Debug($"[OneDriveService] 루트 아이템 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<DriveItem>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 루트 아이템 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 특정 폴더의 아이템 목록 조회
    /// </summary>
    /// <param name="folderId">폴더 ID</param>
    /// <returns>파일/폴더 목록</returns>
    public async Task<IEnumerable<DriveItem>> GetFolderItemsAsync(string folderId)
    {
        if (string.IsNullOrEmpty(folderId))
            throw new ArgumentNullException(nameof(folderId));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var response = await client.Drives[driveId].Items[folderId].Children.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = new[] { "name" };
            });

            Log4.Debug($"[OneDriveService] 폴더 {folderId} 아이템 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<DriveItem>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 폴더 아이템 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 경로로 폴더 아이템 조회
    /// </summary>
    /// <param name="path">폴더 경로 (예: "/Documents/Work")</param>
    /// <returns>파일/폴더 목록</returns>
    public async Task<IEnumerable<DriveItem>> GetItemsByPathAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
            return await GetRootItemsAsync();

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var response = await client.Drives[driveId].Items["root"].ItemWithPath(path).Children.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = new[] { "name" };
            });

            Log4.Debug($"[OneDriveService] 경로 '{path}' 아이템 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<DriveItem>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 경로 아이템 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 드라이브 정보 조회
    /// </summary>
    /// <returns>드라이브 정보</returns>
    public async Task<Drive?> GetDriveInfoAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var drive = await client.Me.Drive.GetAsync();

            Log4.Debug($"[OneDriveService] 드라이브 정보 조회: 사용 {drive?.Quota?.Used ?? 0} / 전체 {drive?.Quota?.Total ?? 0}");
            return drive;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 드라이브 정보 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 새 폴더 생성
    /// </summary>
    /// <param name="parentId">부모 폴더 ID (null이면 루트)</param>
    /// <param name="folderName">폴더 이름</param>
    /// <returns>생성된 폴더</returns>
    public async Task<DriveItem?> CreateFolderAsync(string? parentId, string folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            throw new ArgumentNullException(nameof(folderName));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();

            var newFolder = new DriveItem
            {
                Name = folderName,
                Folder = new Folder()
            };

            DriveItem? response;
            if (string.IsNullOrEmpty(parentId))
            {
                response = await client.Drives[driveId].Items["root"].Children.PostAsync(newFolder);
            }
            else
            {
                response = await client.Drives[driveId].Items[parentId].Children.PostAsync(newFolder);
            }

            Log4.Info($"[OneDriveService] 폴더 생성 완료: {folderName}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 폴더 생성 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 파일 업로드 (소용량, 4MB 이하)
    /// </summary>
    /// <param name="parentId">부모 폴더 ID (null이면 루트)</param>
    /// <param name="fileName">파일 이름</param>
    /// <param name="content">파일 내용</param>
    /// <returns>업로드된 파일</returns>
    public async Task<DriveItem?> UploadSmallFileAsync(string? parentId, string fileName, Stream content)
    {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentNullException(nameof(fileName));
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();

            DriveItem? response;
            if (string.IsNullOrEmpty(parentId))
            {
                response = await client.Drives[driveId].Items["root"].ItemWithPath(fileName).Content.PutAsync(content);
            }
            else
            {
                response = await client.Drives[driveId].Items[parentId].ItemWithPath(fileName).Content.PutAsync(content);
            }

            Log4.Info($"[OneDriveService] 파일 업로드 완료: {fileName}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 파일 업로드 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 파일 다운로드
    /// </summary>
    /// <param name="itemId">파일 ID</param>
    /// <returns>파일 스트림</returns>
    public async Task<Stream?> DownloadFileAsync(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new ArgumentNullException(nameof(itemId));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var content = await client.Drives[driveId].Items[itemId].Content.GetAsync();

            Log4.Debug($"[OneDriveService] 파일 다운로드 시작: {itemId}");
            return content;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 파일 다운로드 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 아이템 삭제
    /// </summary>
    /// <param name="itemId">아이템 ID</param>
    /// <returns>성공 여부</returns>
    public async Task<bool> DeleteItemAsync(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new ArgumentNullException(nameof(itemId));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            await client.Drives[driveId].Items[itemId].DeleteAsync();

            Log4.Info($"[OneDriveService] 아이템 삭제 완료: {itemId}");
            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 아이템 삭제 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 아이템 이름 변경
    /// </summary>
    /// <param name="itemId">아이템 ID</param>
    /// <param name="newName">새 이름</param>
    /// <returns>업데이트된 아이템</returns>
    public async Task<DriveItem?> RenameItemAsync(string itemId, string newName)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new ArgumentNullException(nameof(itemId));
        if (string.IsNullOrEmpty(newName))
            throw new ArgumentNullException(nameof(newName));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();

            var updateItem = new DriveItem
            {
                Name = newName
            };

            var response = await client.Drives[driveId].Items[itemId].PatchAsync(updateItem);

            Log4.Info($"[OneDriveService] 아이템 이름 변경 완료: {newName}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 아이템 이름 변경 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 아이템 이동
    /// </summary>
    /// <param name="itemId">아이템 ID</param>
    /// <param name="newParentId">새 부모 폴더 ID</param>
    /// <returns>이동된 아이템</returns>
    public async Task<DriveItem?> MoveItemAsync(string itemId, string newParentId)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new ArgumentNullException(nameof(itemId));
        if (string.IsNullOrEmpty(newParentId))
            throw new ArgumentNullException(nameof(newParentId));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();

            var updateItem = new DriveItem
            {
                ParentReference = new ItemReference
                {
                    Id = newParentId
                }
            };

            var response = await client.Drives[driveId].Items[itemId].PatchAsync(updateItem);

            Log4.Info($"[OneDriveService] 아이템 이동 완료: {itemId} -> {newParentId}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 아이템 이동 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 아이템 복사
    /// </summary>
    /// <param name="itemId">아이템 ID</param>
    /// <param name="newParentId">대상 폴더 ID</param>
    /// <param name="newName">새 이름 (선택)</param>
    /// <returns>복사 작업 URL</returns>
    public async Task<string?> CopyItemAsync(string itemId, string newParentId, string? newName = null)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new ArgumentNullException(nameof(itemId));
        if (string.IsNullOrEmpty(newParentId))
            throw new ArgumentNullException(nameof(newParentId));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();

            var copyRequest = new Microsoft.Graph.Drives.Item.Items.Item.Copy.CopyPostRequestBody
            {
                ParentReference = new ItemReference
                {
                    Id = newParentId
                },
                Name = newName
            };

            var response = await client.Drives[driveId].Items[itemId].Copy.PostAsync(copyRequest);

            Log4.Info($"[OneDriveService] 아이템 복사 시작: {itemId}");
            return response?.Id;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 아이템 복사 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 파일 검색
    /// </summary>
    /// <param name="query">검색어</param>
    /// <returns>검색 결과</returns>
    public async Task<IEnumerable<DriveItem>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<DriveItem>();

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var response = await client.Drives[driveId].Items["root"].SearchWithQ(query).GetAsync(config =>
            {
                config.QueryParameters.Top = 50;
            });

            Log4.Debug($"[OneDriveService] 검색 '{query}': {response?.Value?.Count ?? 0}개 발견");
            return response?.Value ?? new List<DriveItem>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 검색 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 아이템 정보 조회
    /// </summary>
    /// <param name="itemId">아이템 ID</param>
    /// <returns>아이템 정보</returns>
    public async Task<DriveItem?> GetItemAsync(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new ArgumentNullException(nameof(itemId));

        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var item = await client.Drives[driveId].Items[itemId].GetAsync();

            return item;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 아이템 정보 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 파일 크기 포맷팅
    /// </summary>
    public static string FormatFileSize(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value == 0)
            return "0 B";

        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes.Value;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
