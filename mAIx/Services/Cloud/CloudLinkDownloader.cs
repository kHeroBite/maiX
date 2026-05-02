using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Services.Converter;
using Serilog;

namespace mAIx.Services.Cloud
{
    /// <summary>
    /// 클라우드 파일 다운로드 서비스
    /// Google Drive, OneDrive, Dropbox, SharePoint 링크 지원
    /// </summary>
    public class CloudLinkDownloader : IDisposable
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        // Google Drive 파일 ID 추출 패턴
        private static readonly Regex GoogleDriveIdPattern = new(
            @"(?:file/d/|open\?id=|uc\?id=)([a-zA-Z0-9_-]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // OneDrive/SharePoint 공유 링크 패턴
        private static readonly Regex OneDriveSharePattern = new(
            @"https?://1drv\.ms/([a-zA-Z])/s!([a-zA-Z0-9_-]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public CloudLinkDownloader()
        {
            _logger = Log.ForContext<CloudLinkDownloader>();
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5) // 대용량 파일 고려
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) mAIx/1.0");
        }

        /// <summary>
        /// 클라우드 링크에서 파일 다운로드
        /// </summary>
        /// <param name="link">클라우드 공유 링크</param>
        /// <param name="type">클라우드 서비스 유형</param>
        /// <param name="targetFolder">다운로드 대상 폴더</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>(파일 경로, 파일명) 튜플</returns>
        public async Task<(string FilePath, string FileName)> DownloadAsync(
            string link,
            CloudType type,
            string targetFolder,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(link))
                throw new ArgumentNullException(nameof(link));

            Directory.CreateDirectory(targetFolder);

            _logger.Debug("클라우드 다운로드 시작: {Type}, {Link}", type, link);

