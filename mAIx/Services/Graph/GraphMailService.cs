using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Serilog;

namespace mAIx.Services.Graph
{
    /// <summary>
    /// Microsoft Graph 메일 서비스
    /// </summary>
    public class GraphMailService
    {
        private readonly GraphAuthService _authService;
        private readonly ILogger _logger;
        private const int MaxRetryCount = 3;

        public GraphMailService(GraphAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _logger = Log.ForContext<GraphMailService>();
        }

        /// <summary>
        /// Graph API 호출 시 429 Too Many Requests 처리 (Retry-After + Exponential Backoff)
        /// </summary>
        private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, ILogger logger, CancellationToken ct = default)
        {
            for (int attempt = 0; attempt <= MaxRetryCount; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (ApiException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
                {
                    if (attempt >= MaxRetryCount)
                    {
                        logger.Error(ex, "Graph API 429 — 최대 재시도 {Max}회 소진", MaxRetryCount);
                        throw;
                    }

                    // Retry-After 헤더 파싱 (없으면 지수 백오프)
                    int waitSeconds;
                    if (ex.ResponseHeaders != null &&
                        ex.ResponseHeaders.TryGetValue("Retry-After", out var retryAfterValues))
                    {
                        var retryAfter = string.Join(",", retryAfterValues);
                        waitSeconds = int.TryParse(retryAfter, out var parsed) ? parsed : (int)Math.Pow(2, attempt + 1);
                    }
                    else
                    {
                        waitSeconds = (int)Math.Pow(2, attempt + 1);  // 2s, 4s, 8s
                    }

                    logger.Warning("Graph API 429 — {Wait}초 대기 후 재시도 ({Attempt}/{Max})", waitSeconds, attempt + 1, MaxRetryCount);
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
                }
            }

            // 도달 불가 (컴파일러 만족용)
            throw new InvalidOperationException("ExecuteWithRetryAsync: 예상치 못한 종료");
        }

        /// <summary>
        /// 현재 로그인된 사용자 이메일
        /// </summary>
        public string? CurrentUserEmail => _authService.CurrentUserEmail;

        /// <summary>
        /// 메일 폴더 목록 조회 (하위 폴더 포함, 페이징 처리)
        /// </summary>
        /// <returns>모든 폴더 목록 (최상위 + 하위 폴더)</returns>
        public async Task<IEnumerable<MailFolder>> GetFoldersAsync(CancellationToken ct = default)
        {
            var client = _authService.GetGraphClient();
            var allFolders = new List<MailFolder>();

            // 최상위 폴더 조회 (페이징 처리, 429 재시도 포함)
            var response = await ExecuteWithRetryAsync(() => client.Me.MailFolders.GetAsync(config =>
            {
                config.QueryParameters.Top = 100; // 한 번에 최대 100개
            }), _logger, ct);

            // 모든 최상위 폴더 수집 (페이징)
            var topLevelFolders = new List<MailFolder>();
            while (response != null)
            {
                if (response.Value != null)
                {
                    topLevelFolders.AddRange(response.Value);
                }

                // 다음 페이지가 있으면 계속 조회
                if (!string.IsNullOrEmpty(response.OdataNextLink))
                {
                    response = await client.Me.MailFolders
                        .WithUrl(response.OdataNextLink)
                        .GetAsync();
                }
                else
                {
                    break;
                }
            }

            // 각 폴더와 하위 폴더 재귀 수집
            foreach (var folder in topLevelFolders)
            {
                allFolders.Add(folder);

                // 하위 폴더 재귀 조회
                if (folder.Id != null)
                {
                    await GetChildFoldersRecursiveAsync(client, folder.Id, allFolders);
                }
            }

            return allFolders;
        }

        /// <summary>
        /// 하위 폴더 재귀 조회 (페이징 처리)
        /// </summary>
        private async Task GetChildFoldersRecursiveAsync(GraphServiceClient client, string parentFolderId, List<MailFolder> allFolders)
        {
            try
            {
                var response = await client.Me.MailFolders[parentFolderId].ChildFolders.GetAsync(config =>
                {
                    config.QueryParameters.Top = 100;
                });

                // 페이징 처리
                while (response != null)
                {
                    var childFolders = response.Value ?? new List<MailFolder>();

                    foreach (var child in childFolders)
                    {
                        allFolders.Add(child);

                        // 모든 폴더에 대해 하위 폴더 조회 시도
                        if (child.Id != null)
                        {
                            await GetChildFoldersRecursiveAsync(client, child.Id, allFolders);
                        }
                    }

                    // 다음 페이지가 있으면 계속 조회
                    if (!string.IsNullOrEmpty(response.OdataNextLink))
                    {
                        response = await client.Me.MailFolders[parentFolderId].ChildFolders
                            .WithUrl(response.OdataNextLink)
                            .GetAsync();
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch
            {
                // 하위 폴더 조회 실패 시 무시 (권한 없는 폴더 등)
            }
        }

        /// <summary>
        /// 메일 목록 조회
        /// </summary>
        /// <param name="folderId">폴더 ID (null이면 받은편지함)</param>
        /// <param name="top">조회할 메일 수</param>
        /// <returns>메일 목록</returns>
        public async Task<IEnumerable<Message>> GetMessagesAsync(string folderId = null, int top = 50)
        {
            var client = _authService.GetGraphClient();

            // 필요한 필드 명시적 선택 (IsRead, Flag, Importance 포함)
            var selectFields = new[] {
                "id", "internetMessageId", "conversationId", "subject", "body",
                "from", "toRecipients", "ccRecipients", "receivedDateTime",
                "isRead", "flag", "importance", "hasAttachments"
            };

            if (string.IsNullOrEmpty(folderId))
            {
                // 받은편지함
                var response = await client.Me.MailFolders["Inbox"].Messages.GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = selectFields;
                    config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                });
                return response?.Value ?? new List<Message>();
            }
            else
            {
                var response = await client.Me.MailFolders[folderId].Messages.GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Select = selectFields;
                    config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                });
                return response?.Value ?? new List<Message>();
            }
        }

