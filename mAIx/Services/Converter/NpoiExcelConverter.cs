using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Serilog;

namespace mAIx.Services.Converter
{
    /// <summary>
    /// NPOI 라이브러리를 사용한 Excel 파일 변환기
    /// .xls, .xlsx 파일을 텍스트로 변환
    /// </summary>
    public class NpoiExcelConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".xls", ".xlsx" };

        public NpoiExcelConverter()
        {
            _logger = Log.ForContext<NpoiExcelConverter>();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "NPOI_Excel";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "NPOI (XLS/XLSX)";

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
        /// Excel 파일을 텍스트로 변환
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Excel 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("NPOI Excel 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromExcel(filePath), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("NPOI Excel 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "NPOI Excel 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// NPOI를 사용하여 Excel 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromExcel(string filePath)
        {
            var sb = new StringBuilder();
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                IWorkbook workbook;

                if (extension == ".xlsx")
                {
                    workbook = new XSSFWorkbook(fs);
                }
                else
                {
                    workbook = new HSSFWorkbook(fs);
                }

                // 모든 시트 처리
                for (int sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
                {
                    var sheet = workbook.GetSheetAt(sheetIndex);
                    var sheetName = sheet.SheetName;

                    sb.AppendLine($"=== {sheetName} ===");
                    sb.AppendLine();

                    // 모든 행 처리
                    for (int rowIndex = 0; rowIndex <= sheet.LastRowNum; rowIndex++)
                    {
                        var row = sheet.GetRow(rowIndex);
                        if (row == null) continue;

                        var cellTexts = new List<string>();
                        for (int cellIndex = 0; cellIndex < row.LastCellNum; cellIndex++)
                        {
                            var cell = row.GetCell(cellIndex);
                            var cellValue = GetCellValue(cell);
                            cellTexts.Add(cellValue);
                        }

                        if (cellTexts.Exists(t => !string.IsNullOrEmpty(t)))
                        {
                            sb.AppendLine(string.Join("\t", cellTexts));
                        }
                    }

                    sb.AppendLine();
                }

                workbook.Close();

                var result = sb.ToString().Trim();
                _logger.Information("NPOI Excel 변환 완료: {FilePath}, 길이: {Length}", filePath, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "NPOI Excel 텍스트 추출 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 셀 값을 문자열로 변환
        /// </summary>
        private string GetCellValue(ICell? cell)
        {
            if (cell == null)
                return string.Empty;

            try
            {
                switch (cell.CellType)
                {
                    case CellType.String:
                        return cell.StringCellValue ?? string.Empty;

                    case CellType.Numeric:
                        if (DateUtil.IsCellDateFormatted(cell))
                        {
                            return cell.DateCellValue?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                        }
                        return cell.NumericCellValue.ToString();

                    case CellType.Boolean:
                        return cell.BooleanCellValue.ToString();

                    case CellType.Formula:
                        try
                        {
                            return cell.NumericCellValue.ToString();
                        }
                        catch
                        {
                            try
                            {
                                return cell.StringCellValue ?? string.Empty;
                            }
                            catch
                            {
                                return cell.CellFormula ?? string.Empty;
                            }
                        }

                    case CellType.Error:
                        return $"#ERROR({cell.ErrorCellValue})";

                    case CellType.Blank:
                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
