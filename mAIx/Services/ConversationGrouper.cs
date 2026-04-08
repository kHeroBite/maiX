using System;
using System.Collections.Generic;
using System.Linq;
using mAIx.Models;

namespace mAIx.Services;

/// <summary>
/// 이메일을 대화(ConversationId) 기준으로 그룹핑
/// </summary>
public class ConversationGrouper
{
    /// <summary>
    /// ConversationId 기준 그룹핑, 각 그룹을 ConversationThread로 반환
    /// </summary>
    public List<ConversationThread> GroupByConversation(IEnumerable<Email> emails)
    {
        return emails
            .GroupBy(e => e.ConversationId ?? e.InternetMessageId ?? e.Id.ToString())
            .Select(g => new ConversationThread
            {
                ConversationId = g.Key,
                Subject = g.OrderByDescending(e => e.ReceivedDateTime).First().Subject,
                Emails = g.OrderByDescending(e => e.ReceivedDateTime).ToList()
            })
            .OrderByDescending(t => t.LastActivityAt)
            .ToList();
    }

    /// <summary>
    /// 스레드 깊이 계산 (답장 체인 길이)
    /// </summary>
    public int GetThreadDepth(ConversationThread thread)
    {
        return thread.Emails.Count;
    }
}

/// <summary>
/// 대화 스레드 — 동일 ConversationId를 가진 이메일 묶음
/// </summary>
public class ConversationThread
{
    public string ConversationId { get; set; } = "";
    public string Subject { get; set; } = "";
    public List<Email> Emails { get; set; } = new();
    public Email LatestEmail => Emails.OrderByDescending(e => e.ReceivedDateTime).First();
    public int UnreadCount => Emails.Count(e => !e.IsRead);
    public bool HasUnread => UnreadCount > 0;
    public DateTime LastActivityAt => LatestEmail.ReceivedDateTime ?? DateTime.MinValue;
}