        /// <summary>
        /// Delta Query로 변경된 메일만 조회
        /// </summary>
        /// <param name="folderId">폴더 ID</param>
        /// <param name="deltaLink">이전 동기화의 deltaLink (null이면 초기 동기화)</param>
        /// <returns>변경된 메일 목록, 새 deltaLink, 삭제된 메일 ID 목록</returns>
        public async Task<(IEnumerable<Message> Messages, string? DeltaLink, IEnumerable<string> DeletedIds)>
            GetMessagesDeltaAsync(string folderId, string? deltaLink = null)
        {
            var client = _authService.GetGraphClient();
            var messages = new List<Message>();
            var deletedIds = new List<string>();
            string? newDeltaLink = null;

            try
            {
                // 필요한 필드 명시적 선택 (categories, parentFolderId 추가)
                var selectFields = new[] {
                    "id", "internetMessageId", "conversationId", "subject", "body",
                    "from", "toRecipients", "ccRecipients", "bccRecipients", "receivedDateTime",
                    "isRead", "flag", "importance", "hasAttachments", "categories", "parentFolderId"
                };

                Microsoft.Graph.Me.MailFolders.Item.Messages.Delta.DeltaGetResponse? response;

                if (string.IsNullOrEmpty(deltaLink))
                {
                    // 초기 동기화: 최근 50개 메일부터 시작 (429 재시도 포함)
                    response = await ExecuteWithRetryAsync(() =>
                        client.Me.MailFolders[folderId].Messages.Delta.GetAsDeltaGetResponseAsync(config =>
                        {
                            config.QueryParameters.Select = selectFields;
                            config.QueryParameters.Top = 50;
                        }), _logger);
                }
                else
                {
                    // 이전 deltaLink로 변경분만 조회 (429 재시도 포함)
                    response = await ExecuteWithRetryAsync(() =>
                        client.Me.MailFolders[folderId].Messages.Delta
                            .WithUrl(deltaLink)
                            .GetAsDeltaGetResponseAsync(), _logger);
                }

                // 페이징 처리
                while (response != null)
                {
                    if (response.Value != null)
                    {
                        foreach (var message in response.Value)
                        {
                            // 삭제된 항목 확인 (@removed 속성)
                            if (message.AdditionalData?.ContainsKey("@removed") == true)
                            {
                                if (!string.IsNullOrEmpty(message.Id))
                                    deletedIds.Add(message.Id);
                            }
                            else
                            {
                                messages.Add(message);
                            }
                        }
                    }

                    // deltaLink 저장 (최종 페이지에 포함됨)
                    if (!string.IsNullOrEmpty(response.OdataDeltaLink))
                    {
                        newDeltaLink = response.OdataDeltaLink;
                        break;
                    }

                    // 다음 페이지 조회
                    if (!string.IsNullOrEmpty(response.OdataNextLink))
                    {
                        response = await client.Me.MailFolders[folderId].Messages.Delta
                            .WithUrl(response.OdataNextLink)
                            .GetAsDeltaGetResponseAsync();
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // deltaLink가 만료되었거나 유효하지 않은 경우 초기 동기화로 폴백
                if (ex.Message.Contains("resync", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("syncStateNotFound", StringComparison.OrdinalIgnoreCase))
                {
                    return await GetMessagesDeltaAsync(folderId, null);
                }
                throw;
            }

            return (messages, newDeltaLink, deletedIds);
        }

        /// <summary>
        /// 최신 메일 직접 조회 (Delta API 지연 보완용)
        /// receivedDateTime 내림차순으로 최근 N개 메일 조회
        /// </summary>
        /// <param name="folderId">폴더 ID</param>
        /// <param name="count">조회할 메일 수 (기본 5개)</param>
        /// <param name="since">이 시간 이후 메일만 조회 (null이면 전체)</param>
        /// <returns>최신 메일 목록</returns>
        public async Task<IEnumerable<Message>> GetLatestMessagesAsync(
            string folderId,
            int count = 5,
            DateTime? since = null,
            CancellationToken ct = default)
        {
            var client = _authService.GetGraphClient();

            // 429 재시도 포함
            var response = await ExecuteWithRetryAsync(() =>
                client.Me.MailFolders[folderId].Messages
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Select = new[]
                        {
                            "id", "internetMessageId", "conversationId", "subject",
                            "from", "toRecipients", "ccRecipients", "bccRecipients",
                            "receivedDateTime", "isRead", "flag", "importance",
                            "hasAttachments", "categories", "parentFolderId", "body"
                        };
                        config.QueryParameters.Top = count;
                        config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };

                        // since 파라미터가 있으면 해당 시간 이후 메일만 조회
                        if (since.HasValue)
                        {
                            var sinceUtc = since.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                            config.QueryParameters.Filter = $"receivedDateTime ge {sinceUtc}";
                        }
                    }), _logger, ct);

            return response?.Value ?? new List<Message>();
        }

