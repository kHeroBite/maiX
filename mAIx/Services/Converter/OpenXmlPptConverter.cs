using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using Serilog;

namespace mAIx.Services.Converter
{
    /// <summary>
    /// DocumentFormat.OpenXml 라이브러리를 사용한 PPTX 변환기
    /// .pptx 파일을 텍스트로 변환
    /// </summary>
    public class OpenXmlPptConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".pptx" };

        public OpenXmlPptConverter()
        {
            _logger = Log.ForContext<OpenXmlPptConverter>();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "OpenXml_Ppt";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "OpenXML (PPTX)";

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
        /// PPTX 파일을 텍스트로 변환
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("PPTX 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("OpenXML PPTX 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromPptx(filePath), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("OpenXML PPTX 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenXML PPTX 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// OpenXML을 사용하여 PPTX 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromPptx(string filePath)
        {
            var sb = new StringBuilder();

            try
            {
                using var presentation = PresentationDocument.Open(filePath, false);
                var presentationPart = presentation.PresentationPart;

                if (presentationPart?.Presentation?.SlideIdList == null)
                {
                    _logger.Warning("PPTX 프레젠테이션이 비어있음: {FilePath}", filePath);
                    return string.Empty;
                }

                var slideIds = presentationPart.Presentation.SlideIdList.Elements<SlideId>().ToList();
                var slideNumber = 0;

                foreach (var slideId in slideIds)
                {
                    slideNumber++;
                    var slidePart = presentationPart.GetPartById(slideId.RelationshipId!) as SlidePart;

                    if (slidePart?.Slide == null) continue;

                    sb.AppendLine($"=== 슬라이드 {slideNumber} ===");

                    var slideText = ExtractTextFromSlide(slidePart);
                    if (!string.IsNullOrWhiteSpace(slideText))
                    {
                        sb.AppendLine(slideText);
                    }

                    // 노트 추출
                    if (slidePart.NotesSlidePart?.NotesSlide != null)
                    {
                        var notesText = ExtractTextFromNotes(slidePart.NotesSlidePart);
                        if (!string.IsNullOrWhiteSpace(notesText))
                        {
                            sb.AppendLine("[노트]");
                            sb.AppendLine(notesText);
                        }
                    }

                    sb.AppendLine();
                }

                var result = sb.ToString().Trim();
                _logger.Information("OpenXML PPTX 변환 완료: {FilePath}, 슬라이드: {Slides}, 길이: {Length}",
                    filePath, slideNumber, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenXML PPTX 텍스트 추출 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 슬라이드에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromSlide(SlidePart slidePart)
        {
            var sb = new StringBuilder();

            try
            {
                // 슬라이드의 모든 Shape에서 텍스트 추출
                var shapes = slidePart.Slide.Descendants<Shape>();

                foreach (var shape in shapes)
                {
                    var textBody = shape.TextBody;
                    if (textBody == null) continue;

                    foreach (var paragraph in textBody.Descendants<A.Paragraph>())
                    {
                        var paragraphText = GetParagraphText(paragraph);
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            sb.AppendLine(paragraphText);
                        }
                    }
                }

                // 테이블에서 텍스트 추출
                var tables = slidePart.Slide.Descendants<A.Table>();
                foreach (var table in tables)
                {
                    var tableText = ExtractTextFromTable(table);
                    if (!string.IsNullOrWhiteSpace(tableText))
                    {
                        sb.AppendLine(tableText);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "슬라이드 텍스트 추출 중 오류");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 문단에서 텍스트 추출
        /// </summary>
        private string GetParagraphText(A.Paragraph paragraph)
        {
            var texts = paragraph.Descendants<A.Text>();
            return string.Concat(texts.Select(t => t.Text));
        }

        /// <summary>
        /// 테이블에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromTable(A.Table table)
        {
            var sb = new StringBuilder();

            foreach (var row in table.Descendants<A.TableRow>())
            {
                var cellTexts = new List<string>();

                foreach (var cell in row.Descendants<A.TableCell>())
                {
                    var cellText = string.Concat(
                        cell.Descendants<A.Text>().Select(t => t.Text));
                    cellTexts.Add(cellText);
                }

                if (cellTexts.Any(t => !string.IsNullOrEmpty(t)))
                {
                    sb.AppendLine(string.Join("\t", cellTexts));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 노트에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromNotes(NotesSlidePart notesSlidePart)
        {
            var sb = new StringBuilder();

            try
            {
                var texts = notesSlidePart.NotesSlide.Descendants<A.Text>();
                foreach (var text in texts)
                {
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        sb.Append(text.Text);
                        sb.Append(' ');
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "노트 텍스트 추출 중 오류");
            }

            return sb.ToString().Trim();
        }
    }
}
