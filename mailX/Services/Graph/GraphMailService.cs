using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace mailX.Services.Graph
{
    /// <summary>
    /// Microsoft Graph 메일 서비스
    /// </summary>
    public class GraphMailService
    {
        private readonly GraphAuthService _authService;

        public GraphMailService(GraphAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        /// <summary>
        /// 메일 폴더 목록 조회
        /// </summary>
        /// <returns>폴더 목록</returns>
        public async Task<IEnumerable<MailFolder>> GetFoldersAsync()
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.MailFolders.GetAsync();
            return response?.Value ?? new List<MailFolder>();
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

            if (string.IsNullOrEmpty(folderId))
            {
                // 받은편지함
                var response = await client.Me.MailFolders["Inbox"].Messages.GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                });
                return response?.Value ?? new List<Message>();
            }
            else
            {
                var response = await client.Me.MailFolders[folderId].Messages.GetAsync(config =>
                {
                    config.QueryParameters.Top = top;
                    config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                });
                return response?.Value ?? new List<Message>();
            }
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
    }
}