        /// <summary>
        /// 최근 N일간 메일의 읽음 상태만 조회 (경량 동기화용)
        /// id와 isRead만 가져와서 API 호출 비용 최소화
        /// </summary>
        /// <param name="folderId">폴더 ID</param>
        /// <param name="days">조회할 일수 (기본 7일)</param>
        /// <returns>메일 ID와 읽음 상태 목록</returns>
        public async Task<IEnumerable<(string Id, bool IsRead)>> GetMessagesReadStatusAsync(
            string folderId,
            int days = 7)
        {
            var client = _authService.GetGraphClient();
            var result = new List<(string Id, bool IsRead)>();

            var sinceDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ssZ");

            var response = await client.Me.MailFolders[folderId].Messages
                .GetAsync(config =>
                {
                    // 최소한의 필드만 선택 (id, isRead)
                    config.QueryParameters.Select = new[] { "id", "isRead" };
                    config.QueryParameters.Top = 100;  // 한 번에 최대 100개
                    config.QueryParameters.Filter = $"receivedDateTime ge {sinceDate}";
                    config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                });

            // 페이징 처리
            while (response != null)
            {
                if (response.Value != null)
                {
                    foreach (var msg in response.Value)
                    {
                        if (!string.IsNullOrEmpty(msg.Id))
                        {
                            result.Add((msg.Id, msg.IsRead ?? false));
                        }
                    }
                }

                // 다음 페이지 조회
                if (!string.IsNullOrEmpty(response.OdataNextLink))
                {
                    response = await client.Me.MailFolders[folderId].Messages
                        .WithUrl(response.OdataNextLink)
                        .GetAsync();
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// 단일 메일 조회
        /// </summary>
        /// <param name="messageId">메일 ID</param>
        /// <returns>메일 상세 정보</returns>
        public async Task<Message> GetMessageAsync(string messageId)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            var client = _authService.GetGraphClient();
            return await client.Me.Messages[messageId].GetAsync();
        }

        /// <summary>
        /// 메일 발송
        /// </summary>
        /// <param name="message">발송할 메일</param>
        public async Task SendMessageAsync(Message message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var client = _authService.GetGraphClient();
            await client.Me.SendMail.PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });
        }

