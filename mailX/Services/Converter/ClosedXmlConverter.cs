using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Serilog;

namespace mailX.Services.Converter
{
    /// <summary>
    /// ClosedXML 라이브러리를 사용한 XLSX 변환기
    /// .xlsx 파일을 텍스트로 변환
    /// </summary>
    public class ClosedXmlConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".xlsx" };

        public ClosedXmlConverter()
        {
            _logger = Log.ForContext<ClosedXmlConverter>();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "ClosedXml";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "ClosedXML (XLSX)";

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
        /// XLSX 파일을 텍스트로 변환
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("XLSX 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("ClosedXML 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromXlsx(filePath), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("ClosedXML 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ClosedXML 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// ClosedXML을 사용하여 XLSX 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromXlsx(string filePath)
        {
            var sb = new StringBuilder();

            try
            {
                using var workbook = new XLWorkbook(filePath);

                foreach (var worksheet in workbook.Worksheets)
                {
                    sb.AppendLine($"=== {worksheet.Name} ===");
                    sb.AppendLine();

                    var range = worksheet.RangeUsed();
                    if (range == null) continue;

                    var rows = range.RowsUsed();
                    foreach (var row in rows)
                    {
                        var cellTexts = new List<string>();

                        foreach (var cell in row.CellsUsed())
                        {
                            var value = GetCellValue(cell);

                            // 열 위치에 맞게 빈 셀 채우기
                            var colIndex = cell.Address.ColumnNumber - row.FirstCell().Address.ColumnNumber;
                            while (cellTexts.Count < colIndex)
                            {
                                cellTexts.Add(string.Empty);
                            }
                            cellTexts.Add(value);
                        }

                        if (cellTexts.Exists(t => !string.IsNullOrEmpty(t)))
                        {
                            sb.AppendLine(string.Join("\t", cellTexts));
                        }
                    }

                    sb.AppendLine();
                }

                var result = sb.ToString().Trim();
                _logger.Information("ClosedXML 변환 완료: {FilePath}, 길이: {Length}", filePath, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ClosedXML 텍스트 추출 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 셀 값을 문자열로 변환
        /// </summary>
        private string GetCellValue(IXLCell cell)
        {
            try
            {
                if (cell.IsEmpty())
                    return string.Empty;

                var value = cell.Value;

                if (value.IsDateTime)
                {
                    return value.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss");
                }
                else if (value.IsNumber)
                {
                    return value.GetNumber().ToString();
                }
                else if (value.IsBoolean)
                {
                    return value.GetBoolean().ToString();
                }
                else if (value.IsText)
                {
                    return value.GetText();
                }
                else if (value.IsError)
                {
                    return $"#ERROR({value.GetError()})";
                }

                return cell.GetFormattedString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
