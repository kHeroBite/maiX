using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Serilog;

namespace MaiX.Services.Converter
{
    /// <summary>
    /// PdfPig 라이브러리를 사용한 PDF 변환기
    /// .pdf 파일을 텍스트로 변환
    /// </summary>
    public class PdfPigConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".pdf" };

        public PdfPigConverter()
        {
            _logger = Log.ForContext<PdfPigConverter>();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "PdfPig";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "PdfPig (PDF)";

        /// <summary>
        /// 우선순위 (낮을수록 우선)
        /// </summary>
        public int Priority => 50;

        /// <summary>
        /// 지원 확장자 목록
        /// </summary>
        public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

        /// <summary>
        /// 변환기 사용 가능 여부
        /// </summary>
        public bool IsAvailable => true;

        /// <summary>
        /// 확장자 변환 가능 여부 확인
        /// </summary>
        public bool CanConvert(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;

            var ext = extension.StartsWith(".") ? extension : $".{extension}";
            return Array.Exists(_supportedExtensions,
                e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// PDF 파일을 텍스트로 변환
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("PdfPig 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromPdf(filePath), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("PdfPig 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "PdfPig 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// PdfPig를 사용하여 PDF 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromPdf(string filePath)
        {
            var sb = new StringBuilder();

            try
            {
                using var document = PdfDocument.Open(filePath);
                var pageCount = document.NumberOfPages;

                _logger.Debug("PDF 페이지 수: {PageCount}", pageCount);

                for (int i = 1; i <= pageCount; i++)
                {
                    var page = document.GetPage(i);
                    var pageText = ExtractTextFromPage(page);

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        if (pageCount > 1)
                        {
                            sb.AppendLine($"--- 페이지 {i} ---");
                        }
                        sb.AppendLine(pageText);
                        sb.AppendLine();
                    }
                }

                var result = sb.ToString().Trim();
                _logger.Information("PdfPig 변환 완료: {FilePath}, 페이지: {Pages}, 길이: {Length}",
                    filePath, pageCount, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "PdfPig 텍스트 추출 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// PDF 페이지에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromPage(Page page)
        {
            try
            {
                // 기본 텍스트 추출
                var text = page.Text;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                // 개별 단어에서 텍스트 추출 시도
                var sb = new StringBuilder();
                foreach (var word in page.GetWords())
                {
                    sb.Append(word.Text);
                    sb.Append(' ');
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "페이지 텍스트 추출 중 오류");
                return string.Empty;
            }
        }
    }
}
