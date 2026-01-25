using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mailX.Data;
using mailX.Utils;
using Serilog;

// 모호한 참조 해결을 위한 별칭
using MailXTodo = mailX.Models.Todo;
using MailXEmail = mailX.Models.Email;
using MailXOneNotePage = mailX.Models.OneNotePage;

namespace mailX.Services.Graph;

/// <summary>
/// Microsoft OneNote 연동 서비스
/// </summary>
public class GraphOneNoteService
{
    private readonly GraphAuthService _authService;
    private readonly MailXDbContext _dbContext;
    private readonly ILogger _logger;

    public GraphOneNoteService(GraphAuthService authService, MailXDbContext dbContext)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = Log.ForContext<GraphOneNoteService>();
    }

    /// <summary>
    /// 노트북 목록 조회
    /// </summary>
    /// <returns>노트북 목록</returns>
    public async Task<IEnumerable<Notebook>> GetNotebooksAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Onenote.Notebooks.GetAsync();

            _logger.Debug("노트북 {Count}개 조회", response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Notebook>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "노트북 목록 조회 실패");
            throw;
        }
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
        if (string.IsNullOrEmpty(pageId))
            throw new ArgumentNullException(nameof(pageId));

        try
        {
            var client = _authService.GetGraphClient();
            var contentStream = await client.Me.Onenote.Pages[pageId].Content.GetAsync();

            if (contentStream == null)
                return null;

            using var reader = new System.IO.StreamReader(contentStream);
            var content = await reader.ReadToEndAsync();

            _logger.Debug("페이지 {PageId} 내용 조회 완료", pageId);
            return content;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 내용 조회 실패: PageId={PageId}", pageId);
            throw;
        }
    }

    /// <summary>
    /// 이메일 내용으로 새 페이지 생성
    /// </summary>
    /// <param name="sectionId">섹션 ID</param>
    /// <param name="email">이메일 정보</param>
    /// <returns>생성된 페이지</returns>
    public async Task<OnenotePage?> CreatePageFromEmailAsync(string sectionId, MailXEmail email)
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
    public async Task<OnenotePage?> CreatePageFromTodoAsync(string sectionId, MailXTodo todo)
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
    public async Task<MailXOneNotePage> SavePageAsync(OnenotePage onenotePage, int? linkedEmailId = null)
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
                var page = new MailXOneNotePage
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
    public async Task<IEnumerable<MailXOneNotePage>> GetLinkedPagesAsync(int emailId)
    {
        return await _dbContext.OneNotePages
            .Where(p => p.LinkedEmailId == emailId)
            .OrderByDescending(p => p.CreatedDateTime)
            .ToListAsync();
    }

    /// <summary>
    /// 이메일 HTML 콘텐츠 생성
    /// </summary>
    private string BuildEmailHtmlContent(MailXEmail email)
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
    private string BuildTodoHtmlContent(MailXTodo todo)
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

            // OneNote 페이지 생성 시 제목만 설정
            var newPage = new OnenotePage
            {
                Title = title
            };

            var response = await client.Me.Onenote.Sections[sectionId].Pages.PostAsync(newPage);

            _logger.Information("페이지 생성 완료: {Title}", title);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 생성 실패: Title={Title}", title);
            throw;
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
}
