using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Serilog;

namespace mailX.Services.Converter
{
    /// <summary>
    /// DocumentFormat.OpenXml 라이브러리를 사용한 DOCX 변환기
    /// .docx 파일을 텍스트로 변환
    /// </summary>
    public class OpenXmlDocConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".docx" };

        public OpenXmlDocConverter()
        {
            _logger = Log.ForContext<OpenXmlDocConverter>();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "OpenXml_Doc";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "OpenXML (DOCX)";

        /// <summary>
        /// 우선순위 (낮을수록 우선)
        /// </summary>
        public int Priority => 60;

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
        /// DOCX 파일을 텍스트로 변환
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("DOCX 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("OpenXML DOCX 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromDocx(filePath), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("OpenXML DOCX 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenXML DOCX 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// OpenXML을 사용하여 DOCX 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromDocx(string filePath)
        {
            var sb = new StringBuilder();

            try
            {
                using var wordDocument = WordprocessingDocument.Open(filePath, false);
                var body = wordDocument.MainDocumentPart?.Document?.Body;

                if (body == null)
                {
                    _logger.Warning("DOCX 문서 본문이 비어있음: {FilePath}", filePath);
                    return string.Empty;
                }

                // 모든 요소 순회하며 텍스트 추출
                foreach (var element in body.Elements())
                {
                    ProcessElement(element, sb);
                }

                var result = sb.ToString().Trim();
                _logger.Information("OpenXML DOCX 변환 완료: {FilePath}, 길이: {Length}", filePath, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenXML DOCX 텍스트 추출 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 문서 요소 처리
        /// </summary>
        private void ProcessElement(DocumentFormat.OpenXml.OpenXmlElement element, StringBuilder sb)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    ProcessParagraph(paragraph, sb);
                    break;
                case Table table:
                    ProcessTable(table, sb);
                    break;
                default:
                    // 다른 요소의 자식 요소 처리
                    foreach (var child in element.Elements())
                    {
                        ProcessElement(child, sb);
                    }
                    break;
            }
        }

        /// <summary>
        /// 문단 처리
        /// </summary>
        private void ProcessParagraph(Paragraph paragraph, StringBuilder sb)
        {
            var texts = paragraph.Descendants<Text>();
            var paragraphText = string.Concat(texts.Select(t => t.Text));

            if (!string.IsNullOrWhiteSpace(paragraphText))
            {
                sb.AppendLine(paragraphText);
            }
        }

        /// <summary>
        /// 테이블 처리
        /// </summary>
        private void ProcessTable(Table table, StringBuilder sb)
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cellTexts = new List<string>();

                foreach (var cell in row.Elements<TableCell>())
                {
                    var cellText = string.Concat(
                        cell.Descendants<Text>().Select(t => t.Text));
                    cellTexts.Add(cellText);
                }

                if (cellTexts.Any(t => !string.IsNullOrEmpty(t)))
                {
                    sb.AppendLine(string.Join("\t", cellTexts));
                }
            }
            sb.AppendLine();
        }
    }
}
