using mAIx.Models;
using mAIx.Utils;

namespace mAIx.Services;

/// <summary>
/// 커맨드 팔레트 — 등록된 명령 관리 + 퍼지 검색
/// </summary>
public class CommandPaletteService
{
    private readonly List<CommandPaletteItem> _commands = new();

    public void RegisterCommand(CommandPaletteItem item)
    {
        _commands.Add(item);
    }

    /// <summary>
    /// 기본 명령 일괄 등록 — 각 Execute 액션은 호출자(MainWindow)가 제공
    /// </summary>
    public void RegisterDefaultCommands(
        Action openNewMail,
        Action openReply,
        Action openForward,
        Action deleteEmail,
        Action markAsRead,
        Action moveToFolder,
        Action openSettings,
        Action startSync,
        Action openAutoReply,
        Action openQuickStep)
    {
        _commands.Clear();

        _commands.Add(new CommandPaletteItem
        {
            Id = "compose.new",
            Title = "새 메일 작성",
            Description = "새 메일을 작성합니다 (Ctrl+N)",
            Category = "작성",
            Icon = "Mail24",
            Keywords = "새 메일 작성 compose new ctrl+n",
            Execute = openNewMail
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "email.reply",
            Title = "답장",
            Description = "선택된 메일에 답장합니다 (R)",
            Category = "이메일",
            Icon = "ArrowReply24",
            Keywords = "답장 reply r",
            Execute = openReply
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "email.forward",
            Title = "전달",
            Description = "선택된 메일을 전달합니다 (F)",
            Category = "이메일",
            Icon = "ArrowForward24",
            Keywords = "전달 forward f",
            Execute = openForward
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "email.delete",
            Title = "삭제",
            Description = "선택된 메일을 삭제합니다 (D)",
            Category = "이메일",
            Icon = "Delete24",
            Keywords = "삭제 delete d",
            Execute = deleteEmail
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "email.markRead",
            Title = "읽음으로 표시",
            Description = "선택된 메일을 읽음으로 표시합니다 (U)",
            Category = "이메일",
            Icon = "MailRead24",
            Keywords = "읽음 읽음표시 mark read u",
            Execute = markAsRead
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "email.move",
            Title = "메일 폴더 이동",
            Description = "선택된 메일을 다른 폴더로 이동합니다",
            Category = "이메일",
            Icon = "FolderArrowRight24",
            Keywords = "이동 폴더 move folder",
            Execute = moveToFolder
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "app.settings",
            Title = "설정 열기",
            Description = "앱 설정을 엽니다",
            Category = "설정",
            Icon = "Settings24",
            Keywords = "설정 settings options preferences",
            Execute = openSettings
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "sync.start",
            Title = "동기화 시작",
            Description = "메일 동기화를 시작합니다",
            Category = "설정",
            Icon = "ArrowSync24",
            Keywords = "동기화 sync refresh 새로고침",
            Execute = startSync
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "autoReply.setup",
            Title = "부재중 설정",
            Description = "자동 응답(부재중) 설정을 엽니다",
            Category = "설정",
            Icon = "CalendarClock24",
            Keywords = "부재중 자동응답 out of office auto reply",
            Execute = openAutoReply
        });

        _commands.Add(new CommandPaletteItem
        {
            Id = "quickstep.run",
            Title = "퀵스텝 실행",
            Description = "퀵스텝 목록에서 실행합니다",
            Category = "작성",
            Icon = "Flash24",
            Keywords = "퀵스텝 quickstep 빠른실행 quick step",
            Execute = openQuickStep
        });
    }

    /// <summary>
    /// 퍼지 검색 — Title, Keywords, Category 대상
    /// </summary>
    public List<CommandPaletteItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _commands.ToList();

        var lower = query.ToLower();
        var terms = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return _commands
            .Select(cmd => (cmd, score: Score(cmd, terms)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.cmd)
            .ToList();
    }

    private static int Score(CommandPaletteItem cmd, string[] terms)
    {
        var titleLower = cmd.Title.ToLower();
        var keywordsLower = cmd.Keywords.ToLower();
        var categoryLower = cmd.Category.ToLower();

        int total = 0;
        foreach (var term in terms)
        {
            if (titleLower.StartsWith(term)) total += 10;
            else if (titleLower.Contains(term)) total += 6;
            else if (keywordsLower.Contains(term)) total += 4;
            else if (categoryLower.Contains(term)) total += 2;
            else
            {
                // bigram 유사도 폴백 — 정확한 부분 문자열 매칭 실패 시
                var bigramScore = Math.Max(
                    BigramHelper.Score(term, titleLower),
                    BigramHelper.Score(term, keywordsLower));
                if (bigramScore >= 0.3)
                    total += (int)(bigramScore * 5);
                else
                    return 0;
            }
        }
        return total;
    }
}