        /// <summary>
        /// 대용량 첨부파일 업로드 (3MB 초과, createUploadSession 사용)
        /// </summary>
        public async Task UploadLargeAttachmentAsync(string messageId, string filePath, string fileName)
        {
            if (string.IsNullOrEmpty(messageId))
                throw new ArgumentNullException(nameof(messageId));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            var client = _authService.GetGraphClient();
            var fileInfo = new System.IO.FileInfo(filePath);

            // Upload Session 생성
            var attachmentItem = new AttachmentItem
            {
                AttachmentType = AttachmentType.File,
                Name = fileName,
                Size = fileInfo.Length
            };

            var uploadSession = await client.Me.Messages[messageId]
                .Attachments.CreateUploadSession
                .PostAsync(new Microsoft.Graph.Me.Messages.Item.Attachments.CreateUploadSession.CreateUploadSessionPostRequestBody
                {
                    AttachmentItem = attachmentItem
                });

            if (uploadSession?.UploadUrl == null)
                throw new InvalidOperationException("Upload session 생성 실패");

            // 청크 업로드 (3.25MB 단위)
            const int chunkSize = 3 * 1024 * 1024 + 256 * 1024; // 3.25MB
            using var fileStream = System.IO.File.OpenRead(filePath);
            var fileSize = fileStream.Length;
            var buffer = new byte[chunkSize];
            long offset = 0;

            using var httpClient = new System.Net.Http.HttpClient();

            while (offset < fileSize)
            {
                var bytesRead = await fileStream.ReadAsync(buffer, 0, (int)Math.Min(chunkSize, fileSize - offset));
                var content = new System.Net.Http.ByteArrayContent(buffer, 0, bytesRead);
                content.Headers.Add("Content-Range", $"bytes {offset}-{offset + bytesRead - 1}/{fileSize}");
                content.Headers.ContentLength = bytesRead;

                var response = await httpClient.PutAsync(uploadSession.UploadUrl, content);
                response.EnsureSuccessStatusCode();

                offset += bytesRead;
            }
        }

        /// <summary>
        /// Draft 메시지 발송
        /// </summary>
        public async Task SendDraftMessageAsync(string messageId)
        {
            if (string.IsNullOrEmpty(messageId))
                throw new ArgumentNullException(nameof(messageId));

            var client = _authService.GetGraphClient();
            await client.Me.Messages[messageId].Send.PostAsync();
        }

        /// <summary>
        /// 임시보관함에 메일 저장
        /// </summary>
        /// <param name="message">저장할 메일</param>
        /// <returns>저장된 메시지</returns>
        public async Task<Message?> SaveDraftAsync(Message message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var client = _authService.GetGraphClient();
            // 임시보관함(Drafts)에 메시지 저장
            var savedMessage = await client.Me.Messages.PostAsync(message);
            return savedMessage;
        }

