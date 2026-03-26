using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NPOI.XWPF.UserModel;
using Serilog;

namespace mAIx.Services.Converter
{
    /// <summary>
    /// NPOI 라이브러리를 사용한 Word 문서 변환기
    /// .docx 파일을 텍스트로 변환 (NPOI는 .docx만 지원)
    /// </summary>
    public class NpoiDocConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".docx" };

        public NpoiDocConverter()
        {
            _logger = Log.ForContext<NpoiDocConverter>();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "NPOI_Doc";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "NPOI (DOCX)";

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
        /// Word 문서를 텍스트로 변환
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Word 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("NPOI Word 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromWord(filePath), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("NPOI Word 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "NPOI Word 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// NPOI를 사용하여 Word 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromWord(string filePath)
        {
            var sb = new StringBuilder();

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var document = new XWPFDocument(fs);

            // 문단 텍스트 추출
            foreach (var paragraph in document.Paragraphs)
            {
                var text = paragraph.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    sb.AppendLine(text);
                }
            }

            // 테이블 텍스트 추출
            foreach (var table in document.Tables)
            {
                foreach (var row in table.Rows)
                {
                    var rowTexts = new List<string>();
                    foreach (var cell in row.GetTableCells())
                    {
                        var cellText = cell.GetText();
                        if (!string.IsNullOrEmpty(cellText))
                        {
                            rowTexts.Add(cellText);
                        }
                    }
                    if (rowTexts.Count > 0)
                    {
                        sb.AppendLine(string.Join("\t", rowTexts));
                    }
                }
                sb.AppendLine();
            }

            var result = sb.ToString().Trim();
            _logger.Information("NPOI DOCX 변환 완료: {FilePath}, 길이: {Length}", filePath, result.Length);
            return result;
        }
    }
}