            return type switch
            {
                CloudType.GoogleDrive => await DownloadFromGoogleDriveAsync(link, targetFolder, ct).ConfigureAwait(false),
                CloudType.OneDrive => await DownloadFromOneDriveAsync(link, targetFolder, ct).ConfigureAwait(false),
                CloudType.Dropbox => await DownloadFromDropboxAsync(link, targetFolder, ct).ConfigureAwait(false),
                CloudType.SharePoint => await DownloadFromSharePointAsync(link, targetFolder, ct).ConfigureAwait(false),
                _ => throw new NotSupportedException($"지원하지 않는 클라우드 유형: {type}")
            };
        }

        /// <summary>
        /// Google Drive 파일 다운로드
        /// </summary>
        private async Task<(string, string)> DownloadFromGoogleDriveAsync(
            string link,
            string targetFolder,
            CancellationToken ct)
        {
            // 파일 ID 추출
            var match = GoogleDriveIdPattern.Match(link);
            if (!match.Success)
            {
                throw new InvalidOperationException("Google Drive 파일 ID를 추출할 수 없습니다.");
            }

            var fileId = match.Groups[1].Value;
            _logger.Debug("Google Drive 파일 ID: {FileId}", fileId);

            // 직접 다운로드 URL 생성
            var downloadUrl = $"https://drive.google.com/uc?export=download&id={fileId}";

            // 1차 요청 (소용량 파일 직접 다운로드 또는 확인 페이지)
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // 파일명 추출 시도
            var fileName = ExtractFileName(response, fileId);

            // 바이러스 검사 경고 확인 (대용량 파일)
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType?.Contains("text/html") == true)
            {
                // 확인 토큰이 필요한 경우 (대용량 파일)
                return await DownloadLargeGoogleDriveFileAsync(fileId, targetFolder, fileName, ct).ConfigureAwait(false);
            }

            // 소용량 파일 직접 다운로드
            var filePath = Path.Combine(targetFolder, fileName);
            await using var fileStream = File.Create(filePath);
            await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);

            _logger.Information("Google Drive 다운로드 완료: {FileName}", fileName);
            return (filePath, fileName);
        }

        /// <summary>
        /// Google Drive 대용량 파일 다운로드 (확인 토큰 필요)
        /// </summary>
        private async Task<(string, string)> DownloadLargeGoogleDriveFileAsync(
            string fileId,
            string targetFolder,
            string fileName,
            CancellationToken ct)
        {
            // 확인 페이지에서 토큰 추출 (confirm=t 파라미터 추가)
            var downloadUrl = $"https://drive.google.com/uc?export=download&confirm=t&id={fileId}";

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var filePath = Path.Combine(targetFolder, fileName);
            await using var fileStream = File.Create(filePath);
            await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);

            _logger.Information("Google Drive 대용량 파일 다운로드 완료: {FileName}", fileName);
            return (filePath, fileName);
        }

        /// <summary>
        /// OneDrive 파일 다운로드
        /// </summary>
        private async Task<(string, string)> DownloadFromOneDriveAsync(
            string link,
            string targetFolder,
            CancellationToken ct)
        {
            // OneDrive 공유 링크를 직접 다운로드 URL로 변환
            var downloadUrl = ConvertOneDriveLinkToDownload(link);

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            // 리다이렉트 처리 (실제 다운로드 URL로 이동)
            if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
            {
                var redirectUrl = response.Headers.Location?.ToString();
                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    return await DownloadFromUrlAsync(redirectUrl, targetFolder, ct).ConfigureAwait(false);
                }
            }

            response.EnsureSuccessStatusCode();

            var fileName = ExtractFileName(response, "onedrive_file");
            var filePath = Path.Combine(targetFolder, fileName);

            await using var fileStream = File.Create(filePath);
            await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);

            _logger.Information("OneDrive 다운로드 완료: {FileName}", fileName);
            return (filePath, fileName);
        }

        /// <summary>
        /// OneDrive 링크를 다운로드 URL로 변환
        /// </summary>
        private string ConvertOneDriveLinkToDownload(string link)
        {
            // 1drv.ms 단축 링크 처리
            if (link.Contains("1drv.ms"))
            {
                // 단축 링크는 그대로 사용 (리다이렉트 됨)
                return link;
            }

            // onedrive.live.com 링크를 다운로드 URL로 변환
            // ?download=1 파라미터 추가
            if (link.Contains("?"))
            {
                return link + "&download=1";
            }
            return link + "?download=1";
        }

        /// <summary>
        /// Dropbox 파일 다운로드
        /// </summary>
        private async Task<(string, string)> DownloadFromDropboxAsync(
            string link,
            string targetFolder,
            CancellationToken ct)
        {
            // Dropbox 공유 링크를 직접 다운로드 URL로 변환
            // dl=0 → dl=1 또는 ?dl=1 추가
            var downloadUrl = link;
            if (link.Contains("dl=0"))
            {
                downloadUrl = link.Replace("dl=0", "dl=1");
            }
            else if (!link.Contains("dl=1"))
            {
                downloadUrl = link.Contains("?") ? link + "&dl=1" : link + "?dl=1";
            }

            return await DownloadFromUrlAsync(downloadUrl, targetFolder, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// SharePoint 파일 다운로드
        /// </summary>
        private async Task<(string, string)> DownloadFromSharePointAsync(
            string link,
            string targetFolder,
            CancellationToken ct)
        {
            // SharePoint 링크에 download=1 파라미터 추가
            var downloadUrl = link.Contains("?")
                ? link + "&download=1"
                : link + "?download=1";

            return await DownloadFromUrlAsync(downloadUrl, targetFolder, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// URL에서 직접 다운로드
        /// </summary>
        private async Task<(string, string)> DownloadFromUrlAsync(
            string url,
            string targetFolder,
            CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var fileName = ExtractFileName(response, "downloaded_file");
            var filePath = Path.Combine(targetFolder, fileName);

            await using var fileStream = File.Create(filePath);
            await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);

            _logger.Information("파일 다운로드 완료: {FileName}", fileName);
            return (filePath, fileName);
        }

        /// <summary>
        /// HTTP 응답에서 파일명 추출
        /// </summary>
        private string ExtractFileName(HttpResponseMessage response, string fallbackName)
        {
            // Content-Disposition 헤더에서 파일명 추출
            var contentDisposition = response.Content.Headers.ContentDisposition;
            if (contentDisposition != null)
            {
                var fileName = contentDisposition.FileNameStar ?? contentDisposition.FileName;
                if (!string.IsNullOrEmpty(fileName))
                {
                    // 따옴표 제거
                    return fileName.Trim('"', '\'');
                }
            }

            // URL에서 파일명 추출 시도
            var requestUri = response.RequestMessage?.RequestUri?.ToString();
            if (!string.IsNullOrEmpty(requestUri))
            {
                var uriFileName = Path.GetFileName(new Uri(requestUri).LocalPath);
                if (!string.IsNullOrEmpty(uriFileName) && uriFileName.Contains('.'))
                {
                    return SanitizeFileName(uriFileName);
                }
            }

            // Content-Type에서 확장자 추론
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var extension = GetExtensionFromContentType(contentType);

            return $"{fallbackName}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
        }

        /// <summary>
        /// Content-Type에서 확장자 추론
        /// </summary>
        private string GetExtensionFromContentType(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return ".bin";

            return contentType.ToLowerInvariant() switch
            {
                "application/pdf" => ".pdf",
                "application/msword" => ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.ms-excel" => ".xls",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                "application/vnd.ms-powerpoint" => ".ppt",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
                "application/hwp" or "application/x-hwp" => ".hwp",
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/gif" => ".gif",
                "text/plain" => ".txt",
                "text/html" => ".html",
                "application/json" => ".json",
                "application/xml" or "text/xml" => ".xml",
                "application/zip" => ".zip",
                _ => ".bin"
            };
        }

        /// <summary>
        /// 파일명 정리 (특수문자 제거)
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

            // 최대 길이 제한
            if (sanitized.Length > 200)
            {
                var ext = Path.GetExtension(sanitized);
                sanitized = sanitized[..(200 - ext.Length)] + ext;
            }

            return sanitized;
        }

        /// <summary>
        /// 클라우드 유형 자동 감지
        /// </summary>
        public static CloudType DetectCloudType(string link)
        {
            if (string.IsNullOrEmpty(link))
                return CloudType.Unknown;

            var lowerLink = link.ToLowerInvariant();

            if (lowerLink.Contains("drive.google.com"))
                return CloudType.GoogleDrive;

            if (lowerLink.Contains("onedrive.live.com") || lowerLink.Contains("1drv.ms"))
                return CloudType.OneDrive;

            if (lowerLink.Contains("dropbox.com"))
                return CloudType.Dropbox;

            if (lowerLink.Contains("sharepoint.com"))
                return CloudType.SharePoint;

            return CloudType.Unknown;
        }

        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _httpClient?.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