        /// <summary>
        /// 기존 임시보관함 메일 업데이트
        /// </summary>
        /// <param name="messageId">업데이트할 메시지 ID</param>
        /// <param name="message">업데이트할 내용</param>
        /// <returns>업데이트된 메시지</returns>
        public async Task<Message?> UpdateDraftAsync(string messageId, Message message)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var client = _authService.GetGraphClient();
            // 기존 드래프트 메시지 업데이트
            var updatedMessage = await client.Me.Messages[messageId].PatchAsync(message);
            return updatedMessage;
        }

        /// <summary>
        /// 메시지의 플래그 상태 업데이트
        /// </summary>
        /// <param name="messageId">메시지 ID (EntryId)</param>
        /// <param name="flagStatus">플래그 상태 (flagged, complete, notFlagged)</param>
        public async Task UpdateMessageFlagAsync(string messageId, string flagStatus)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            var client = _authService.GetGraphClient();

            var flagType = flagStatus?.ToLowerInvariant() switch
            {
                "flagged" => Microsoft.Graph.Models.FollowupFlagStatus.Flagged,
                "complete" => Microsoft.Graph.Models.FollowupFlagStatus.Complete,
                _ => Microsoft.Graph.Models.FollowupFlagStatus.NotFlagged
            };

            await client.Me.Messages[messageId].PatchAsync(new Message
            {
                Flag = new FollowupFlag
                {
                    FlagStatus = flagType
                }
            });
        }

        /// <summary>
        /// 메시지의 읽음 상태 업데이트
        /// </summary>
        /// <param name="messageId">메시지 ID (EntryId)</param>
        /// <param name="isRead">읽음 여부</param>
        public async Task UpdateMessageReadStatusAsync(string messageId, bool isRead)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            var client = _authService.GetGraphClient();

            await client.Me.Messages[messageId].PatchAsync(new Message
            {
                IsRead = isRead
            });
        }

        /// <summary>
        /// 메시지의 카테고리 업데이트
        /// </summary>
        /// <param name="messageId">메시지 ID (EntryId)</param>
        /// <param name="categories">카테고리 목록</param>
        public async Task UpdateMessageCategoriesAsync(string messageId, List<string> categories)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            var client = _authService.GetGraphClient();

            await client.Me.Messages[messageId].PatchAsync(new Message
            {
                Categories = categories ?? new List<string>()
            });
        }

        /// <summary>
        /// 메시지의 중요도 업데이트
        /// </summary>
        /// <param name="messageId">메시지 ID (EntryId)</param>
        /// <param name="importance">중요도 (low, normal, high)</param>
        public async Task UpdateMessageImportanceAsync(string messageId, string importance)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            var client = _authService.GetGraphClient();

            var importanceLevel = importance?.ToLowerInvariant() switch
            {
                "high" => Microsoft.Graph.Models.Importance.High,
                "low" => Microsoft.Graph.Models.Importance.Low,
                _ => Microsoft.Graph.Models.Importance.Normal
            };

            await client.Me.Messages[messageId].PatchAsync(new Message
            {
                Importance = importanceLevel
            });
        }

        /// <summary>
        /// 메일 이동
        /// </summary>
        /// <param name="messageId">메일 ID</param>
        /// <param name="destinationFolderId">대상 폴더 ID</param>
        /// <returns>이동된 메일</returns>
        public async Task<Message> MoveMessageAsync(string messageId, string destinationFolderId)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            if (string.IsNullOrEmpty(destinationFolderId))
            {
                throw new ArgumentNullException(nameof(destinationFolderId));
            }

            var client = _authService.GetGraphClient();
            return await client.Me.Messages[messageId].Move.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = destinationFolderId
                });
        }

        /// <summary>
        /// 메일 삭제
        /// </summary>
        /// <param name="messageId">메일 ID</param>
        public async Task DeleteMessageAsync(string messageId)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentNullException(nameof(messageId));
            }

            var client = _authService.GetGraphClient();
            await client.Me.Messages[messageId].DeleteAsync();
        }

        #region Phase 2: 폴더 CRUD

        /// <summary>
        /// 폴더 생성
        /// </summary>
        /// <param name="folderName">폴더 이름</param>
        /// <param name="parentFolderId">상위 폴더 ID (null이면 루트에 생성)</param>
        /// <returns>생성된 폴더</returns>
        public async Task<MailFolder?> CreateFolderAsync(string folderName, string? parentFolderId = null)
        {
            if (string.IsNullOrEmpty(folderName))
            {
                throw new ArgumentNullException(nameof(folderName));
            }

            var client = _authService.GetGraphClient();
            var newFolder = new MailFolder
            {
                DisplayName = folderName
            };

            if (string.IsNullOrEmpty(parentFolderId))
            {
                // 루트 레벨에 생성
                return await client.Me.MailFolders.PostAsync(newFolder);
            }
            else
            {
                // 지정된 부모 폴더 아래에 생성
                return await client.Me.MailFolders[parentFolderId].ChildFolders.PostAsync(newFolder);
            }
        }

        /// <summary>
        /// 폴더 이름 변경
        /// </summary>
        /// <param name="folderId">폴더 ID</param>
        /// <param name="newName">새 이름</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> RenameFolderAsync(string folderId, string newName)
        {
            if (string.IsNullOrEmpty(folderId))
            {
                throw new ArgumentNullException(nameof(folderId));
            }

            if (string.IsNullOrEmpty(newName))
            {
                throw new ArgumentNullException(nameof(newName));
            }

            try
            {
                var client = _authService.GetGraphClient();
                await client.Me.MailFolders[folderId].PatchAsync(new MailFolder
                {
                    DisplayName = newName
                });
                return true;
            }
            catch (Exception ex)
            {
                Utils.Log4.Error($"폴더 이름 변경 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 폴더 삭제
        /// </summary>
        /// <param name="folderId">폴더 ID</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> DeleteFolderAsync(string folderId)
        {
            if (string.IsNullOrEmpty(folderId))
            {
                throw new ArgumentNullException(nameof(folderId));
            }

            try
            {
                var client = _authService.GetGraphClient();
                await client.Me.MailFolders[folderId].DeleteAsync();
                return true;
            }
            catch (Exception ex)
            {
                Utils.Log4.Error($"폴더 삭제 실패: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
