using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using mAIx.Models;
using mAIx.Utils;

namespace mAIx.Services.Graph;

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

    #region JSON 헬퍼 메서드

    /// <summary>
    /// JsonElement에서 문자열 값을 안전하게 추출 (숫자도 문자열로 변환)
    /// </summary>
    private static string GetJsonString(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString() ?? "",
            System.Text.Json.JsonValueKind.Number => element.GetRawText(),
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            System.Text.Json.JsonValueKind.Null => "",
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// JsonElement에서 long 값을 안전하게 추출 (문자열도 파싱)
    /// </summary>
    private static long GetJsonLong(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var val) ? val : 0,
            System.Text.Json.JsonValueKind.String => long.TryParse(element.GetString(), out var val) ? val : 0,
            _ => 0
        };
    }

    /// <summary>
    /// JsonElement에서 int 값을 안전하게 추출 (문자열도 파싱)
    /// </summary>
    private static int GetJsonInt(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => element.TryGetInt32(out var val) ? val : 0,
            System.Text.Json.JsonValueKind.String => int.TryParse(element.GetString(), out var val) ? val : 0,
            _ => 0
        };
    }

    #endregion

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
                Folder = new Microsoft.Graph.Models.Folder()
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
    /// 최근 파일 조회
    /// </summary>
    /// <param name="top">조회할 최대 개수</param>
    /// <returns>최근 파일 목록</returns>
    public async Task<IEnumerable<DriveItem>> GetRecentItemsAsync(int top = 20)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var response = await client.Drives[driveId].Recent.GetAsRecentGetResponseAsync(config =>
            {
                config.QueryParameters.Top = top;
                // 소유자 정보 포함
                config.QueryParameters.Select = new[] 
                { 
                    "id", "name", "webUrl", "size", "createdDateTime", "lastModifiedDateTime",
                    "createdBy", "lastModifiedBy", "remoteItem", "file", "folder", "shared"
                };
            });

            Log4.Debug($"[OneDriveService] 최근 파일 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<DriveItem>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 최근 파일 조회 실패: {ex.Message}");
            return new List<DriveItem>();
        }
    }

    /// <summary>
    /// 나와 공유된 파일 조회
    /// </summary>
    /// <param name="top">조회할 최대 개수</param>
    /// <returns>공유된 파일 목록</returns>
    public async Task<IEnumerable<DriveItem>> GetSharedWithMeAsync(int top = 50)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            
            // SharedWithMe는 Drives[driveId].SharedWithMe 사용
            var response = await client.Drives[driveId].SharedWithMe.GetAsSharedWithMeGetResponseAsync(config =>
            {
                config.QueryParameters.Top = top;
            });

            var items = response?.Value ?? new List<DriveItem>();
            
            Log4.Debug($"[OneDriveService] 공유된 파일 {items.Count}개 조회");
            return items;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 공유된 파일 조회 실패: {ex.Message}");
            return new List<DriveItem>();
        }
    }


    /// <summary>
    /// 휴지통 아이템 조회 (SharePoint REST API 사용)
    /// </summary>
    public async Task<List<OneDriveRecycleBinItem>> GetTrashItemsAsync(int top = 100)
    {
        Log4.Info("[OneDriveService] GetTrashItemsAsync 시작");
        var result = new List<OneDriveRecycleBinItem>();

        try
        {
            var client = _authService.GetGraphClient();

            // 1. OneDrive WebUrl에서 SharePoint 사이트 URL 추출
            var drive = await client.Me.Drive.GetAsync();
            if (drive?.WebUrl == null)
            {
                Log4.Warn("[OneDriveService] Drive WebUrl을 가져올 수 없습니다.");
                return result;
            }

            // WebUrl 예: https://daiquesthk-my.sharepoint.com/personal/giro_kim_daiquestus_com/Documents
            // SharePoint 사이트 URL: https://daiquesthk-my.sharepoint.com/personal/giro_kim_daiquestus_com
            var webUrl = drive.WebUrl;
            var documentsIndex = webUrl.IndexOf("/Documents", StringComparison.OrdinalIgnoreCase);
            if (documentsIndex > 0)
            {
                webUrl = webUrl.Substring(0, documentsIndex);
            }

            Log4.Debug($"[OneDriveService] SharePoint 사이트 URL: {webUrl}");

            // 2. SharePoint REST API용 토큰 획득
            var accessToken = await _authService.GetSharePointAccessTokenAsync(webUrl);
            if (string.IsNullOrEmpty(accessToken))
            {
                Log4.Warn("[OneDriveService] SharePoint 토큰 획득 실패 - 휴지통 접근 불가");
                return result;
            }

            // 3. SharePoint REST API로 휴지통 조회
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var recyclebinUrl = $"{webUrl}/_api/web/recyclebin?$top={top}&$orderby=DeletedDate desc";
            Log4.Debug($"[OneDriveService] 휴지통 API 호출: {recyclebinUrl}");

            var response = await httpClient.GetAsync(recyclebinUrl);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log4.Error($"[OneDriveService] 휴지통 조회 실패: {response.StatusCode} - {errorContent}");
                return result;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);

            if (jsonDoc.RootElement.TryGetProperty("value", out var valueArray))
            {
                foreach (var item in valueArray.EnumerateArray())
                {
                    var recycleBinItem = new OneDriveRecycleBinItem
                    {
                        Id = item.TryGetProperty("Id", out var id) ? GetJsonString(id) : "",
                        Title = item.TryGetProperty("Title", out var title) ? GetJsonString(title) : "",
                        LeafName = item.TryGetProperty("LeafName", out var leafName) ? GetJsonString(leafName) : "",
                        DirName = item.TryGetProperty("DirName", out var dirName) ? GetJsonString(dirName) : "",
                        DeletedByName = item.TryGetProperty("DeletedByName", out var deletedBy) ? GetJsonString(deletedBy) : "",
                        DeletedDate = item.TryGetProperty("DeletedDate", out var deletedDate) ?
                            DateTime.TryParse(GetJsonString(deletedDate), out var dt) ? dt : DateTime.MinValue : DateTime.MinValue,
                        Size = item.TryGetProperty("Size", out var size) ? GetJsonLong(size) : 0,
                        ItemType = item.TryGetProperty("ItemType", out var itemType) ? GetJsonInt(itemType) : 0, // 0=File, 1=Folder
                        AuthorName = item.TryGetProperty("AuthorName", out var author) ? GetJsonString(author) : ""
                    };
                    result.Add(recycleBinItem);
                }
            }

            Log4.Info($"[OneDriveService] 휴지통 아이템 {result.Count}개 조회 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 휴지통 조회 실패: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 휴지통 아이템 복원
    /// </summary>
    public async Task<bool> RestoreTrashItemAsync(string itemId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var drive = await client.Me.Drive.GetAsync();
            if (drive?.WebUrl == null) return false;

            var webUrl = drive.WebUrl;
            var documentsIndex = webUrl.IndexOf("/Documents", StringComparison.OrdinalIgnoreCase);
            if (documentsIndex > 0)
            {
                webUrl = webUrl.Substring(0, documentsIndex);
            }

            var accessToken = await _authService.GetSharePointAccessTokenAsync(webUrl);
            if (string.IsNullOrEmpty(accessToken))
            {
                Log4.Warn("[OneDriveService] SharePoint 토큰 획득 실패 - 복원 불가");
                return false;
            }

            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // 휴지통 아이템 복원 API
            var restoreUrl = $"{webUrl}/_api/web/recyclebin('{itemId}')/restore()";
            var response = await httpClient.PostAsync(restoreUrl, null);

            if (response.IsSuccessStatusCode)
            {
                Log4.Info($"[OneDriveService] 휴지통 아이템 복원 성공: {itemId}");
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Log4.Error($"[OneDriveService] 휴지통 아이템 복원 실패: {response.StatusCode} - {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 휴지통 아이템 복원 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 휴지통 아이템 영구 삭제
    /// </summary>
    public async Task<bool> DeleteTrashItemPermanentlyAsync(string itemId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var drive = await client.Me.Drive.GetAsync();
            if (drive?.WebUrl == null) return false;

            var webUrl = drive.WebUrl;
            var documentsIndex = webUrl.IndexOf("/Documents", StringComparison.OrdinalIgnoreCase);
            if (documentsIndex > 0)
            {
                webUrl = webUrl.Substring(0, documentsIndex);
            }

            var accessToken = await _authService.GetSharePointAccessTokenAsync(webUrl);
            if (string.IsNullOrEmpty(accessToken))
            {
                Log4.Warn("[OneDriveService] SharePoint 토큰 획득 실패 - 삭제 불가");
                return false;
            }

            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // 휴지통 아이템 영구 삭제 API
            var deleteUrl = $"{webUrl}/_api/web/recyclebin('{itemId}')/deleteObject()";
            var response = await httpClient.PostAsync(deleteUrl, null);

            if (response.IsSuccessStatusCode)
            {
                Log4.Info($"[OneDriveService] 휴지통 아이템 영구 삭제 성공: {itemId}");
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Log4.Error($"[OneDriveService] 휴지통 아이템 영구 삭제 실패: {response.StatusCode} - {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 휴지통 아이템 영구 삭제 실패: {ex.Message}");
            return false;
        }
    }


    /// <summary>
    /// 파일에 대한 공유 링크를 생성합니다.
    /// </summary>
    public async Task<string?> CreateShareLinkAsync(string itemId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            
            var requestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
            {
                Type = "view",
                Scope = "organization"
            };
            
            var permission = await client.Drives[driveId].Items[itemId].CreateLink.PostAsync(requestBody);
            
            Log4.Debug($"[OneDriveService] 공유 링크 생성 완료: {permission?.Link?.WebUrl}");
            return permission?.Link?.WebUrl;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 공유 링크 생성 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 미디어 파일(이미지, 비디오)을 검색합니다.
    /// </summary>
    public async Task<IEnumerable<DriveItem>> GetMediaFilesAsync(int top = 200)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var allMediaItems = new List<DriveItem>();
            
            // 1. 최근 파일에서 미디어 파일 필터링
            try
            {
                var recentItems = await GetRecentItemsAsync(200);
                var recentMedia = recentItems.Where(i => IsMediaFile(i.Name ?? string.Empty)).ToList();
                allMediaItems.AddRange(recentMedia);
                Log4.Debug($"[OneDriveService] 최근 파일에서 미디어: {recentMedia.Count}개");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] 최근 미디어 조회 실패: {ex.Message}");
            }
            
            // 2. 미디어 관련 폴더들 재귀적 탐색
            var mediaFolderNames = new[] { "그림", "Pictures", "사진", "바탕 화면", "Desktop", "스크린샷", "Screenshots", "카메라 롤", "Camera Roll" };
            try
            {
                var rootItems = await client.Drives[driveId].Items["root"].Children.GetAsync();
                var mediaFolders = rootItems?.Value?.Where(i => 
                    i.Folder != null && 
                    mediaFolderNames.Any(name => i.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true))
                    .ToList() ?? new List<DriveItem>();
                
                foreach (var folder in mediaFolders)
                {
                    await CollectMediaFromFolderAsync(client, driveId, folder.Id!, allMediaItems, depth: 0, maxDepth: 3);
                }
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] 미디어 폴더 탐색 실패: {ex.Message}");
            }
            
            // 3. Insights/Used에서 미디어 파일 필터링
            try
            {
                var usedInsights = await GetUsedInsightsAsync(200);
                var usedMedia = usedInsights.Where(i => IsMediaFile(i.Name ?? string.Empty)).ToList();
                allMediaItems.AddRange(usedMedia);
                Log4.Debug($"[OneDriveService] Insights/Used 미디어: {usedMedia.Count}개");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] Insights 미디어 조회 실패: {ex.Message}");
            }
            
            // 중복 제거
            var uniqueItems = allMediaItems
                .Where(i => !string.IsNullOrEmpty(i.Id))
                .GroupBy(i => i.Id)
                .Select(g => g.First())
                .OrderByDescending(i => i.CreatedDateTime ?? i.LastModifiedDateTime)
                .Take(100)
                .ToList();
            
            Log4.Info($"[OneDriveService] 미디어 파일 수집: {allMediaItems.Count}개, 중복 제거 후: {uniqueItems.Count}개");
            
            // 썸네일이 없는 아이템들에 대해 병렬로 썸네일 가져오기
            var itemsNeedingThumbnails = uniqueItems.Where(i => i.Thumbnails == null || !i.Thumbnails.Any()).ToList();
            if (itemsNeedingThumbnails.Any())
            {
                Log4.Debug($"[OneDriveService] 썸네일 필요한 아이템: {itemsNeedingThumbnails.Count}개");
                
                var thumbnailTasks = itemsNeedingThumbnails.Take(50).Select(async item =>
                {
                    try
                    {
                        var itemWithThumbnail = await client.Drives[driveId].Items[item.Id].GetAsync(config =>
                        {
                            config.QueryParameters.Expand = new[] { "thumbnails" };
                        });
                        return (item.Id, itemWithThumbnail);
                    }
                    catch
                    {
                        return (item.Id, item);
                    }
                });
                
                var results = await Task.WhenAll(thumbnailTasks);
                
                // 썸네일을 가져온 아이템으로 교체
                var thumbnailDict = results.Where(r => r.Item2 != null).ToDictionary(r => r.Item1!, r => r.Item2!);
                for (int i = 0; i < uniqueItems.Count; i++)
                {
                    if (uniqueItems[i].Id != null && thumbnailDict.TryGetValue(uniqueItems[i].Id!, out var itemWithThumb))
                    {
                        uniqueItems[i] = itemWithThumb;
                    }
                }
            }
            
            Log4.Info($"[OneDriveService] 미디어 파일 총 {uniqueItems.Count}개 (썸네일 처리 완료)");
            return uniqueItems;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 미디어 파일 조회 실패: {ex.Message}");
            return new List<DriveItem>();
        }
    }


    /// <summary>
    /// 폴더에서 미디어 파일을 재귀적으로 수집
    /// </summary>
    private async Task CollectMediaFromFolderAsync(GraphServiceClient client, string driveId, string folderId, List<DriveItem> allMediaItems, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        
        try
        {
            var folderItems = await client.Drives[driveId].Items[folderId].Children.GetAsync(config =>
            {
                config.QueryParameters.Top = 200;
                config.QueryParameters.Expand = new[] { "thumbnails" };
            });
            
            if (folderItems?.Value == null) return;
            
            foreach (var item in folderItems.Value)
            {
                if (item.File != null && IsMediaFile(item.Name ?? string.Empty))
                {
                    allMediaItems.Add(item);
                }
                else if (item.Folder != null && depth < maxDepth)
                {
                    // 하위 폴더 재귀 탐색
                    await CollectMediaFromFolderAsync(client, driveId, item.Id!, allMediaItems, depth + 1, maxDepth);
                }
            }
            
            Log4.Debug($"[OneDriveService] 폴더 탐색 (depth={depth}): {folderItems.Value.Count}개 아이템");
        }
        catch (Exception ex)
        {
            Log4.Warn($"[OneDriveService] 폴더 탐색 실패 (folderId={folderId}): {ex.Message}");
        }
    }


    /// <summary>
    /// 파일명이 미디어 파일인지 확인
    /// </summary>
    private bool IsMediaFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var mediaExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".mp4", ".avi", ".mov", ".wmv", ".mkv" };
        return mediaExtensions.Contains(extension);
    }

    /// <summary>
    /// 공유된 파일을 소유자별로 그룹화하여 반환합니다.
    /// </summary>
    public async Task<Dictionary<string, List<DriveItem>>> GetSharedItemsByPersonAsync(int top = 200)
    {
        try
        {
            var result = new Dictionary<string, List<DriveItem>>();
            var allItems = new List<DriveItem>();
            
            // 1. SharedWithMe (직접 공유된 파일) - 최대치로
            try
            {
                var sharedItems = await GetSharedWithMeAsync(200);
                allItems.AddRange(sharedItems);
                Log4.Debug($"[OneDriveService] SharedWithMe: {sharedItems.Count()}개");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] SharedWithMe 조회 실패: {ex.Message}");
            }
            
            // 2. Recent (최근 접근한 파일)
            try
            {
                var recentItems = await GetRecentItemsAsync(200);
                allItems.AddRange(recentItems);
                Log4.Debug($"[OneDriveService] Recent: {recentItems.Count()}개");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] Recent 조회 실패: {ex.Message}");
            }
            
            // 3. Insights/Used (사용한 파일들)
            try
            {
                var usedInsights = await GetUsedInsightsAsync(200);
                allItems.AddRange(usedInsights);
                Log4.Debug($"[OneDriveService] Insights/Used: {usedInsights.Count()}개");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] Insights/Used 조회 실패: {ex.Message}");
            }
            
            // 4. Insights/Shared (공유된 파일들)
            try
            {
                var sharedInsights = await GetSharedInsightsAsync(200);
                allItems.AddRange(sharedInsights);
                Log4.Debug($"[OneDriveService] Insights/Shared: {sharedInsights.Count()}개");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] Insights/Shared 조회 실패: {ex.Message}");
            }
            
            // 5. Insights/Trending (인기 파일들)
            try
            {
                var trendingInsights = await GetTrendingInsightsAsync(200);
                allItems.AddRange(trendingInsights);
                Log4.Debug($"[OneDriveService] Insights/Trending: {trendingInsights.Count()}개");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneDriveService] Insights/Trending 조회 실패: {ex.Message}");
            }
            
            // 중복 제거 (ID 기준, null ID 제외)
            var uniqueItems = allItems
                .Where(i => !string.IsNullOrEmpty(i.Id))
                .GroupBy(i => i.Id)
                .Select(g => g.First())
                .ToList();
            
            Log4.Debug($"[OneDriveService] 전체 수집: {allItems.Count}개, 중복 제거 후: {uniqueItems.Count}개");
            
            foreach (var item in uniqueItems)
            {
                string ownerName = ExtractOwnerName(item);

                // 자기 자신의 파일, 알 수 없음 제외
                if (ownerName == "나" || ownerName == "알 수 없음" || string.IsNullOrEmpty(ownerName))
                    continue;

                // 조직명/팀명/사이트명 패턴 제외 (사람 이름만 허용)
                if (!IsPersonName(ownerName))
                    continue;

                if (!result.ContainsKey(ownerName))
                {
                    result[ownerName] = new List<DriveItem>();
                }
                result[ownerName].Add(item);
            }
            
            Log4.Info($"[OneDriveService] 사람별 파일: {result.Count}명, 총 {result.Values.Sum(v => v.Count)}개 파일");
            return result;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 사람별 공유 파일 조회 실패: {ex.Message}");
            return new Dictionary<string, List<DriveItem>>();
        }
    }

    /// <summary>
    /// Insights/Used API - 사용한 파일들
    /// </summary>
    private async Task<IEnumerable<DriveItem>> GetUsedInsightsAsync(int top = 100)
    {
        var client = _authService.GetGraphClient();
        var result = new List<DriveItem>();
        
        var response = await client.Me.Insights.Used.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
            config.QueryParameters.Orderby = new[] { "lastUsed/lastAccessedDateTime desc" };
        });
        
        if (response?.Value != null)
        {
            foreach (var insight in response.Value)
            {
                if (insight.ResourceReference?.Id != null)
                {
                    // DriveItem으로 변환
                    var driveItem = new DriveItem
                    {
                        Id = insight.ResourceReference.Id,
                        Name = insight.ResourceVisualization?.Title ?? "알 수 없음",
                        WebUrl = insight.ResourceReference.WebUrl,
                        // LastUsed에서 사용자 정보를 가져올 수 없으므로 ResourceVisualization 사용
                        CreatedBy = new IdentitySet
                        {
                            User = new Identity
                            {
                                DisplayName = insight.ResourceVisualization?.ContainerDisplayName
                            }
                        }
                    };
                    result.Add(driveItem);
                }
            }
        }
        
        return result;
    }

    /// <summary>
    /// Insights/Shared API - 공유된 파일들
    /// </summary>
    private async Task<IEnumerable<DriveItem>> GetSharedInsightsAsync(int top = 200)
    {
        var client = _authService.GetGraphClient();
        var result = new List<DriveItem>();
        
        var response = await client.Me.Insights.Shared.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
            // Orderby 제거 - LastShared 속성은 필터링 지원하지 않음
        });
        
        if (response?.Value != null)
        {
            foreach (var insight in response.Value)
            {
                if (insight.ResourceReference?.Id != null)
                {
                    var driveItem = new DriveItem
                    {
                        Id = insight.ResourceReference.Id,
                        Name = insight.ResourceVisualization?.Title ?? "알 수 없음",
                        WebUrl = insight.ResourceReference.WebUrl,
                        // SharedBy에서 사용자 정보 가져오기
                        CreatedBy = new IdentitySet
                        {
                            User = new Identity
                            {
                                DisplayName = insight.LastShared?.SharedBy?.DisplayName ?? insight.ResourceVisualization?.ContainerDisplayName
                            }
                        }
                    };
                    result.Add(driveItem);
                }
            }
        }
        
        return result;
    }


    /// <summary>
    /// Insights/Trending API로 트렌딩 파일 조회
    /// </summary>
    private async Task<IEnumerable<DriveItem>> GetTrendingInsightsAsync(int top = 200)
    {
        var client = _authService.GetGraphClient();
        var result = new List<DriveItem>();
        
        try
        {
            var response = await client.Me.Insights.Trending.GetAsync(config =>
            {
                config.QueryParameters.Top = top;
            });
            
            if (response?.Value != null)
            {
                foreach (var insight in response.Value)
                {
                    if (insight.ResourceReference?.Id != null)
                    {
                        var driveItem = new DriveItem
                        {
                            Id = insight.ResourceReference.Id,
                            Name = insight.ResourceVisualization?.Title ?? "알 수 없음",
                            WebUrl = insight.ResourceReference.WebUrl,
                            CreatedBy = new IdentitySet
                            {
                                User = new Identity
                                {
                                    DisplayName = insight.ResourceVisualization?.ContainerDisplayName ?? insight.LastModifiedDateTime?.ToString("yyyy-MM-dd")
                                }
                            }
                        };
                        result.Add(driveItem);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[OneDriveService] Trending insights 조회 실패: {ex.Message}");
        }
        
        return result;
    }

    /// <summary>
    /// DriveItem에서 소유자 이름 추출
    /// </summary>
    private string ExtractOwnerName(DriveItem item)
    {
        // 1. RemoteItem의 CreatedBy (공유된 파일)
        if (!string.IsNullOrEmpty(item.RemoteItem?.CreatedBy?.User?.DisplayName))
            return item.RemoteItem.CreatedBy.User.DisplayName;
        
        // 2. RemoteItem의 LastModifiedBy
        if (!string.IsNullOrEmpty(item.RemoteItem?.LastModifiedBy?.User?.DisplayName))
            return item.RemoteItem.LastModifiedBy.User.DisplayName;
        
        // 3. Shared의 SharedBy
        if (!string.IsNullOrEmpty(item.Shared?.SharedBy?.User?.DisplayName))
            return item.Shared.SharedBy.User.DisplayName;
        
        // 4. Shared의 Owner
        if (!string.IsNullOrEmpty(item.Shared?.Owner?.User?.DisplayName))
            return item.Shared.Owner.User.DisplayName;
        
        // 5. 직접 CreatedBy
        if (!string.IsNullOrEmpty(item.CreatedBy?.User?.DisplayName))
            return item.CreatedBy.User.DisplayName;
        
        // 6. LastModifiedBy
        if (!string.IsNullOrEmpty(item.LastModifiedBy?.User?.DisplayName))
            return item.LastModifiedBy.User.DisplayName;
        
        return "알 수 없음";
    }


    /// <summary>
    /// 문자열이 사람 이름인지 확인 (조직명/팀명/사이트명 제외)
    /// </summary>
    private bool IsPersonName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // 한글 이름 패턴 (2-4글자, 공백 없음)
        // 예: 김기로, 장영환, 고종구
        var koreanNamePattern = @"^[가-힣]{2,4}$";
        if (System.Text.RegularExpressions.Regex.IsMatch(name, koreanNamePattern))
            return true;

        // 영어 이름 패턴 (First Last 또는 First Middle Last)
        // 예: John Smith, Kim Gi Ro
        var englishNamePattern = @"^[A-Za-z]+(\s+[A-Za-z]+){1,2}$";
        if (System.Text.RegularExpressions.Regex.IsMatch(name, englishNamePattern))
            return true;

        // 조직명/팀명/사이트명 패턴 제외
        var excludePatterns = new[]
        {
            "사업", "본부", "그룹", "부문", "팀", "센터", "실",  // 조직 키워드
            "SharePoint", "OneDrive", "Teams",                    // Microsoft 서비스
            " - ",                                                  // 구분자 포함
            "General", "Documents", "Files"                        // 일반 폴더명
        };

        foreach (var pattern in excludePatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 이름이 너무 길면 조직명일 가능성 높음 (10글자 초과)
        if (name.Length > 15)
            return false;

        // 기본적으로 허용 (위 패턴에 해당하지 않으면)
        return true;
    }

    /// <summary>
    /// 파일 버전 목록 조회
    /// </summary>
    public async Task<List<DriveItemVersion>> GetFileVersionsAsync(string itemId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var response = await client.Drives[driveId].Items[itemId].Versions.GetAsync();
            return response?.Value?.ToList() ?? new List<DriveItemVersion>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 버전 목록 조회 실패: {ex.Message}");
            return new List<DriveItemVersion>();
        }
    }

    /// <summary>
    /// 버전 복원
    /// </summary>
    public async Task<bool> RestoreVersionAsync(string itemId, string versionId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            await client.Drives[driveId].Items[itemId].Versions[versionId].RestoreVersion.PostAsync();
            Log4.Info($"[OneDriveService] 버전 복원 완료: {itemId} → {versionId}");
            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 버전 복원 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 공유 링크 생성 (유형/만료일 지정)
    /// </summary>
    public async Task<Permission?> CreateShareLinkWithOptionsAsync(string itemId, string type = "view", DateTimeOffset? expiry = null)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();

            var requestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
            {
                Type = type,
                Scope = "organization",
                ExpirationDateTime = expiry
            };

            var permission = await client.Drives[driveId].Items[itemId].CreateLink.PostAsync(requestBody);
            Log4.Debug($"[OneDriveService] 공유 링크 생성: type={type}, url={permission?.Link?.WebUrl}");
            return permission;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 공유 링크 생성 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 공유 권한 목록 조회
    /// </summary>
    public async Task<List<Permission>> GetSharePermissionsAsync(string itemId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var response = await client.Drives[driveId].Items[itemId].Permissions.GetAsync();
            return response?.Value?.ToList() ?? new List<Permission>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 공유 권한 조회 실패: {ex.Message}");
            return new List<Permission>();
        }
    }

    /// <summary>
    /// 대용량 파일 청크 업로드
    /// </summary>
    public async Task<DriveItem?> UploadLargeFileAsync(Stream content, string? parentId, string fileName, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();

            // 업로드 세션 생성
            var uploadSessionBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    Name = fileName,
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", "rename" }
                    }
                }
            };

            string itemPath = string.IsNullOrEmpty(parentId) ? $"root:/{fileName}:" : $"items/{parentId}:/{fileName}:";

            var uploadSession = await client.Drives[driveId]
                .Items[string.IsNullOrEmpty(parentId) ? "root" : parentId]
                .ItemWithPath(fileName)
                .CreateUploadSession
                .PostAsync(uploadSessionBody, cancellationToken: ct);

            if (uploadSession?.UploadUrl == null)
            {
                Log4.Error("[OneDriveService] 업로드 세션 생성 실패");
                return null;
            }

            // 5MB 청크로 업로드
            const int chunkSize = 5 * 1024 * 1024;
            long totalLength = content.Length;
            long uploaded = 0;
            int retryCount = 0;
            const int maxRetries = 3;

            using var httpClient = new System.Net.Http.HttpClient();

            while (uploaded < totalLength)
            {
                ct.ThrowIfCancellationRequested();

                int currentChunkSize = (int)Math.Min(chunkSize, totalLength - uploaded);
                byte[] buffer = new byte[currentChunkSize];
                int bytesRead = await content.ReadAsync(buffer, 0, currentChunkSize, ct);

                if (bytesRead == 0) break;

                using var chunkContent = new System.Net.Http.ByteArrayContent(buffer, 0, bytesRead);
                chunkContent.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(uploaded, uploaded + bytesRead - 1, totalLength);
                chunkContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                try
                {
                    var response = await httpClient.PutAsync(uploadSession.UploadUrl, chunkContent, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        uploaded += bytesRead;
                        progress?.Report((double)uploaded / totalLength * 100);
                        retryCount = 0;

                        // 업로드 완료 (200 또는 201 반환)
                        if (response.StatusCode == System.Net.HttpStatusCode.OK || response.StatusCode == System.Net.HttpStatusCode.Created)
                        {
                            var json = await response.Content.ReadAsStringAsync(ct);
                            Log4.Info($"[OneDriveService] 대용량 파일 업로드 완료: {fileName}");
                            // 완료 후 아이템 정보 다시 가져오기
                            var driveItem = await GetItemAsync(parentId ?? "root");
                            return driveItem;
                        }
                    }
                    else if (retryCount < maxRetries)
                    {
                        retryCount++;
                        content.Position = uploaded; // 다시 같은 위치에서 시도
                        Log4.Warn($"[OneDriveService] 청크 업로드 재시도 {retryCount}/{maxRetries}");
                        await Task.Delay(1000 * retryCount, ct);
                    }
                    else
                    {
                        Log4.Error($"[OneDriveService] 청크 업로드 최대 재시도 초과");
                        return null;
                    }
                }
                catch (System.Net.Http.HttpRequestException ex) when (retryCount < maxRetries)
                {
                    retryCount++;
                    content.Position = uploaded;
                    Log4.Warn($"[OneDriveService] 청크 업로드 네트워크 오류, 재시도 {retryCount}: {ex.Message}");
                    await Task.Delay(1000 * retryCount, ct);
                }
            }

            progress?.Report(100);
            Log4.Info($"[OneDriveService] 대용량 파일 업로드 완료: {fileName} ({FormatFileSize(totalLength)})");
            return null; // 세션 완료 응답에서 DriveItem을 이미 반환했어야 함
        }
        catch (OperationCanceledException)
        {
            Log4.Warn($"[OneDriveService] 파일 업로드 취소: {fileName}");
            throw;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneDriveService] 대용량 파일 업로드 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 파일 미리보기 URL 가져오기 (Office Online 등)
    /// </summary>
    public async Task<string?> GetPreviewUrlAsync(string itemId)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var preview = await client.Drives[driveId].Items[itemId].Preview.PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.Preview.PreviewPostRequestBody());
            return preview?.GetUrl;
        }
        catch (Exception ex)
        {
            Log4.Debug($"[OneDriveService] 미리보기 URL 조회 실패 (미지원 파일일 수 있음): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 파일 썸네일 URL 가져오기
    /// </summary>
    public async Task<string?> GetThumbnailUrlAsync(string itemId, string size = "large")
    {
        try
        {
            var client = _authService.GetGraphClient();
            var driveId = await GetDriveIdAsync();
            var thumbnails = await client.Drives[driveId].Items[itemId].Thumbnails.GetAsync();
            var thumb = thumbnails?.Value?.FirstOrDefault();
            return size switch
            {
                "small" => thumb?.Small?.Url,
                "medium" => thumb?.Medium?.Url,
                _ => thumb?.Large?.Url
            };
        }
        catch (Exception ex)
        {
            Log4.Debug($"[OneDriveService] 썸네일 조회 실패: {ex.Message}");
            return null;
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
