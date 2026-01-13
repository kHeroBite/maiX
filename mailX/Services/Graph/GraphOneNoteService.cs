using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mailX.Data;
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
}
