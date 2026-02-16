using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MaiX.Data;
using MaiX.Utils;
using Serilog;

// 모호한 참조 해결을 위한 별칭
using MaiXTodo = MaiX.Models.Todo;
using MaiXEmail = MaiX.Models.Email;
using MaiXOneNotePage = MaiX.Models.OneNotePage;

namespace MaiX.Services.Graph;

/// <summary>
/// Microsoft OneNote 연동 서비스
/// </summary>
public class GraphOneNoteService
{
    private readonly GraphAuthService _authService;
    private readonly MaiXDbContext _dbContext;
    private readonly ILogger _logger;

    public GraphOneNoteService(GraphAuthService authService, MaiXDbContext dbContext)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = Log.ForContext<GraphOneNoteService>();
    }

    /// <summary>
    /// 개인 노트북 목록 조회
    /// </summary>
    /// <returns>노트북 목록</returns>
    public async Task<IEnumerable<Notebook>> GetNotebooksAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            Log4.Debug("[GetNotebooksAsync] Graph 클라이언트 획득 완료");
            
            var response = await client.Me.Onenote.Notebooks.GetAsync();

            var count = response?.Value?.Count ?? 0;
            Log4.Info($"[GetNotebooksAsync] 개인 노트북 {count}개 조회");
            return response?.Value ?? new List<Notebook>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[GetNotebooksAsync] 노트북 목록 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 사용자가 멤버인 그룹 목록 조회
    /// </summary>
    /// <returns>그룹 목록</returns>
    public async Task<IEnumerable<Microsoft.Graph.Models.Group>> GetUserGroupsAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();

            // Teams에서 사용자가 속한 팀 가져오기 (Teams 팀 = Microsoft 365 그룹)
            var teamsResponse = await client.Me.JoinedTeams.GetAsync();

            var teams = teamsResponse?.Value ?? new List<Microsoft.Graph.Models.Team>();

            // Team을 Group으로 변환 (팀 ID = 그룹 ID)
            var result = teams.Select(team => new Microsoft.Graph.Models.Group
            {
                Id = team.Id,
                DisplayName = team.DisplayName,
                Description = team.Description,
                GroupTypes = new List<string> { "Unified" }  // Teams 팀은 항상 Microsoft 365 그룹
            }).ToList();

            _logger.Debug("사용자가 속한 Teams 팀 {Count}개 발견", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "사용자 그룹 목록 조회 실패");
            return new List<Microsoft.Graph.Models.Group>();
        }
    }

    /// <summary>
    /// 그룹의 노트북 목록 조회
    /// </summary>
    /// <param name="groupId">그룹 ID</param>
    /// <returns>노트북 목록</returns>
    public async Task<IEnumerable<Notebook>> GetGroupNotebooksAsync(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return new List<Notebook>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Groups[groupId].Onenote.Notebooks.GetAsync();

            _logger.Debug("그룹 {GroupId} 노트북 {Count}개 조회", groupId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Notebook>();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "그룹 노트북 조회 실패: GroupId={GroupId}", groupId);
            return new List<Notebook>();
        }
    }

    /// <summary>
    /// 그룹 노트북의 섹션 목록 조회
    /// </summary>
    public async Task<IEnumerable<OnenoteSection>> GetGroupSectionsAsync(string groupId, string notebookId)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(notebookId))
            return new List<OnenoteSection>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Groups[groupId].Onenote.Notebooks[notebookId].Sections.GetAsync();

            _logger.Debug("그룹 {GroupId} 노트북 {NotebookId} 섹션 {Count}개 조회",
                groupId, notebookId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<OnenoteSection>();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "그룹 섹션 조회 실패: GroupId={GroupId}, NotebookId={NotebookId}", groupId, notebookId);
            return new List<OnenoteSection>();
        }
    }

    /// <summary>
    /// 그룹 섹션의 페이지 목록 조회
    /// </summary>
    public async Task<IEnumerable<OnenotePage>> GetGroupPagesAsync(string groupId, string sectionId)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(sectionId))
            return new List<OnenotePage>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Groups[groupId].Onenote.Sections[sectionId].Pages.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
            });

            _logger.Debug("그룹 {GroupId} 섹션 {SectionId} 페이지 {Count}개 조회",
                groupId, sectionId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<OnenotePage>();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "그룹 페이지 조회 실패: GroupId={GroupId}, SectionId={SectionId}", groupId, sectionId);
            return new List<OnenotePage>();
        }
    }

    /// <summary>
    /// SharePoint 사이트의 전자 필기장 조회 (Teams 그룹 사이트 + 팔로우 사이트)
    /// </summary>
    /// <returns>SharePoint 사이트의 전자 필기장 목록</returns>
    public async Task<List<NotebookWithSource>> GetSharePointNotebooksAsync()
    {
        var result = new List<NotebookWithSource>();
        var processedSiteIds = new HashSet<string>();

        try
        {
            var client = _authService.GetGraphClient();

            // 1. 사용자가 속한 Microsoft 365 그룹의 SharePoint 루트 사이트에서 노트북 조회
            var groups = await GetUserGroupsAsync();

            foreach (var group in groups)
            {
                try
                {
                    // 그룹의 루트 SharePoint 사이트 가져오기
                    var rootSite = await client.Groups[group.Id].Sites["root"].GetAsync();
                    if (rootSite != null && !string.IsNullOrEmpty(rootSite.Id))
                    {
                        if (!processedSiteIds.Contains(rootSite.Id))
                        {
                            processedSiteIds.Add(rootSite.Id);

                            try
                            {
                                var siteNotebooks = await client.Sites[rootSite.Id].Onenote.Notebooks.GetAsync();
                                if (siteNotebooks?.Value != null && siteNotebooks.Value.Count > 0)
                                {
                                    foreach (var nb in siteNotebooks.Value)
                                    {
                                        result.Add(new NotebookWithSource
                                        {
                                            Notebook = nb,
                                            Source = NotebookSource.Site,
                                            SourceName = group.DisplayName ?? rootSite.DisplayName ?? "SharePoint",
                                            SiteId = rootSite.Id
                                        });
                                    }
                                }
                            }
                            catch
                            {
                                // 사이트 노트북 조회 실패 시 무시
                            }
                        }
                    }
                }
                catch
                {
                    // 루트 사이트 조회 실패 시 무시
                }
            }

            // 2. 그룹 드라이브(Shared Documents)에서 노트북 검색 - 사이트의 모든 노트북 다시 조회
            foreach (var group in groups)
            {
                try
                {
                    // 그룹의 루트 사이트에서 모든 노트북 조회 (필터 없이)
                    var rootSite = await client.Groups[group.Id].Sites["root"].GetAsync();
                    if (rootSite != null && !string.IsNullOrEmpty(rootSite.Id) && !processedSiteIds.Contains(rootSite.Id + "_all"))
                    {
                        processedSiteIds.Add(rootSite.Id + "_all");

                        try
                        {
                            // 사이트의 모든 노트북 조회 (Top 없이 전체)
                            var allNotebooks = await client.Sites[rootSite.Id].Onenote.Notebooks.GetAsync();

                            if (allNotebooks?.Value != null)
                            {
                                foreach (var nb in allNotebooks.Value)
                                {
                                    // 중복 체크
                                    if (!result.Any(r => r.Notebook?.Id == nb.Id))
                                    {
                                        result.Add(new NotebookWithSource
                                        {
                                            Notebook = nb,
                                            Source = NotebookSource.Site,
                                            SourceName = $"{group.DisplayName}",
                                            SiteId = rootSite.Id
                                        });
                                        Log4.Debug($"[SharePoint] 사이트에서 노트북 발견: {nb.DisplayName} (그룹: {group.DisplayName})");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log4.Debug($"[SharePoint] 사이트 노트북 전체 조회 실패 ({group.DisplayName}): {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log4.Debug($"[SharePoint] 그룹 사이트 검색 실패 ({group.DisplayName}): {ex.Message}");
                }
            }

            // 3. 사용자가 팔로우하는 사이트에서 노트북 조회
            try
            {
                var followedSites = await client.Me.FollowedSites.GetAsync(config =>
                {
                    config.QueryParameters.Top = 50;
                });

                if (followedSites?.Value != null)
                {
                    foreach (var site in followedSites.Value)
                    {
                        if (string.IsNullOrEmpty(site.Id) || processedSiteIds.Contains(site.Id))
                            continue;

                        processedSiteIds.Add(site.Id);

                        try
                        {
                            var siteNotebooks = await client.Sites[site.Id].Onenote.Notebooks.GetAsync();
                            if (siteNotebooks?.Value != null)
                            {
                                foreach (var nb in siteNotebooks.Value)
                                {
                                    result.Add(new NotebookWithSource
                                    {
                                        Notebook = nb,
                                        Source = NotebookSource.Site,
                                        SourceName = site.DisplayName ?? "SharePoint",
                                        SiteId = site.Id
                                    });
                                }
                            }
                        }
                        catch
                        {
                            // 팔로우 사이트 노트북 조회 실패 시 무시
                        }
                    }
                }
            }
            catch
            {
                // 팔로우 사이트 조회 실패 시 무시
            }

            _logger.Debug("SharePoint 사이트 전자 필기장 총 {Count}개 조회", result.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "SharePoint 전자 필기장 조회 실패");
        }

        return result;
    }

    /// <summary>
    /// SharePoint 사이트 경로를 사용하여 해당 사이트의 노트북을 조회합니다.
    /// 예: "sites/AI785-1" 또는 "AI785-1" 형식의 경로를 받습니다.
    /// </summary>
    /// <param name="sitePath">SharePoint 사이트 경로 (예: "sites/AI785-1" 또는 "AI785-1")</param>
    /// <returns>사이트의 노트북 목록</returns>
    public async Task<List<NotebookWithSource>> GetNotebooksFromSitePathAsync(string sitePath)
    {
        var result = new List<NotebookWithSource>();

        if (string.IsNullOrWhiteSpace(sitePath))
        {
            Log4.Warn("[GetNotebooksFromSitePath] 사이트 경로가 비어있습니다.");
            return result;
        }

        try
        {
            var client = _authService.GetGraphClient();

            // 사이트 경로 정규화: "sites/" 접두사가 없으면 추가
            var normalizedPath = sitePath.Trim();
            if (!normalizedPath.StartsWith("sites/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = $"sites/{normalizedPath}";
            }

            Log4.Info($"[GetNotebooksFromSitePath] 사이트 경로로 노트북 조회 시작: {normalizedPath}");

            // SharePoint 호스트명 가져오기 (테넌트 설정에서)
            // Graph API: GET /sites/{hostname}:/{sitePath}:
            // 예: GET /sites/diquest01.sharepoint.com:/sites/AI785-1:
            
            // 먼저 루트 사이트에서 호스트명 추출
            var rootSite = await client.Sites["root"].GetAsync();
            var hostname = rootSite?.SiteCollection?.Hostname ?? "sharepoint.com";
            
            Log4.Debug($"[GetNotebooksFromSitePath] 호스트명: {hostname}");

            // 사이트 정보 조회
            // Graph API: GET /sites/{hostname}:/{relative-path}:
            var siteRequestUrl = $"{hostname}:/{normalizedPath}:";
            var site = await client.Sites[siteRequestUrl].GetAsync();

            if (site == null || string.IsNullOrEmpty(site.Id))
            {
                Log4.Warn($"[GetNotebooksFromSitePath] 사이트를 찾을 수 없습니다: {normalizedPath}");
                return result;
            }

            Log4.Info($"[GetNotebooksFromSitePath] 사이트 발견: {site.DisplayName} (ID: {site.Id})");

            // 사이트의 노트북 조회
            var siteNotebooks = await client.Sites[site.Id].Onenote.Notebooks.GetAsync();

            if (siteNotebooks?.Value != null && siteNotebooks.Value.Count > 0)
            {
                foreach (var nb in siteNotebooks.Value)
                {
                    result.Add(new NotebookWithSource
                    {
                        Notebook = nb,
                        Source = NotebookSource.Site,
                        SourceName = site.DisplayName ?? normalizedPath,
                        SiteId = site.Id
                    });
                    Log4.Info($"[GetNotebooksFromSitePath] 노트북 발견: {nb.DisplayName} (Site: {site.DisplayName})");
                }

                _logger.Information("[GetNotebooksFromSitePath] 사이트 '{SiteName}'에서 {Count}개 노트북 조회 완료",
                    site.DisplayName, result.Count);
            }
            else
            {
                Log4.Info($"[GetNotebooksFromSitePath] 사이트 '{site.DisplayName}'에 노트북이 없습니다.");
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataError)
        {
            Log4.Error($"[GetNotebooksFromSitePath] Graph API 오류: {odataError.Error?.Code} - {odataError.Error?.Message}");
            _logger.Error(odataError, "[GetNotebooksFromSitePath] 사이트 '{SitePath}' 노트북 조회 실패: {Message}",
                sitePath, odataError.Error?.Message);
        }
        catch (Exception ex)
        {
            Log4.Error($"[GetNotebooksFromSitePath] 사이트 노트북 조회 실패: {ex.Message}");
            _logger.Error(ex, "[GetNotebooksFromSitePath] 사이트 '{SitePath}' 노트북 조회 실패", sitePath);
        }

        return result;
    }

    /// <summary>
    /// SharePoint 사이트 노트북의 섹션 목록 조회
    /// </summary>
    public async Task<IEnumerable<OnenoteSection>> GetSiteSectionsAsync(string siteId, string notebookId)
    {
        if (string.IsNullOrEmpty(siteId) || string.IsNullOrEmpty(notebookId))
            return new List<OnenoteSection>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Sites[siteId].Onenote.Notebooks[notebookId].Sections.GetAsync();

            _logger.Debug("사이트 {SiteId} 노트북 {NotebookId} 섹션 {Count}개 조회",
                siteId, notebookId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<OnenoteSection>();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "사이트 섹션 조회 실패: SiteId={SiteId}, NotebookId={NotebookId}", siteId, notebookId);
            return new List<OnenoteSection>();
        }
    }

    /// <summary>
    /// SharePoint 사이트 섹션의 페이지 목록 조회
    /// </summary>
    public async Task<IEnumerable<OnenotePage>> GetSitePagesAsync(string siteId, string sectionId)
    {
        if (string.IsNullOrEmpty(siteId) || string.IsNullOrEmpty(sectionId))
            return new List<OnenotePage>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Sites[siteId].Onenote.Sections[sectionId].Pages.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
            });

            _logger.Debug("사이트 {SiteId} 섹션 {SectionId} 페이지 {Count}개 조회",
                siteId, sectionId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<OnenotePage>();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "사이트 페이지 조회 실패: SiteId={SiteId}, SectionId={SectionId}", siteId, sectionId);
            return new List<OnenotePage>();
        }
    }

    /// <summary>
    /// 모든 노트북 통합 조회 (개인 + 그룹 + SharePoint 사이트)
    /// </summary>
    /// <returns>출처 정보가 포함된 노트북 목록</returns>
    public async Task<IEnumerable<NotebookWithSource>> GetAllNotebooksAsync()
    {
        var result = new List<NotebookWithSource>();
        var client = _authService.GetGraphClient();

        try
        {
            // 1. 개인 노트북 조회
            Log4.Info("[GetAllNotebooks] 1단계: 개인 노트북 조회 시작");
            var personalNotebooks = await GetNotebooksAsync();
            var personalCount = personalNotebooks.Count();
            foreach (var nb in personalNotebooks)
            {
                result.Add(new NotebookWithSource
                {
                    Notebook = nb,
                    Source = NotebookSource.Personal,
                    SourceName = "개인"
                });
            }
            Log4.Info($"[GetAllNotebooks] 개인 노트북 {personalCount}개 추가");

            // 2. 그룹 노트북 조회 (순차 처리 - Rate Limit 방지)
            Log4.Info("[GetAllNotebooks] 2단계: 그룹 노트북 조회 시작");
            var groups = await GetUserGroupsAsync();
            var groupList = groups.ToList();
            Log4.Info($"[GetAllNotebooks] 그룹 {groupList.Count}개 발견");

            // 순차적으로 그룹 노트북 조회 (Rate Limit 방지)
            // 그룹 노트북은 그룹의 SharePoint 사이트에 저장되므로 SiteId도 함께 저장
            foreach (var group in groupList)
            {
                try
                {
                    // 그룹의 SharePoint 사이트 ID 가져오기
                    string siteId = string.Empty;
                    try
                    {
                        var rootSite = await client.Groups[group.Id].Sites["root"].GetAsync();
                        siteId = rootSite?.Id ?? string.Empty;
                    }
                    catch
                    {
                        // 사이트 ID 조회 실패 시 무시
                    }

                    var notebooks = await GetGroupNotebooksAsync(group.Id ?? string.Empty);
                    foreach (var nb in notebooks)
                    {
                        result.Add(new NotebookWithSource
                        {
                            Notebook = nb,
                            Source = NotebookSource.Group,
                            SourceName = group.DisplayName ?? "팀",
                            GroupId = group.Id ?? string.Empty,
                            SiteId = siteId  // 그룹의 SharePoint 사이트 ID도 저장
                        });
                        Log4.Debug($"[GetAllNotebooks] 그룹 노트북 추가: {nb.DisplayName} (GroupId={group.Id}, SiteId={siteId})");
                    }
                }
                catch (Exception ex)
                {
                    Log4.Debug($"[GetAllNotebooks] 그룹 {group.DisplayName} 노트북 조회 실패: {ex.Message}");
                }
            }

            var groupCount = result.Count - personalCount;
            Log4.Info($"[GetAllNotebooks] 그룹 노트북 {groupCount}개 추가");

            // 3. SharePoint 사이트 전자 필기장 조회 (팔로우 사이트 포함)
            // 2단계에서 그룹 사이트를 조회했으나, 직접 SharePoint 사이트에 생성된 노트북은 누락될 수 있음
            Log4.Info("[GetAllNotebooks] 3단계: SharePoint 사이트 노트북 조회 시작");
            var siteCount = 0;
            var processedSiteIds = new HashSet<string>(result.Where(r => !string.IsNullOrEmpty(r.SiteId)).Select(r => r.SiteId));
            var processedNotebookIds = new HashSet<string>(result.Where(r => r.Notebook?.Id != null).Select(r => r.Notebook.Id!));

            try
            {
                // 3-1. 팔로우하는 사이트에서 노트북 조회
                var followedSites = await client.Me.FollowedSites.GetAsync(config =>
                {
                    config.QueryParameters.Top = 50;
                });

                if (followedSites?.Value != null)
                {
                    Log4.Info($"[GetAllNotebooks] 팔로우 사이트 {followedSites.Value.Count}개 발견");
                    foreach (var site in followedSites.Value)
                    {
                        if (string.IsNullOrEmpty(site.Id) || processedSiteIds.Contains(site.Id))
                        {
                            Log4.Debug($"[GetAllNotebooks] 사이트 건너뜀 (이미 처리됨 또는 ID 없음): {site.DisplayName}");
                            continue;
                        }

                        processedSiteIds.Add(site.Id);

                        try
                        {
                            var siteNotebooks = await client.Sites[site.Id].Onenote.Notebooks.GetAsync();
                            if (siteNotebooks?.Value != null)
                            {
                                foreach (var nb in siteNotebooks.Value)
                                {
                                    // 중복 노트북 체크
                                    if (nb.Id != null && processedNotebookIds.Contains(nb.Id))
                                    {
                                        Log4.Debug($"[GetAllNotebooks] 중복 노트북 건너뜀: {nb.DisplayName}");
                                        continue;
                                    }

                                    if (nb.Id != null)
                                        processedNotebookIds.Add(nb.Id);

                                    result.Add(new NotebookWithSource
                                    {
                                        Notebook = nb,
                                        Source = NotebookSource.Site,
                                        SourceName = site.DisplayName ?? "SharePoint",
                                        SiteId = site.Id
                                    });
                                    siteCount++;
                                    Log4.Debug($"[GetAllNotebooks] 팔로우 사이트 노트북 추가: {nb.DisplayName} (Site={site.DisplayName}, SiteId={site.Id})");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log4.Debug($"[GetAllNotebooks] 팔로우 사이트 노트북 조회 실패 ({site.DisplayName}): {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log4.Warn($"[GetAllNotebooks] 팔로우 사이트 조회 실패: {ex.Message}");
            }

            Log4.Info($"[GetAllNotebooks] SharePoint 사이트 노트북 {siteCount}개 추가");

            _logger.Information("전체 노트북 조회 완료: 개인 {PersonalCount}개, 그룹 {GroupCount}개",
                personalCount, groupCount);
        }
        catch (Exception ex)
        {
            Log4.Error($"[GetAllNotebooks] 전체 노트북 조회 실패: {ex.Message}");
            _logger.Error(ex, "[GetAllNotebooks] 전체 노트북 조회 실패: {Message}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// 노트북의 섹션 목록 조회
    /// </summary>
    /// <param name="notebookId">노트북 ID</param>
    /// <returns>섹션 목록</returns>
    public async Task<IEnumerable<OnenoteSection>> GetSectionsAsync(string notebookId)
    {
        if (string.IsNullOrEmpty(notebookId))
            throw new ArgumentNullException(nameof(notebookId));

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Onenote.Notebooks[notebookId].Sections.GetAsync();

            _logger.Debug("노트북 {NotebookId} 섹션 {Count}개 조회", notebookId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<OnenoteSection>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "섹션 목록 조회 실패: NotebookId={NotebookId}", notebookId);
            throw;
        }
    }

    /// <summary>
    /// 섹션의 페이지 목록 조회
    /// </summary>
    /// <param name="sectionId">섹션 ID</param>
    /// <returns>페이지 목록</returns>
    public async Task<IEnumerable<OnenotePage>> GetPagesAsync(string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId))
            throw new ArgumentNullException(nameof(sectionId));

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Onenote.Sections[sectionId].Pages.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
            });

            _logger.Debug("섹션 {SectionId} 페이지 {Count}개 조회", sectionId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<OnenotePage>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 목록 조회 실패: SectionId={SectionId}", sectionId);
            throw;
        }
    }

    /// <summary>
    /// 페이지 내용 가져오기
    /// </summary>
    /// <param name="pageId">페이지 ID</param>
    /// <returns>페이지 HTML 내용</returns>
    public async Task<string?> GetPageContentAsync(string pageId)
    {
        return await GetPageContentAsync(pageId, null, null);
    }

    /// <summary>
    /// 페이지 내용 가져오기 (그룹/사이트 노트북 지원)
    /// </summary>
    /// <param name="pageId">페이지 ID</param>
    /// <param name="groupId">그룹 ID (그룹 노트북인 경우)</param>
    /// <param name="siteId">사이트 ID (사이트 노트북인 경우)</param>
    /// <returns>페이지 HTML 내용</returns>
    public async Task<string?> GetPageContentAsync(string pageId, string? groupId, string? siteId)
    {
        if (string.IsNullOrEmpty(pageId))
            throw new ArgumentNullException(nameof(pageId));

        try
        {
            var client = _authService.GetGraphClient();
            System.IO.Stream? contentStream = null;

            // 그룹 노트북인 경우
            if (!string.IsNullOrEmpty(groupId))
            {
                Log4.Debug($"[OneNote] 그룹 페이지 콘텐츠 로드: GroupId={groupId}, PageId={pageId}");
                contentStream = await client.Groups[groupId].Onenote.Pages[pageId].Content.GetAsync();
            }
            // 사이트 노트북인 경우
            else if (!string.IsNullOrEmpty(siteId))
            {
                Log4.Debug($"[OneNote] 사이트 페이지 콘텐츠 로드: SiteId={siteId}, PageId={pageId}");
                contentStream = await client.Sites[siteId].Onenote.Pages[pageId].Content.GetAsync();
            }
            // 개인 노트북인 경우
            else
            {
                Log4.Debug($"[OneNote] 개인 페이지 콘텐츠 로드: PageId={pageId}");
                contentStream = await client.Me.Onenote.Pages[pageId].Content.GetAsync();
            }

            if (contentStream == null)
                return null;

            using var reader = new System.IO.StreamReader(contentStream);
            var content = await reader.ReadToEndAsync();

            _logger.Debug("페이지 {PageId} 내용 조회 완료 (GroupId={GroupId}, SiteId={SiteId})", pageId, groupId ?? "N/A", siteId ?? "N/A");
            return content;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 내용 조회 실패: PageId={PageId}, GroupId={GroupId}, SiteId={SiteId}", pageId, groupId ?? "N/A", siteId ?? "N/A");
            throw;
        }
    }

    /// <summary>
    /// 이메일 내용으로 새 페이지 생성
    /// </summary>
    /// <param name="sectionId">섹션 ID</param>
    /// <param name="email">이메일 정보</param>
    /// <returns>생성된 페이지</returns>
    public async Task<OnenotePage?> CreatePageFromEmailAsync(string sectionId, MaiXEmail email)
    {
        if (string.IsNullOrEmpty(sectionId))
            throw new ArgumentNullException(nameof(sectionId));
        if (email == null)
            throw new ArgumentNullException(nameof(email));

        try
        {
            // OneNote HTML 형식으로 페이지 생성
            var htmlContent = BuildEmailHtmlContent(email);

            // Graph SDK v5.x에서는 OnenotePage 객체를 사용
            var page = await CreatePageWithHtmlContentAsync(sectionId, htmlContent);

            if (page != null)
            {
                // 로컬 DB에 저장
                await SavePageAsync(page, email.Id);
                _logger.Information("이메일에서 OneNote 페이지 생성: {Title}", email.Subject);
            }

            return page;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "이메일에서 OneNote 페이지 생성 실패: EmailId={EmailId}", email.Id);
            throw;
        }
    }

    /// <summary>
    /// 할일을 OneNote에 저장
    /// </summary>
    /// <param name="sectionId">섹션 ID</param>
    /// <param name="todo">할일 정보</param>
    /// <returns>생성된 페이지</returns>
    public async Task<OnenotePage?> CreatePageFromTodoAsync(string sectionId, MaiXTodo todo)
    {
        if (string.IsNullOrEmpty(sectionId))
            throw new ArgumentNullException(nameof(sectionId));
        if (todo == null)
            throw new ArgumentNullException(nameof(todo));

        try
        {
            var htmlContent = BuildTodoHtmlContent(todo);

            // Graph SDK v5.x에서는 OnenotePage 객체를 사용
            var page = await CreatePageWithHtmlContentAsync(sectionId, htmlContent);

            if (page != null)
            {
                await SavePageAsync(page, todo.EmailId);
                _logger.Information("할일에서 OneNote 페이지 생성: {Content}", todo.Content.Substring(0, Math.Min(50, todo.Content.Length)));
            }

            return page;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "할일에서 OneNote 페이지 생성 실패: TodoId={TodoId}", todo.Id);
            throw;
        }
    }

    /// <summary>
    /// HTML 콘텐츠로 OneNote 페이지 생성 (내부 헬퍼)
    /// Graph SDK v5.x에서는 직접 REST API 호출 필요
    /// </summary>
    private async Task<OnenotePage?> CreatePageWithHtmlContentAsync(string sectionId, string htmlContent)
    {
        var client = _authService.GetGraphClient();

        // Graph SDK v5.x에서 OneNote 페이지 생성은 multipart/form-data로 처리해야 함
        // 간소화를 위해 먼저 빈 페이지를 생성하고 나중에 업데이트하는 방식 사용
        // 또는 HTTP 클라이언트를 직접 사용

        try
        {
            // Microsoft Graph SDK v5.x에서는 Pages.PostAsync가 OnenotePage를 받음
            // OneNote 페이지 생성은 특수한 경우로, REST API를 직접 호출해야 함
            // 여기서는 SDK의 제한으로 인해 페이지 제목만 설정하고 생성

            // Note: 실제 구현에서는 HttpClient를 사용하여
            // POST /me/onenote/sections/{id}/pages 에
            // Content-Type: application/xhtml+xml 로 HTML 전송 필요

            _logger.Warning("OneNote 페이지 생성은 현재 HTML 콘텐츠 없이 생성됩니다. 추후 REST API 직접 호출로 개선 필요.");

            // SDK에서 지원하는 방식으로 페이지 메타데이터만 설정
            // 실제 HTML 콘텐츠 전송을 위해서는 HttpRequestMessage 사용 필요
            var newPage = new OnenotePage
            {
                Title = ExtractTitleFromHtml(htmlContent)
            };

            // Note: 이 방식은 빈 페이지를 생성함
            // 실제 HTML 콘텐츠 전송은 별도의 HTTP 요청 필요
            var createdPage = await client.Me.Onenote.Sections[sectionId].Pages.PostAsync(newPage);

            return createdPage;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OneNote 페이지 생성 실패");
            throw;
        }
    }

    /// <summary>
    /// HTML에서 제목 추출
    /// </summary>
    private string ExtractTitleFromHtml(string html)
    {
        // <title> 태그에서 제목 추출
        var titleMatch = System.Text.RegularExpressions.Regex.Match(
            html,
            @"<title>([^<]*)</title>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (titleMatch.Success)
            return titleMatch.Groups[1].Value;

        // <h1> 태그에서 제목 추출
        var h1Match = System.Text.RegularExpressions.Regex.Match(
            html,
            @"<h1>([^<]*)</h1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (h1Match.Success)
            return h1Match.Groups[1].Value;

        return "새 페이지";
    }

    /// <summary>
    /// OneNote 페이지를 로컬 DB에 저장
    /// </summary>
    /// <param name="onenotePage">Graph API 페이지</param>
    /// <param name="linkedEmailId">연결된 이메일 ID (선택)</param>
    /// <returns>저장된 OneNotePage</returns>
    public async Task<MaiXOneNotePage> SavePageAsync(OnenotePage onenotePage, int? linkedEmailId = null)
    {
        if (onenotePage == null)
            throw new ArgumentNullException(nameof(onenotePage));

        try
        {
            var pageId = onenotePage.Id ?? Guid.NewGuid().ToString();

            var existingPage = await _dbContext.OneNotePages
                .FirstOrDefaultAsync(p => p.Id == pageId);

            if (existingPage != null)
            {
                existingPage.Title = onenotePage.Title;
                existingPage.ContentUrl = onenotePage.ContentUrl;
                existingPage.LinkedEmailId = linkedEmailId ?? existingPage.LinkedEmailId;
            }
            else
            {
                var page = new MaiXOneNotePage
                {
                    Id = pageId,
                    SectionId = onenotePage.ParentSection?.Id,
                    Title = onenotePage.Title,
                    ContentUrl = onenotePage.ContentUrl,
                    LinkedEmailId = linkedEmailId,
                    CreatedDateTime = onenotePage.CreatedDateTime?.DateTime
                };

                _dbContext.OneNotePages.Add(page);
                existingPage = page;
            }

            await _dbContext.SaveChangesAsync();
            _logger.Debug("OneNote 페이지 저장: {PageId}", pageId);

            return existingPage;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OneNote 페이지 저장 실패");
            throw;
        }
    }

    /// <summary>
    /// 이메일과 연결된 OneNote 페이지 조회
    /// </summary>
    /// <param name="emailId">이메일 ID</param>
    /// <returns>연결된 페이지 목록</returns>
    public async Task<IEnumerable<MaiXOneNotePage>> GetLinkedPagesAsync(int emailId)
    {
        return await _dbContext.OneNotePages
            .Where(p => p.LinkedEmailId == emailId)
            .OrderByDescending(p => p.CreatedDateTime)
            .ToListAsync();
    }

    /// <summary>
    /// 이메일 HTML 콘텐츠 생성
    /// </summary>
    private string BuildEmailHtmlContent(MaiXEmail email)
    {
        var dateStr = email.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "날짜 없음";
        var priorityStr = email.PriorityLevel ?? "normal";

        return $@"<!DOCTYPE html>
<html>
<head>
    <title>{EscapeHtml(email.Subject)}</title>
    <meta name=""created"" content=""{DateTime.Now:yyyy-MM-ddTHH:mm:ssZ}"" />
</head>
<body>
    <h1>{EscapeHtml(email.Subject)}</h1>

    <table style=""border-collapse: collapse; width: 100%; margin-bottom: 20px;"">
        <tr>
            <td style=""padding: 5px; font-weight: bold; width: 100px;"">발신자:</td>
            <td style=""padding: 5px;"">{EscapeHtml(email.From)}</td>
        </tr>
        <tr>
            <td style=""padding: 5px; font-weight: bold;"">수신일:</td>
            <td style=""padding: 5px;"">{dateStr}</td>
        </tr>
        <tr>
            <td style=""padding: 5px; font-weight: bold;"">우선순위:</td>
            <td style=""padding: 5px;"">{priorityStr} ({email.PriorityScore ?? 0}점)</td>
        </tr>
        {(email.Deadline.HasValue ? $@"<tr>
            <td style=""padding: 5px; font-weight: bold;"">마감일:</td>
            <td style=""padding: 5px;"">{email.Deadline.Value:yyyy-MM-dd}</td>
        </tr>" : "")}
    </table>

    {(!string.IsNullOrEmpty(email.SummaryOneline) ? $@"<h2>요약</h2>
    <p>{EscapeHtml(email.SummaryOneline)}</p>" : "")}

    <h2>본문</h2>
    <div>{email.Body ?? "내용 없음"}</div>
</body>
</html>";
    }

    /// <summary>
    /// 할일 HTML 콘텐츠 생성
    /// </summary>
    private string BuildTodoHtmlContent(MaiXTodo todo)
    {
        var dueDateStr = todo.DueDate?.ToString("yyyy-MM-dd") ?? "마감일 없음";
        var priorityStr = todo.Priority switch
        {
            1 => "매우 높음",
            2 => "높음",
            3 => "보통",
            4 => "낮음",
            5 => "매우 낮음",
            _ => "보통"
        };

        return $@"<!DOCTYPE html>
<html>
<head>
    <title>TODO: {EscapeHtml(todo.Content.Substring(0, Math.Min(50, todo.Content.Length)))}</title>
    <meta name=""created"" content=""{DateTime.Now:yyyy-MM-ddTHH:mm:ssZ}"" />
</head>
<body>
    <h1>할일 항목</h1>

    <table style=""border-collapse: collapse; width: 100%; margin-bottom: 20px;"">
        <tr>
            <td style=""padding: 5px; font-weight: bold; width: 100px;"">상태:</td>
            <td style=""padding: 5px;"">{todo.Status}</td>
        </tr>
        <tr>
            <td style=""padding: 5px; font-weight: bold;"">우선순위:</td>
            <td style=""padding: 5px;"">{priorityStr}</td>
        </tr>
        <tr>
            <td style=""padding: 5px; font-weight: bold;"">마감일:</td>
            <td style=""padding: 5px;"">{dueDateStr}</td>
        </tr>
    </table>

    <h2>내용</h2>
    <p data-tag=""to-do"">{EscapeHtml(todo.Content)}</p>
</body>
</html>";
    }

    /// <summary>
    /// HTML 특수문자 이스케이프
    /// </summary>
    private string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    /// <summary>
    /// 새 노트북 생성
    /// </summary>
    /// <param name="displayName">노트북 이름</param>
    /// <returns>생성된 노트북</returns>
    public async Task<Notebook?> CreateNotebookAsync(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            throw new ArgumentNullException(nameof(displayName));

        try
        {
            var client = _authService.GetGraphClient();

            var notebook = new Notebook
            {
                DisplayName = displayName
            };

            var response = await client.Me.Onenote.Notebooks.PostAsync(notebook);

            _logger.Information("노트북 생성 완료: {DisplayName}", displayName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "노트북 생성 실패: DisplayName={DisplayName}", displayName);
            throw;
        }
    }

    /// <summary>
    /// 새 섹션 생성
    /// </summary>
    /// <param name="notebookId">노트북 ID</param>
    /// <param name="displayName">섹션 이름</param>
    /// <returns>생성된 섹션</returns>
    public async Task<OnenoteSection?> CreateSectionAsync(string notebookId, string displayName)
    {
        if (string.IsNullOrEmpty(notebookId))
            throw new ArgumentNullException(nameof(notebookId));
        if (string.IsNullOrEmpty(displayName))
            throw new ArgumentNullException(nameof(displayName));

        try
        {
            var client = _authService.GetGraphClient();

            var section = new OnenoteSection
            {
                DisplayName = displayName
            };

            var response = await client.Me.Onenote.Notebooks[notebookId].Sections.PostAsync(section);

            _logger.Information("섹션 생성 완료: {DisplayName}", displayName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "섹션 생성 실패: DisplayName={DisplayName}", displayName);
            throw;
        }
    }

    /// <summary>
    /// 새 페이지 생성 (제목과 HTML 콘텐츠)
    /// </summary>
    /// <param name="sectionId">섹션 ID</param>
    /// <param name="title">페이지 제목</param>
    /// <param name="htmlContent">HTML 콘텐츠 (선택)</param>
    /// <returns>생성된 페이지</returns>
    public async Task<OnenotePage?> CreatePageAsync(string sectionId, string title, string? htmlContent = null)
    {
        if (string.IsNullOrEmpty(sectionId))
            throw new ArgumentNullException(nameof(sectionId));
        if (string.IsNullOrEmpty(title))
            throw new ArgumentNullException(nameof(title));

        try
        {
            var client = _authService.GetGraphClient();

            // HTML 콘텐츠 생성 (OneNote API는 HTML 형식으로 페이지 생성)
            var bodyContent = string.IsNullOrEmpty(htmlContent) ? "<p></p>" : htmlContent;
            var htmlPage = $@"<!DOCTYPE html>
<html>
<head>
<title>{System.Web.HttpUtility.HtmlEncode(title)}</title>
</head>
<body>
{bodyContent}
</body>
</html>";

            // REST API 직접 호출 (Graph SDK가 스트림을 지원하지 않는 경우)
            var httpClient = new System.Net.Http.HttpClient();
            var token = await _authService.GetAccessTokenAsync();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var content = new System.Net.Http.StringContent(htmlPage, System.Text.Encoding.UTF8, "text/html");
            var response = await httpClient.PostAsync(
                $"https://graph.microsoft.com/v1.0/me/onenote/sections/{sectionId}/pages",
                content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var page = System.Text.Json.JsonSerializer.Deserialize<OnenotePage>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                _logger.Information("페이지 생성 완료: {Title}", title);
                return page;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error("페이지 생성 실패: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 생성 실패: Title={Title}", title);
            throw;
        }
    }

    /// <summary>
    /// 파일 확장자로 MIME 타입 반환
    /// </summary>
    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".html" => "text/html",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 파일 첨부와 함께 새 페이지 생성 (multipart/form-data)
    /// </summary>
    public async Task<OnenotePage?> CreatePageWithAttachmentsAsync(
        string sectionId, string title, string? htmlContent,
        List<(string FilePath, string FileName)> attachments)
    {
        if (string.IsNullOrEmpty(sectionId))
            throw new ArgumentNullException(nameof(sectionId));
        if (string.IsNullOrEmpty(title))
            throw new ArgumentNullException(nameof(title));

        // 첨부파일 없으면 기존 메서드 사용
        if (attachments == null || attachments.Count == 0)
            return await CreatePageAsync(sectionId, title, htmlContent);

        try
        {
            var token = await _authService.GetAccessTokenAsync();
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // HTML에 <object> 태그 삽입
            var bodyContent = string.IsNullOrEmpty(htmlContent) ? "<p></p>" : htmlContent;
            var attachmentHtml = new StringBuilder();
            for (int i = 0; i < attachments.Count; i++)
            {
                var (filePath, fileName) = attachments[i];
                var mimeType = GetMimeType(fileName);
                var partName = $"file{i + 1}";
                attachmentHtml.AppendLine(
                    $"<object data-attachment=\"{System.Web.HttpUtility.HtmlEncode(fileName)}\" " +
                    $"data=\"name:{partName}\" type=\"{mimeType}\" />");
            }

            var htmlPage = $@"<!DOCTYPE html>
<html>
<head><title>{System.Web.HttpUtility.HtmlEncode(title)}</title></head>
<body>
{bodyContent}
{attachmentHtml}
</body>
</html>";

            // multipart/form-data 구성
            using var multipart = new MultipartFormDataContent();

            // Presentation 파트 (HTML)
            var presentationContent = new StringContent(htmlPage, Encoding.UTF8, "text/html");
            multipart.Add(presentationContent, "Presentation");

            // 파일 파트들
            for (int i = 0; i < attachments.Count; i++)
            {
                var (filePath, fileName) = attachments[i];
                var partName = $"file{i + 1}";
                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(GetMimeType(fileName));
                multipart.Add(fileContent, partName);
            }

            var response = await httpClient.PostAsync(
                $"https://graph.microsoft.com/v1.0/me/onenote/sections/{sectionId}/pages",
                multipart);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var page = JsonSerializer.Deserialize<OnenotePage>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                _logger.Information("파일 첨부 페이지 생성 완료: {Title}, 첨부={Count}개", title, attachments.Count);
                return page;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error("파일 첨부 페이지 생성 실패: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "파일 첨부 페이지 생성 예외: Title={Title}", title);
            throw;
        }
    }

    /// <summary>
    /// 기존 페이지에 파일 첨부 (multipart/form-data PATCH)
    /// </summary>
    public async Task<bool> AppendFileToPageAsync(string pageId, string filePath, string fileName)
    {
        if (string.IsNullOrEmpty(pageId))
            return false;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var token = await _authService.GetAccessTokenAsync();
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var mimeType = GetMimeType(fileName);
            var partName = "fileAttachment1";

            // Commands 파트 (JSON)
            var commands = new[]
            {
                new
                {
                    target = "body",
                    action = "append",
                    position = "after",
                    content = $"<object data-attachment=\"{System.Web.HttpUtility.HtmlEncode(fileName)}\" " +
                              $"data=\"name:{partName}\" type=\"{mimeType}\" />"
                }
            };
            var commandsJson = JsonSerializer.Serialize(commands);

            // multipart/form-data 구성
            using var multipart = new MultipartFormDataContent();

            var commandsContent = new StringContent(commandsJson, Encoding.UTF8, "application/json");
            multipart.Add(commandsContent, "Commands");

            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            multipart.Add(fileContent, partName);

            var url = $"https://graph.microsoft.com/v1.0/me/onenote/pages/{pageId}/content";
            var response = await httpClient.PatchAsync(url, multipart);

            if (response.IsSuccessStatusCode)
            {
                _logger.Information("페이지 파일 첨부 완료: PageId={PageId}, File={FileName}", pageId, fileName);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error("페이지 파일 첨부 실패: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 파일 첨부 예외: PageId={PageId}, File={FileName}", pageId, fileName);
            return false;
        }
    }


    /// <summary>
    /// 섹션 삭제
    /// </summary>
    public async Task DeleteSectionAsync(string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId))
            throw new ArgumentNullException(nameof(sectionId));

        try
        {
            var httpClient = new System.Net.Http.HttpClient();
            var token = await _authService.GetAccessTokenAsync();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.DeleteAsync(
                $"https://graph.microsoft.com/v1.0/me/onenote/sections/{sectionId}");

            if (response.IsSuccessStatusCode)
            {
                _logger.Information("섹션 삭제 완료: {SectionId}", sectionId);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error("섹션 삭제 실패: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new Exception($"섹션 삭제 실패: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "섹션 삭제 실패: SectionId={SectionId}", sectionId);
            throw;
        }
    }

    /// <summary>
    /// 페이지(노트) 삭제
    /// </summary>
    public async Task DeletePageAsync(string pageId)
    {
        if (string.IsNullOrEmpty(pageId))
            throw new ArgumentNullException(nameof(pageId));

        try
        {
            var httpClient = new System.Net.Http.HttpClient();
            var token = await _authService.GetAccessTokenAsync();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.DeleteAsync(
                $"https://graph.microsoft.com/v1.0/me/onenote/pages/{pageId}");

            if (response.IsSuccessStatusCode)
            {
                _logger.Information("페이지 삭제 완료: {PageId}", pageId);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error("페이지 삭제 실패: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new Exception($"페이지 삭제 실패: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 삭제 실패: PageId={PageId}", pageId);
            throw;
        }
    }

    /// <summary>
    /// 페이지 제목 업데이트 (PATCH API)
    /// </summary>
    /// <param name="pageId">페이지 ID</param>
    /// <param name="newTitle">새 제목</param>
    /// <returns>성공 여부</returns>
    public async Task<bool> UpdatePageTitleAsync(string pageId, string newTitle)
    {
        if (string.IsNullOrEmpty(pageId))
            throw new ArgumentNullException(nameof(pageId));

        if (string.IsNullOrWhiteSpace(newTitle))
            throw new ArgumentNullException(nameof(newTitle));

        try
        {
            Log4.Info($"[GraphOneNote] 페이지 제목 업데이트 시작: PageId={pageId}, NewTitle={newTitle}");

            var accessToken = await _authService.GetAccessTokenAsync();

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            // OneNote API에서 제목 변경은 title 요소를 replace하는 방식
            var url = $"https://graph.microsoft.com/v1.0/me/onenote/pages/{pageId}/content";

            var patchOperations = new object[]
            {
                new
                {
                    target = "title",
                    action = "replace",
                    content = newTitle
                }
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(patchOperations);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PatchAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                Log4.Info($"[GraphOneNote] 페이지 제목 업데이트 완료: {newTitle}");
                return true;
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Log4.Warn($"[GraphOneNote] 페이지 제목 업데이트 실패: {response.StatusCode}, {errorBody}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[GraphOneNote] 페이지 제목 업데이트 오류: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 페이지 내용 업데이트 (PATCH API)
    /// OneNote Graph API는 generated ID를 사용한 replace만 지원
    /// 전략: editorRoot div의 generated ID를 찾아서 replace, 없으면 append
    /// </summary>
    /// <param name="pageId">페이지 ID</param>
    /// <param name="htmlContent">새 HTML 콘텐츠</param>
    /// <returns>성공 여부</returns>
    public async Task<bool> UpdatePageContentAsync(string pageId, string htmlContent)
    {
        Log4.Debug($"[GraphOneNote] UpdatePageContentAsync 진입: PageId={pageId}, ContentLength={htmlContent?.Length ?? 0}");

        if (string.IsNullOrEmpty(pageId))
            throw new ArgumentNullException(nameof(pageId));

        try
        {
            var accessToken = await _authService.GetAccessTokenAsync();
            Log4.Debug("[GraphOneNote] 액세스 토큰 획득 완료");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            // HTML에서 body 내용만 추출
            var bodyContent = htmlContent;
            var bodyMatch = Regex.Match(htmlContent, @"<body[^>]*>(.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (bodyMatch.Success)
            {
                bodyContent = bodyMatch.Groups[1].Value;
                Log4.Debug($"[GraphOneNote] body 태그 내용 추출: {bodyContent.Length}자");
            }

            // 내용이 비어있으면 최소 내용 유지
            if (string.IsNullOrWhiteSpace(bodyContent) || bodyContent.Trim() == "<p></p>" || bodyContent.Trim() == "<p><br></p>")
            {
                bodyContent = "<p>&nbsp;</p>";
                Log4.Debug("[GraphOneNote] 빈 콘텐츠 → 최소 내용으로 대체");
            }

            // 현재 페이지에서 editorRoot의 generated ID 조회
            Log4.Debug("[GraphOneNote] editorRoot generated ID 조회 중...");
            var editorRootGeneratedId = await GetEditorRootGeneratedIdAsync(httpClient, pageId);
            Log4.Debug($"[GraphOneNote] editorRoot generated ID: {editorRootGeneratedId ?? "없음"}");

            var url = $"https://graph.microsoft.com/v1.0/me/onenote/pages/{pageId}/content";

            object[] patchOperations;

            if (!string.IsNullOrEmpty(editorRootGeneratedId))
            {
                // generated ID가 있으면 replace 사용 (중복 추가 방지)
                Log4.Debug($"[GraphOneNote] replace 사용: target={editorRootGeneratedId}");
                patchOperations = new object[]
                {
                    new
                    {
                        target = editorRootGeneratedId,
                        action = "replace",
                        content = $"<div data-id=\"editorRoot\">{bodyContent}</div>"
                    }
                };
            }
            else
            {
                // generated ID가 없으면 최초 저장 - append 사용
                Log4.Debug("[GraphOneNote] 최초 저장: append 사용");
                patchOperations = new object[]
                {
                    new
                    {
                        target = "body",
                        action = "append",
                        content = $"<div data-id=\"editorRoot\">{bodyContent}</div>"
                    }
                };
            }

            var patchJson = JsonSerializer.Serialize(patchOperations);
            Log4.Debug($"[GraphOneNote] PATCH 요청 전송: PageId={pageId}, JSON길이={patchJson.Length}");

            var patchContent = new StringContent(patchJson, Encoding.UTF8, "application/json");
            var response = await httpClient.PatchAsync(url, patchContent);
            Log4.Debug($"[GraphOneNote] PATCH 응답: StatusCode={response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                Log4.Info($"[GraphOneNote] 페이지 업데이트 완료: {pageId}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log4.Warn($"[GraphOneNote] 페이지 업데이트 실패: StatusCode={response.StatusCode}, Error={errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[GraphOneNote] 페이지 업데이트 예외: PageId={pageId}, Error={ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }


    /// <summary>
    /// 페이지에서 editorRoot div의 generated ID를 조회
    /// includeIDs=true로 페이지 콘텐츠를 조회하여 data-id="editorRoot"를 가진 div의 id 속성 추출
    /// 예: <div id="div:{guid}{index}" data-id="editorRoot">
    /// </summary>
    private async Task<string?> GetEditorRootGeneratedIdAsync(HttpClient httpClient, string pageId)
    {
        try
        {
            var url = $"https://graph.microsoft.com/v1.0/me/onenote/pages/{pageId}/content?includeIDs=true";
            Log4.Debug($"[GraphOneNote] editorRoot generated ID GET: {url}");

            var response = await httpClient.GetAsync(url);
            Log4.Debug($"[GraphOneNote] editorRoot 조회 응답: StatusCode={response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                Log4.Debug($"[GraphOneNote] 페이지 HTML 길이: {html?.Length ?? 0}");

                if (string.IsNullOrEmpty(html))
                    return null;

                // 먼저 data-id="editorRoot"가 있는지 확인
                var editorRootIndex = html.IndexOf("data-id=\"editorRoot\"", StringComparison.OrdinalIgnoreCase);
                if (editorRootIndex == -1)
                {
                    Log4.Debug("[GraphOneNote] data-id=\"editorRoot\" 없음 (최초 저장)");
                    return null;
                }

                // editorRoot 주변 HTML 샘플 추출 (디버깅용)
                var sampleStart = Math.Max(0, editorRootIndex - 200);
                var sampleEnd = Math.Min(html.Length, editorRootIndex + 100);
                var sample = html.Substring(sampleStart, sampleEnd - sampleStart);
                Log4.Debug($"[GraphOneNote] editorRoot 주변 HTML: {sample}");

                // data-id="editorRoot"를 가진 div의 id 속성(generated ID) 추출
                // OneNote generated ID 형식: div:{guid}{number} 또는 다른 형식일 수 있음
                // 더 유연한 패턴: id="..."를 캡처
                var match = Regex.Match(html,
                    @"<div[^>]*\bid=""([^""]+)""[^>]*data-id=""editorRoot""[^>]*>|<div[^>]*data-id=""editorRoot""[^>]*\bid=""([^""]+)""[^>]*>",
                    RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    // 두 캡처 그룹 중 하나가 매칭됨
                    var generatedId = !string.IsNullOrEmpty(match.Groups[1].Value)
                        ? match.Groups[1].Value
                        : match.Groups[2].Value;
                    Log4.Debug($"[GraphOneNote] editorRoot generated ID 찾음: {generatedId}");
                    return generatedId;
                }

                Log4.Debug("[GraphOneNote] editorRoot div의 id 속성을 찾을 수 없음");
                return null;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log4.Warn($"[GraphOneNote] 페이지 콘텐츠 조회 실패: StatusCode={response.StatusCode}, Error={errorContent}");
            }
            return null;
        }
        catch (Exception ex)
        {
            Log4.Warn($"[GraphOneNote] editorRoot generated ID 조회 예외: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 페이지 콘텐츠에서 editorRoot 내용만 추출
    /// editorRoot가 없으면 body 전체 반환
    /// </summary>
    public string ExtractEditorRootContent(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // data-id="editorRoot" div의 내용 추출
        var match = Regex.Match(html,
            @"<div[^>]*data-id=""editorRoot""[^>]*>(.*?)</div>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (match.Success)
        {
            _logger.Debug("editorRoot 콘텐츠 추출: {Length}자", match.Groups[1].Value.Length);
            return match.Groups[1].Value;
        }

        // editorRoot가 없으면 body 전체 반환
        var bodyMatch = Regex.Match(html, @"<body[^>]*>(.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (bodyMatch.Success)
        {
            _logger.Debug("body 콘텐츠 추출 (editorRoot 없음): {Length}자", bodyMatch.Groups[1].Value.Length);
            return bodyMatch.Groups[1].Value;
        }

        return html;
    }

    /// <summary>
    /// 비오디오 &lt;object data-attachment="..."&gt; 태그를 클릭 가능한 📎 링크로 변환
    /// OneNote API에서 첨부파일은 object 태그로 반환되지만 TinyMCE에서는 제대로 렌더링되지 않음
    /// </summary>

    // --- 첨부파일 아이콘 추출 (Win32 SHGetFileInfo) ---

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private static readonly Dictionary<string, string> _아이콘캐시 = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 확장자에 해당하는 Windows 시스템 아이콘을 Base64 PNG로 반환
    /// </summary>
    public static string GetFileIconBase64(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(ext))
            return ""; // 확장자 없으면 빈 문자열 → 폴백 처리

        if (_아이콘캐시.TryGetValue(ext, out var cached))
            return cached;

        try
        {
            var shInfo = new SHFILEINFO();
            var result = SHGetFileInfo(
                $"file{ext}", FILE_ATTRIBUTE_NORMAL, ref shInfo,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(SHFILEINFO)),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

            if (result == IntPtr.Zero || shInfo.hIcon == IntPtr.Zero)
                return "";

            try
            {
                using var icon = System.Drawing.Icon.FromHandle(shInfo.hIcon);
                using var bmp = icon.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var base64 = Convert.ToBase64String(ms.ToArray());
                _아이콘캐시[ext] = base64;
                return base64;
            }
            finally
            {
                DestroyIcon(shInfo.hIcon);
            }
        }
        catch (Exception ex)
        {
            Log4.Debug($"[OneNote] 아이콘 추출 실패: {ext} — {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 첨부파일 카드 HTML 생성 — 아이콘(48px) + 파일명 (확장자 제외)
    /// </summary>
    public static string GenerateAttachmentCardHtml(string fileName, string href = "#", string extraAttrs = "")
    {
        var safeFileName = System.Web.HttpUtility.HtmlEncode(fileName);
        var displayName = System.Web.HttpUtility.HtmlEncode(Path.GetFileNameWithoutExtension(fileName));
        var iconBase64 = GetFileIconBase64(fileName);

        string iconHtml;
        if (!string.IsNullOrEmpty(iconBase64))
        {
            iconHtml = $"<img src=\"data:image/png;base64,{iconBase64}\" width=\"48\" height=\"48\" style=\"display:block;margin:0 auto;\" alt=\"{safeFileName}\" />";
        }
        else
        {
            // 폴백: 이모지 아이콘
            iconHtml = "<span style=\"font-size:48px;display:block;text-align:center;\">📎</span>";
        }

        return $"<div contenteditable=\"false\" style=\"display:inline-block;text-align:center;padding:8px 12px;margin:4px;border:1px solid #e0e0e0;border-radius:8px;background:#f9f9f9;cursor:pointer;vertical-align:top;min-width:80px;max-width:120px;\" title=\"{safeFileName}\" data-attachment=\"{safeFileName}\">"
             + $"<a href=\"{href}\" title=\"{safeFileName}\" data-attachment=\"{safeFileName}\" style=\"text-decoration:none;color:inherit;display:block;\" {extraAttrs}>"
             + iconHtml
             + $"<div style=\"margin-top:4px;font-size:11px;color:#333;word-break:break-all;line-height:1.3;\">{displayName}</div>"
             + "</a></div>";
    }

    public string ConvertAttachmentObjectsToLinks(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // <object ... data-attachment="파일명" ... type="비오디오" ... /> 또는 <object ...>...</object>
        var objectRegex = new Regex(
            @"<object\s+([^>]*)(?:/>|>.*?</object>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var result = objectRegex.Replace(html, match =>
        {
            var attrs = match.Groups[1].Value;

            // 오디오 타입은 기존 오디오 처리 로직에 맡김 (스킵)
            if (attrs.Contains("audio/", StringComparison.OrdinalIgnoreCase))
                return match.Value;

            // data-attachment 속성에서 파일명 추출
            var attachmentMatch = Regex.Match(attrs, @"data-attachment=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!attachmentMatch.Success)
                return match.Value;

            var fileName = attachmentMatch.Groups[1].Value;
            var safeFileName = System.Web.HttpUtility.HtmlEncode(fileName);

            Log4.Debug($"[OneNote] 첨부파일 object→카드 변환: {fileName}");
            return GenerateAttachmentCardHtml(fileName);
        });

        return result;
    }

    /// <summary>
    /// HTML 콘텐츠의 이미지 URL을 Base64 데이터 URL로 변환
    /// Graph API 인증이 필요한 이미지를 인라인으로 변환하여 WebView2에서 표시 가능하게 함
    /// </summary>
    /// <param name="htmlContent">원본 HTML 콘텐츠</param>
    /// <returns>이미지가 Base64로 변환된 HTML</returns>
    public async Task<string> ConvertImagesToBase64Async(string htmlContent)
    {
        if (string.IsNullOrEmpty(htmlContent))
            return htmlContent;

        try
        {
            // Graph API URL 이미지 패턴 (OneNote 리소스)
            // 패턴: src="https://graph.microsoft.com/.../resources/{id}/$value" 또는
            //       src="https://graph.microsoft.com/.../resources/{id}/content"
            var imgRegex = new Regex(
                @"src=""(https://graph\.microsoft\.com[^""]+(?:resources/[^""]+|\$value|/content)[^""]*)""",
                RegexOptions.IgnoreCase);

            var matches = imgRegex.Matches(htmlContent);
            if (matches.Count == 0)
            {
                _logger.Debug("변환할 Graph API 이미지 없음");
                return htmlContent;
            }

            _logger.Debug("변환할 이미지 {Count}개 발견", matches.Count);

            var accessToken = await _authService.GetAccessTokenAsync();
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            foreach (Match match in matches)
            {
                var imageUrl = match.Groups[1].Value;
                try
                {
                    // 이미지 다운로드
                    var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);

                    // MIME 타입 감지
                    var mimeType = DetectMimeType(imageBytes);

                    // Base64 인코딩
                    var base64 = Convert.ToBase64String(imageBytes);
                    var dataUrl = $"data:{mimeType};base64,{base64}";

                    // HTML에서 URL 교체
                    htmlContent = htmlContent.Replace(imageUrl, dataUrl);

                    _logger.Debug("이미지 변환 완료: {Url} -> Base64 ({Size}bytes)",
                        imageUrl.Substring(0, Math.Min(50, imageUrl.Length)) + "...",
                        imageBytes.Length);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "이미지 변환 실패 (원본 유지): {Url}", imageUrl);
                    // 실패 시 원본 URL 유지
                }
            }

            return htmlContent;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "이미지 Base64 변환 실패");
            return htmlContent; // 실패 시 원본 반환
        }
    }

    /// <summary>
    /// OneNote 페이지의 오디오/미디어 리소스 목록 가져오기
    /// 페이지 HTML에서 object 태그로 포함된 미디어를 검색
    /// </summary>
    /// <param name="pageId">페이지 ID</param>
    /// <returns>오디오 리소스 정보 목록</returns>
    public async Task<List<PageAudioResource>> GetPageAudioResourcesAsync(string pageId)
    {
        var resources = new List<PageAudioResource>();

        if (string.IsNullOrEmpty(pageId))
            return resources;

        try
        {
            var accessToken = await _authService.GetAccessTokenAsync();
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            // 페이지 콘텐츠 가져오기
            var url = $"https://graph.microsoft.com/v1.0/me/onenote/pages/{pageId}/content";
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("페이지 오디오 리소스 조회 실패: PageId={PageId}", pageId);
                return resources;
            }

            var html = await response.Content.ReadAsStringAsync();

            Log4.Info($"[OneNote Audio] 페이지 {pageId} HTML 길이: {html?.Length ?? 0}자");

            // 디버깅용: HTML 전체를 파일로 저장
            try
            {
                var debugDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MaiX", "debug");
                Directory.CreateDirectory(debugDir);
                var debugFile = Path.Combine(debugDir, $"page_{pageId.Replace("!", "_").Replace("-", "_")}.html");
                File.WriteAllText(debugFile, html);
                Log4.Info($"[OneNote Audio] HTML 저장됨: {debugFile}");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneNote Audio] HTML 저장 실패: {ex.Message}");
            }

            // HTML 내용 디버깅 (키워드 검색)
            if (!string.IsNullOrEmpty(html))
            {
                var hasAudio = html.Contains("audio", StringComparison.OrdinalIgnoreCase);
                var hasObject = html.Contains("<object", StringComparison.OrdinalIgnoreCase);
                var hasRecording = html.Contains("녹음", StringComparison.OrdinalIgnoreCase) ||
                                   html.Contains("Recording", StringComparison.OrdinalIgnoreCase);
                var hasResource = html.Contains("resources/", StringComparison.OrdinalIgnoreCase);

                Log4.Info($"[OneNote Audio] 키워드 검색: audio={hasAudio}, object={hasObject}, recording={hasRecording}, resources={hasResource}");

                // object 태그 내용 추출 (닫힘 태그 포함 + 자체 닫힘 태그)
                var objectMatches = Regex.Matches(html, @"<object[^>]*(?:>.*?</object>|/>)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                Log4.Info($"[OneNote Audio] object 태그 수 (닫힘+자체닫힘): {objectMatches.Count}");
                foreach (Match m in objectMatches)
                {
                    Log4.Info($"[OneNote Audio] object 태그: {m.Value.Substring(0, Math.Min(500, m.Value.Length))}");
                }

                // object 시작 태그만 찾기 (디버깅용)
                var objectStartTags = Regex.Matches(html, @"<object[^>]*>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                Log4.Info($"[OneNote Audio] object 시작 태그 수: {objectStartTags.Count}");
                foreach (Match m in objectStartTags)
                {
                    Log4.Info($"[OneNote Audio] object 시작 태그: {m.Value.Substring(0, Math.Min(500, m.Value.Length))}");
                }

                // 녹음 관련 텍스트 주변 100자 추출
                if (hasRecording)
                {
                    var idx = html.IndexOf("녹음", StringComparison.OrdinalIgnoreCase);
                    if (idx == -1) idx = html.IndexOf("Recording", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var start = Math.Max(0, idx - 50);
                        var len = Math.Min(200, html.Length - start);
                        Log4.Info($"[OneNote Audio] 녹음 텍스트 주변: {html.Substring(start, len)}");
                    }
                }

                // audio 문자열 주변 컨텍스트 확인
                if (hasAudio)
                {
                    var idx = html.IndexOf("audio", StringComparison.OrdinalIgnoreCase);
                    while (idx >= 0)
                    {
                        var start = Math.Max(0, idx - 50);
                        var len = Math.Min(300, html.Length - start);
                        Log4.Info($"[OneNote Audio] audio 문자열 주변 (위치 {idx}): {html.Substring(start, len)}");
                        idx = html.IndexOf("audio", idx + 1, StringComparison.OrdinalIgnoreCase);
                    }
                }

                // resources/ 문자열 주변 컨텍스트 확인
                if (hasResource)
                {
                    var idx = html.IndexOf("resources/", StringComparison.OrdinalIgnoreCase);
                    while (idx >= 0)
                    {
                        var start = Math.Max(0, idx - 20);
                        var len = Math.Min(200, html.Length - start);
                        Log4.Info($"[OneNote Audio] resources 문자열 (위치 {idx}): {html.Substring(start, len)}");
                        idx = html.IndexOf("resources/", idx + 10, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            // OneNote 오디오 녹음의 다양한 패턴 검색

            // 패턴 1: <object data="..." data-attachment="..." type="audio/...">
            var objectRegex1 = new Regex(
                @"<object[^>]*data=""([^""]+)""[^>]*data-attachment=""([^""]+)""[^>]*type=""(audio/[^""]+)""[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in objectRegex1.Matches(html))
            {
                var resourceUrl = match.Groups[1].Value;
                var fileName = match.Groups[2].Value;
                var mimeType = match.Groups[3].Value;

                var resourceIdMatch = Regex.Match(resourceUrl, @"resources/([^/]+)/");
                var resourceId = resourceIdMatch.Success ? resourceIdMatch.Groups[1].Value : string.Empty;

                Log4.Info($"[OneNote] 패턴1 오디오 발견: {fileName}");
                resources.Add(new PageAudioResource
                {
                    ResourceId = resourceId,
                    ResourceUrl = resourceUrl,
                    FileName = fileName,
                    MimeType = mimeType,
                    PageId = pageId
                });
            }

            // 패턴 2: <object type="audio/..." data="..." data-attachment="..."> (속성 순서 다름)
            var objectRegex2 = new Regex(
                @"<object[^>]*type=""(audio/[^""]+)""[^>]*data=""([^""]+)""[^>]*data-attachment=""([^""]+)""[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in objectRegex2.Matches(html))
            {
                var mimeType = match.Groups[1].Value;
                var resourceUrl = match.Groups[2].Value;
                var fileName = match.Groups[3].Value;

                if (resources.Any(r => r.ResourceUrl == resourceUrl))
                    continue;

                var resourceIdMatch = Regex.Match(resourceUrl, @"resources/([^/]+)/");
                var resourceId = resourceIdMatch.Success ? resourceIdMatch.Groups[1].Value : string.Empty;

                Log4.Info($"[OneNote] 패턴2 오디오 발견: {fileName}");
                resources.Add(new PageAudioResource
                {
                    ResourceId = resourceId,
                    ResourceUrl = resourceUrl,
                    FileName = fileName,
                    MimeType = mimeType,
                    PageId = pageId
                });
            }

            // 패턴 3: 모든 object 태그에서 audio 타입 검색 (더 유연한 패턴 - 자체 닫힘 태그 포함)
            // 자체 닫힘: <object ... />  또는  닫힘 태그: <object ...>...</object>
            var objectRegex3 = new Regex(
                @"<object\s+([^>]*)(?:/>|>.*?</object>)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in objectRegex3.Matches(html))
            {
                var objectAttrs = match.Groups[1].Value; // 속성들만 추출

                Log4.Info($"[OneNote Audio] 패턴3 object 속성: {objectAttrs.Substring(0, Math.Min(300, objectAttrs.Length))}");

                // audio 타입인지 확인
                if (!objectAttrs.Contains("audio/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // data URL 추출
                var dataMatch = Regex.Match(objectAttrs, @"data=""([^""]+)""", RegexOptions.IgnoreCase);
                if (!dataMatch.Success)
                    continue;

                var resourceUrl = dataMatch.Groups[1].Value;
                if (resources.Any(r => r.ResourceUrl == resourceUrl))
                    continue;

                // 파일명 추출
                var attachmentMatch = Regex.Match(objectAttrs, @"data-attachment=""([^""]+)""", RegexOptions.IgnoreCase);
                var fileName = attachmentMatch.Success ? attachmentMatch.Groups[1].Value : "recording.wav";

                // 타입 추출
                var typeMatch = Regex.Match(objectAttrs, @"type=""(audio/[^""]+)""", RegexOptions.IgnoreCase);
                var mimeType = typeMatch.Success ? typeMatch.Groups[1].Value : "audio/wav";

                var resourceIdMatch = Regex.Match(resourceUrl, @"resources/([^/]+)/");
                var resourceId = resourceIdMatch.Success ? resourceIdMatch.Groups[1].Value : string.Empty;

                Log4.Info($"[OneNote Audio] 패턴3 오디오 발견: {fileName}, URL={resourceUrl}");
                resources.Add(new PageAudioResource
                {
                    ResourceId = resourceId,
                    ResourceUrl = resourceUrl,
                    FileName = fileName,
                    MimeType = mimeType,
                    PageId = pageId
                });
            }

            // 패턴 4: 녹음 시작 텍스트가 있는 경우 근처의 리소스 검색
            // "오디오 녹음 시작:" 텍스트 패턴
            if (html.Contains("오디오 녹음") || html.Contains("Audio Recording") || html.Contains("녹음 시작"))
            {
                _logger.Debug("페이지에 오디오 녹음 텍스트 발견, 리소스 검색 중...");

                // resources URL 패턴으로 직접 검색
                var resourceUrlRegex = new Regex(
                    @"https://graph\.microsoft\.com[^""'\s]+resources/([^/""'\s]+)/\$value",
                    RegexOptions.IgnoreCase);

                foreach (Match match in resourceUrlRegex.Matches(html))
                {
                    var resourceUrl = match.Value;
                    var resourceId = match.Groups[1].Value;

                    if (resources.Any(r => r.ResourceId == resourceId))
                        continue;

                    _logger.Debug("패턴4 리소스 URL 발견: {ResourceId}", resourceId);
                    resources.Add(new PageAudioResource
                    {
                        ResourceId = resourceId,
                        ResourceUrl = resourceUrl,
                        FileName = $"recording_{resourceId}.wav",
                        MimeType = "audio/wav",
                        PageId = pageId
                    });
                }
            }

            _logger.Information("페이지 {PageId} 오디오 리소스 {Count}개 발견", pageId, resources.Count);

            // HTML에서 object 태그 샘플 로깅 (디버깅용)
            if (resources.Count == 0)
            {
                var objectTags = Regex.Matches(html, @"<object[^>]*>", RegexOptions.IgnoreCase);
                _logger.Debug("페이지 object 태그 {Count}개 발견", objectTags.Count);
                foreach (Match tag in objectTags.Take(5))
                {
                    _logger.Debug("Object 태그 샘플: {Tag}", tag.Value.Substring(0, Math.Min(200, tag.Value.Length)));
                }
            }
            return resources;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "페이지 오디오 리소스 조회 예외: PageId={PageId}", pageId);
            return resources;
        }
    }

    /// <summary>
    /// OneNote 오디오 리소스를 로컬 파일로 다운로드
    /// </summary>
    /// <param name="resourceUrl">리소스 URL</param>
    /// <param name="fileName">저장할 파일명</param>
    /// <param name="saveDir">저장 디렉토리</param>
    /// <returns>저장된 파일 경로</returns>
    public async Task<string?> DownloadAudioResourceAsync(string resourceUrl, string fileName, string saveDir)
    {
        if (string.IsNullOrEmpty(resourceUrl))
            return null;

        try
        {
            var accessToken = await _authService.GetAccessTokenAsync();
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var bytes = await httpClient.GetByteArrayAsync(resourceUrl);

            if (!Directory.Exists(saveDir))
                Directory.CreateDirectory(saveDir);

            // 안전한 파일명 생성
            var safeFileName = Path.GetInvalidFileNameChars()
                .Aggregate(fileName, (current, c) => current.Replace(c, '_'));
            var filePath = Path.Combine(saveDir, safeFileName);

            // 중복 파일명 처리
            var baseName = Path.GetFileNameWithoutExtension(safeFileName);
            var extension = Path.GetExtension(safeFileName);
            var counter = 1;
            while (File.Exists(filePath))
            {
                safeFileName = $"{baseName}_{counter}{extension}";
                filePath = Path.Combine(saveDir, safeFileName);
                counter++;
            }

            await File.WriteAllBytesAsync(filePath, bytes);
            _logger.Debug("오디오 리소스 다운로드 완료: {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "오디오 리소스 다운로드 실패: {Url}", resourceUrl);
            return null;
        }
    }

    /// <summary>
    /// 바이트 배열에서 MIME 타입 감지
    /// </summary>
    private static string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length < 4)
            return "image/png";

        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        // GIF: 47 49 46 38
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return "image/gif";

        // BMP: 42 4D
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            return "image/bmp";

        // WebP: 52 49 46 46 ... 57 45 42 50
        if (bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
        {
            if (bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return "image/webp";
        }

        // 기본값
        return "image/png";
    }

    /// <summary>
    /// Graph API로 페이지 제목 검색 (개인 + 그룹 + 사이트 병렬)
    /// </summary>
    /// <param name="query">검색어</param>
    /// <param name="groupIds">검색할 그룹 ID 목록</param>
    /// <param name="siteIds">검색할 사이트 ID 목록</param>
    /// <returns>검색 결과 페이지 목록</returns>
    public async Task<List<OnenotePage>> SearchPagesAsync(string query, IEnumerable<string> groupIds, IEnumerable<string> siteIds)
    {
        var allPages = new List<OnenotePage>();
        var client = _authService.GetGraphClient();
        var escapedQuery = query.ToLower().Replace("'", "''");
        var pageFilter = $"contains(tolower(title),'{escapedQuery}')";

        // 1단계: 각 영역에서 전체 섹션 목록 수집 (병렬)
        var sectionTasks = new List<Task<List<(string sectionId, string? groupId, string? siteId)>>>();

        // 개인 노트북 섹션
        sectionTasks.Add(Task.Run(async () =>
        {
            var result = new List<(string sectionId, string? groupId, string? siteId)>();
            try
            {
                var response = await client.Me.Onenote.Sections.GetAsync(config =>
                {
                    config.QueryParameters.Top = 100;
                    config.QueryParameters.Select = new[] { "id" };
                });
                if (response?.Value != null)
                {
                    foreach (var s in response.Value.Where(s => !string.IsNullOrEmpty(s.Id)))
                        result.Add((s.Id!, null, null));
                }
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneNote검색] 개인 섹션 목록 조회 실패: {ex.Message}");
            }
            return result;
        }));

        // 그룹별 섹션
        foreach (var groupId in groupIds.Where(id => !string.IsNullOrEmpty(id)).Distinct())
        {
            var gid = groupId;
            sectionTasks.Add(Task.Run(async () =>
            {
                var result = new List<(string sectionId, string? groupId, string? siteId)>();
                try
                {
                    var response = await client.Groups[gid].Onenote.Sections.GetAsync(config =>
                    {
                        config.QueryParameters.Top = 100;
                        config.QueryParameters.Select = new[] { "id" };
                    });
                    if (response?.Value != null)
                    {
                        foreach (var s in response.Value.Where(s => !string.IsNullOrEmpty(s.Id)))
                            result.Add((s.Id!, gid, null));
                    }
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[OneNote검색] 그룹 '{gid}' 섹션 목록 조회 실패: {ex.Message}");
                }
                return result;
            }));
        }

        // 사이트별 섹션
        foreach (var siteId in siteIds.Where(id => !string.IsNullOrEmpty(id)).Distinct())
        {
            var sid = siteId;
            sectionTasks.Add(Task.Run(async () =>
            {
                var result = new List<(string sectionId, string? groupId, string? siteId)>();
                try
                {
                    var response = await client.Sites[sid].Onenote.Sections.GetAsync(config =>
                    {
                        config.QueryParameters.Top = 100;
                        config.QueryParameters.Select = new[] { "id" };
                    });
                    if (response?.Value != null)
                    {
                        foreach (var s in response.Value.Where(s => !string.IsNullOrEmpty(s.Id)))
                            result.Add((s.Id!, null, sid));
                    }
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[OneNote검색] 사이트 '{sid}' 섹션 목록 조회 실패: {ex.Message}");
                }
                return result;
            }));
        }

        var sectionResults = await Task.WhenAll(sectionTasks);
        var allSections = sectionResults.SelectMany(s => s).ToList();
        Log4.Info($"[OneNote검색] 총 {allSections.Count}개 섹션에서 '{query}' 검색 시작");

        // 2단계: 각 섹션에서 페이지 검색 (병렬, 동시 10개 제한)
        var semaphore = new SemaphoreSlim(10);
        var pageTasks = allSections.Select(async section =>
        {
            await semaphore.WaitAsync();
            try
            {
                if (section.groupId != null)
                {
                    var response = await client.Groups[section.groupId].Onenote.Sections[section.sectionId].Pages.GetAsync(config =>
                    {
                        config.QueryParameters.Filter = pageFilter;
                        config.QueryParameters.Top = 20;
                        config.QueryParameters.Expand = new[] { "parentSection($select=id,displayName)", "parentNotebook($select=id,displayName)" };
                    });
                    return response?.Value?.ToList() ?? new List<OnenotePage>();
                }
                else if (section.siteId != null)
                {
                    var response = await client.Sites[section.siteId].Onenote.Sections[section.sectionId].Pages.GetAsync(config =>
                    {
                        config.QueryParameters.Filter = pageFilter;
                        config.QueryParameters.Top = 20;
                        config.QueryParameters.Expand = new[] { "parentSection($select=id,displayName)", "parentNotebook($select=id,displayName)" };
                    });
                    return response?.Value?.ToList() ?? new List<OnenotePage>();
                }
                else
                {
                    var response = await client.Me.Onenote.Sections[section.sectionId].Pages.GetAsync(config =>
                    {
                        config.QueryParameters.Filter = pageFilter;
                        config.QueryParameters.Top = 20;
                        config.QueryParameters.Expand = new[] { "parentSection($select=id,displayName)", "parentNotebook($select=id,displayName)" };
                    });
                    return response?.Value?.ToList() ?? new List<OnenotePage>();
                }
            }
            catch (Exception ex)
            {
                Log4.Debug($"[OneNote검색] 섹션 '{section.sectionId}' 페이지 검색 실패: {ex.Message}");
                return new List<OnenotePage>();
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var pageResults = await Task.WhenAll(pageTasks);
        foreach (var pages in pageResults)
        {
            allPages.AddRange(pages);
        }

        Log4.Info($"[OneNote검색] 페이지 검색 완료: {allPages.Count}개 발견");

        // 중복 제거 (같은 페이지가 여러 경로로 조회될 수 있음)
        return allPages.GroupBy(p => p.Id).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// 섹션 이름으로 검색 (개인/그룹/사이트 병렬)
    /// </summary>
    public async Task<List<OnenoteSection>> SearchSectionsAsync(string query, IEnumerable<string> groupIds, IEnumerable<string> siteIds)
    {
        var allSections = new List<OnenoteSection>();
        var client = _authService.GetGraphClient();
        var escapedQuery = query.ToLower().Replace("'", "''");
        var sectionFilter = $"contains(tolower(name),'{escapedQuery}')";

        var tasks = new List<Task<List<OnenoteSection>>>();

        // 개인 노트북 섹션 검색
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                var response = await client.Me.Onenote.Sections.GetAsync(config =>
                {
                    config.QueryParameters.Filter = sectionFilter;
                    config.QueryParameters.Top = 50;
                    config.QueryParameters.Expand = new[] { "parentNotebook($select=id,displayName)" };
                });
                return response?.Value?.ToList() ?? new List<OnenoteSection>();
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneNote검색] 개인 섹션 검색 실패: {ex.Message}");
                return new List<OnenoteSection>();
            }
        }));

        // 그룹별 섹션 검색
        foreach (var groupId in groupIds.Where(id => !string.IsNullOrEmpty(id)).Distinct())
        {
            var gid = groupId;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var response = await client.Groups[gid].Onenote.Sections.GetAsync(config =>
                    {
                        config.QueryParameters.Filter = sectionFilter;
                        config.QueryParameters.Top = 50;
                        config.QueryParameters.Expand = new[] { "parentNotebook($select=id,displayName)" };
                    });
                    return response?.Value?.ToList() ?? new List<OnenoteSection>();
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[OneNote검색] 그룹 '{gid}' 섹션 검색 실패: {ex.Message}");
                    return new List<OnenoteSection>();
                }
            }));
        }

        // 사이트별 섹션 검색
        foreach (var siteId in siteIds.Where(id => !string.IsNullOrEmpty(id)).Distinct())
        {
            var sid = siteId;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var response = await client.Sites[sid].Onenote.Sections.GetAsync(config =>
                    {
                        config.QueryParameters.Filter = sectionFilter;
                        config.QueryParameters.Top = 50;
                        config.QueryParameters.Expand = new[] { "parentNotebook($select=id,displayName)" };
                    });
                    return response?.Value?.ToList() ?? new List<OnenoteSection>();
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[OneNote검색] 사이트 '{sid}' 섹션 검색 실패: {ex.Message}");
                    return new List<OnenoteSection>();
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        foreach (var sections in results)
        {
            allSections.AddRange(sections);
        }

        Log4.Info($"[OneNote검색] 섹션 검색 완료: '{query}' → {allSections.Count}개 발견");

        // 중복 제거
        return allSections.GroupBy(s => s.Id).Select(g => g.First()).ToList();
    }
}

/// <summary>
/// 출처 정보가 포함된 노트북
/// </summary>
public class NotebookWithSource
{
    /// <summary>
    /// 노트북 정보
    /// </summary>
    public Notebook Notebook { get; set; } = new();

    /// <summary>
    /// 노트북 출처
    /// </summary>
    public NotebookSource Source { get; set; }

    /// <summary>
    /// 출처 이름 (그룹/사이트 이름)
    /// </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// 그룹 ID (그룹 노트북인 경우)
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// 사이트 ID (사이트 노트북인 경우)
    /// </summary>
    public string SiteId { get; set; } = string.Empty;
}

/// <summary>
/// 노트북 출처
/// </summary>
public enum NotebookSource
{
    /// <summary>
    /// 개인 노트북
    /// </summary>
    Personal,

    /// <summary>
    /// Microsoft 365 그룹 노트북
    /// </summary>
    Group,

    /// <summary>
    /// SharePoint 사이트 노트북
    /// </summary>
    Site
}

/// <summary>
/// OneNote 페이지의 오디오 리소스 정보
/// </summary>
public class PageAudioResource
{
    /// <summary>
    /// 리소스 ID
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// 리소스 URL (다운로드용)
    /// </summary>
    public string ResourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// 원본 파일명
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME 타입 (audio/wav, audio/mp3 등)
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// 페이지 ID
    /// </summary>
    public string PageId { get; set; } = string.Empty;
}
