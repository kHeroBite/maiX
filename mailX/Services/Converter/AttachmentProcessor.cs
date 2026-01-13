using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using mailX.Services.Cloud;
using Serilog;

namespace mailX.Services.Converter
{
    /// <summary>
    /// 첨부파일 처리 메인 서비스
    /// 파일 확장자별 변환기 선택, 임시 폴더 관리, 클라우드 링크 감지
    /// </summary>
    public class AttachmentProcessor
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, IDocumentConverter> _converters;
        private readonly CloudLinkDownloader _cloudDownloader;
        private readonly string _tempFolder;

        // 클라우드 링크 패턴
        private static readonly Regex GoogleDrivePattern = new(
            @"https?://drive\.google\.com/(?:file/d/|open\?id=|uc\?id=)([a-zA-Z0-9_-]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex OneDrivePattern = new(
            @"https?://(?:onedrive\.live\.com|1drv\.ms)/[^\s]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DropboxPattern = new(
            @"https?://(?:www\.)?dropbox\.com/[^\s]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SharePointPattern = new(
            @"https?://[a-zA-Z0-9-]+\.sharepoint\.com/[^\s]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public AttachmentProcessor()
        {
            _logger = Log.ForContext<AttachmentProcessor>();
            _converters = new Dictionary<string, IDocumentConverter>(StringComparer.OrdinalIgnoreCase);
            _cloudDownloader = new CloudLinkDownloader();

            // 임시 폴더 설정
            _tempFolder = Path.Combine(Path.GetTempPath(), "mailX", "attachments");
            Directory.CreateDirectory(_tempFolder);

            // 변환기 등록
            RegisterDefaultConverters();
        }

        /// <summary>
        /// 기본 변환기 등록
        /// </summary>
        private void RegisterDefaultConverters()
        {
            RegisterConverter(new HwpConverter());
            RegisterConverter(new PandocConverter());
            RegisterConverter(new OcrConverter());

            _logger.Information("첨부파일 변환기 등록 완료: {Count}개", _converters.Count);
        }

        /// <summary>
        /// 변환기 등록
        /// </summary>
        public void RegisterConverter(IDocumentConverter converter)
        {
            foreach (var ext in converter.SupportedExtensions)
            {
                // 이미 등록된 확장자는 덮어쓰지 않음 (먼저 등록된 변환기 우선)
                if (!_converters.ContainsKey(ext))
                {
                    _converters[ext] = converter;
                    _logger.Debug("변환기 등록: {Extension} → {Converter}", ext, converter.Name);
                }
            }
        }

        /// <summary>
        /// 첨부파일 처리 (파일 경로)
        /// </summary>
        public async Task<ConversionResult> ProcessAttachmentAsync(
            string filePath,
            CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (string.IsNullOrEmpty(filePath))
            {
                return ConversionResult.Failed("파일 경로가 비어있습니다.", filePath, "None");
            }

            if (!File.Exists(filePath))
            {
                return ConversionResult.Failed($"파일을 찾을 수 없습니다: {filePath}", filePath, "None");
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            _logger.Debug("첨부파일 처리 시작: {FilePath}, 확장자: {Extension}", filePath, extension);

            try
            {
                // 변환기 선택
                if (!_converters.TryGetValue(extension, out var converter))
                {
                    // 텍스트 파일인 경우 직접 읽기
                    if (IsTextFile(extension))
                    {
                        var text = await File.ReadAllTextAsync(filePath, ct);
                        sw.Stop();
                        var result = ConversionResult.Succeeded(text, filePath, "DirectRead");
                        result.Duration = sw.Elapsed;
                        return result;
                    }

                    return ConversionResult.Failed(
                        $"지원하지 않는 파일 형식: {extension}",
                        filePath,
                        "None");
                }

                // 변환기 사용 가능 여부 확인
                if (!converter.IsAvailable)
                {
                    return ConversionResult.Failed(
                        $"{converter.Name}을(를) 사용할 수 없습니다.",
                        filePath,
                        converter.Name);
                }

                // 변환 수행
                var convertedText = await converter.ConvertToTextAsync(filePath, ct);
                sw.Stop();

                var conversionResult = ConversionResult.Succeeded(convertedText, filePath, converter.Name);
                conversionResult.Duration = sw.Elapsed;
                conversionResult.Metadata["FileSize"] = new FileInfo(filePath).Length;
                conversionResult.Metadata["Extension"] = extension;

                _logger.Information("첨부파일 처리 완료: {FilePath}, 시간: {Duration}ms",
                    filePath, sw.ElapsedMilliseconds);

                return conversionResult;
            }
            catch (OperationCanceledException)
            {
                return ConversionResult.Failed("작업이 취소되었습니다.", filePath, "Cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "첨부파일 처리 실패: {FilePath}", filePath);
                return ConversionResult.Failed(ex.Message, filePath, "Error");
            }
        }

        /// <summary>
        /// 첨부파일 처리 (바이트 배열)
        /// </summary>
        public async Task<ConversionResult> ProcessAttachmentAsync(
            byte[] content,
            string fileName,
            CancellationToken ct = default)
        {
            if (content == null || content.Length == 0)
            {
                return ConversionResult.Failed("파일 내용이 비어있습니다.", fileName, "None");
            }

            // 임시 파일로 저장
            var tempPath = Path.Combine(_tempFolder, $"{Guid.NewGuid()}_{fileName}");

            try
            {
                await File.WriteAllBytesAsync(tempPath, content, ct);
                return await ProcessAttachmentAsync(tempPath, ct);
            }
            finally
            {
                // 임시 파일 정리
                TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// 첨부파일 처리 (스트림)
        /// </summary>
        public async Task<ConversionResult> ProcessAttachmentAsync(
            Stream stream,
            string fileName,
            CancellationToken ct = default)
        {
            if (stream == null || !stream.CanRead)
            {
                return ConversionResult.Failed("스트림을 읽을 수 없습니다.", fileName, "None");
            }

            // 임시 파일로 저장
            var tempPath = Path.Combine(_tempFolder, $"{Guid.NewGuid()}_{fileName}");

            try
            {
                await using var fileStream = File.Create(tempPath);
                await stream.CopyToAsync(fileStream, ct);
                await fileStream.FlushAsync(ct);

                return await ProcessAttachmentAsync(tempPath, ct);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// 여러 첨부파일 일괄 처리
        /// </summary>
        public async Task<List<ConversionResult>> ProcessAttachmentsAsync(
            IEnumerable<string> filePaths,
            CancellationToken ct = default)
        {
            var results = new List<ConversionResult>();

            foreach (var filePath in filePaths)
            {
                if (ct.IsCancellationRequested)
                    break;

                var result = await ProcessAttachmentAsync(filePath, ct);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// 클라우드 링크 감지 및 처리
        /// </summary>
        public async Task<List<ConversionResult>> ProcessCloudLinksAsync(
            string text,
            CancellationToken ct = default)
        {
            var results = new List<ConversionResult>();
            var links = DetectCloudLinks(text);

            _logger.Information("클라우드 링크 감지: {Count}개", links.Count);

            foreach (var (link, type) in links)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    var (filePath, fileName) = await _cloudDownloader.DownloadAsync(link, type, _tempFolder, ct);

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var result = await ProcessAttachmentAsync(filePath, ct);
                        result.Metadata["CloudLink"] = link;
                        result.Metadata["CloudType"] = type.ToString();
                        results.Add(result);

                        TryDeleteFile(filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "클라우드 파일 처리 실패: {Link}", link);
                    results.Add(ConversionResult.Failed(
                        $"클라우드 파일 다운로드 실패: {ex.Message}",
                        link,
                        "CloudDownloader"));
                }
            }

            return results;
        }

        /// <summary>
        /// 클라우드 링크 감지
        /// </summary>
        public List<(string Link, CloudType Type)> DetectCloudLinks(string text)
        {
            var links = new List<(string, CloudType)>();

            if (string.IsNullOrEmpty(text))
                return links;

            // Google Drive
            foreach (Match match in GoogleDrivePattern.Matches(text))
            {
                links.Add((match.Value, CloudType.GoogleDrive));
            }

            // OneDrive
            foreach (Match match in OneDrivePattern.Matches(text))
            {
                links.Add((match.Value, CloudType.OneDrive));
            }

            // Dropbox
            foreach (Match match in DropboxPattern.Matches(text))
            {
                links.Add((match.Value, CloudType.Dropbox));
            }

            // SharePoint
            foreach (Match match in SharePointPattern.Matches(text))
            {
                links.Add((match.Value, CloudType.SharePoint));
            }

            return links.Distinct().ToList();
        }

        /// <summary>
        /// 통합 처리 (로컬 첨부파일 + 클라우드 링크)
        /// </summary>
        public async Task<AttachmentProcessingResult> ProcessAllAsync(
            IEnumerable<string> localFiles,
            string emailBody,
            CancellationToken ct = default)
        {
            var result = new AttachmentProcessingResult();
            var sb = new StringBuilder();

            // 로컬 첨부파일 처리
            foreach (var filePath in localFiles ?? Enumerable.Empty<string>())
            {
                if (ct.IsCancellationRequested)
                    break;

                var convResult = await ProcessAttachmentAsync(filePath, ct);
                result.Results.Add(convResult);

                if (convResult.Success && !string.IsNullOrEmpty(convResult.Text))
                {
                    sb.AppendLine($"[첨부파일: {Path.GetFileName(filePath)}]");
                    sb.AppendLine(convResult.Text);
                    sb.AppendLine();
                }
            }

            // 클라우드 링크 처리
            if (!string.IsNullOrEmpty(emailBody))
            {
                var cloudResults = await ProcessCloudLinksAsync(emailBody, ct);
                result.Results.AddRange(cloudResults);

                foreach (var cloudResult in cloudResults.Where(r => r.Success))
                {
                    sb.AppendLine($"[클라우드 파일: {Path.GetFileName(cloudResult.FilePath)}]");
                    sb.AppendLine(cloudResult.Text);
                    sb.AppendLine();
                }
            }

            result.CombinedText = sb.ToString().Trim();
            result.TotalFiles = result.Results.Count;
            result.SuccessCount = result.Results.Count(r => r.Success);
            result.FailedCount = result.Results.Count(r => !r.Success);

            _logger.Information("첨부파일 통합 처리 완료: 성공 {Success}/{Total}",
                result.SuccessCount, result.TotalFiles);

            return result;
        }

        /// <summary>
        /// 지원 확장자 목록 조회
        /// </summary>
        public IReadOnlyList<string> GetSupportedExtensions()
        {
            return _converters.Keys.ToList();
        }

        /// <summary>
        /// 특정 확장자 지원 여부 확인
        /// </summary>
        public bool IsSupported(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;

            var ext = extension.StartsWith(".") ? extension : $".{extension}";
            return _converters.ContainsKey(ext) || IsTextFile(ext);
        }

        /// <summary>
        /// 텍스트 파일 여부 확인
        /// </summary>
        private bool IsTextFile(string extension)
        {
            var textExtensions = new[] { ".txt", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".md", ".ini", ".cfg" };
            return Array.Exists(textExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 파일 삭제 시도
        /// </summary>
        private void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "임시 파일 삭제 실패: {FilePath}", filePath);
            }
        }

        /// <summary>
        /// 임시 폴더 정리 (오래된 파일 삭제)
        /// </summary>
        public void CleanupTempFolder(TimeSpan? maxAge = null)
        {
            var age = maxAge ?? TimeSpan.FromHours(24);

            try
            {
                var cutoff = DateTime.Now - age;
                var files = Directory.GetFiles(_tempFolder);

                var deletedCount = 0;
                foreach (var file in files)
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        TryDeleteFile(file);
                        deletedCount++;
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.Information("임시 폴더 정리: {Count}개 파일 삭제", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "임시 폴더 정리 실패");
            }
        }
    }

    /// <summary>
    /// 첨부파일 처리 결과 (통합)
    /// </summary>
    public class AttachmentProcessingResult
    {
        /// <summary>
        /// 개별 변환 결과 목록
        /// </summary>
        public List<ConversionResult> Results { get; set; } = new();

        /// <summary>
        /// 모든 텍스트 통합 결과
        /// </summary>
        public string CombinedText { get; set; } = string.Empty;

        /// <summary>
        /// 총 파일 수
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// 성공한 파일 수
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 실패한 파일 수
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 처리 성공 여부 (하나라도 성공하면 true)
        /// </summary>
        public bool HasContent => SuccessCount > 0 && !string.IsNullOrEmpty(CombinedText);
    }

    /// <summary>
    /// 클라우드 서비스 유형
    /// </summary>
    public enum CloudType
    {
        GoogleDrive,
        OneDrive,
        Dropbox,
        SharePoint,
        Unknown
    }
}
