using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenMcdf;
using Serilog;

namespace MaiX.Services.Converter
{
    /// <summary>
    /// HWP 파일 변환기 (OpenMcdf 사용)
    /// .hwp, .hwpx 파일을 텍스트로 변환
    /// </summary>
    public class HwpConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".hwp", ".hwpx" };

        public HwpConverter()
        {
            _logger = Log.ForContext<HwpConverter>();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "OpenMcdfHwp";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "OpenMcdf (HWP)";

        /// <summary>
        /// 우선순위 (낮을수록 우선)
        /// </summary>
        public int Priority => 100;

        /// <summary>
        /// 지원 확장자 목록
        /// </summary>
        public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

        /// <summary>
        /// 변환기 사용 가능 여부
        /// OpenMcdf는 NuGet으로 설치되므로 항상 사용 가능
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
        /// HWP 파일을 텍스트로 변환
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("HWP 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("HWP 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromHwp(filePath), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("HWP 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "HWP 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// HWP 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromHwp(string filePath)
        {
            var sb = new StringBuilder();
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (extension == ".hwpx")
                {
                    // HWPX는 OOXML 기반 (ZIP 압축)
                    sb.Append(ExtractFromHwpx(filePath));
                }
                else
                {
                    // HWP는 Compound File Binary Format
                    sb.Append(ExtractFromHwp(filePath));
                }

                var result = sb.ToString().Trim();
                _logger.Information("HWP 변환 완료: {FilePath}, 길이: {Length}", filePath, result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "HWP 텍스트 추출 실패: {FilePath}", filePath);

                // 폴백: 바이너리에서 텍스트 추출 시도
                return TryFallbackExtraction(filePath);
            }
        }

        /// <summary>
        /// HWP 파일에서 텍스트 추출 (OpenMcdf를 사용한 Compound File 처리)
        /// </summary>
        private string ExtractFromHwp(string filePath)
        {
            var sb = new StringBuilder();

            try
            {
                // OpenMcdf 3.x로 OLE Compound File 열기
                using var rootStorage = RootStorage.OpenRead(filePath);

                // HWP 파일의 BodyText 스트림 찾기
                // HWP 5.0 형식의 본문 텍스트는 BodyText/SectionN 스트림에 저장됨

                // BodyText 스토리지 찾기
                if (TryGetStorage(rootStorage, "BodyText", out var bodyTextStorage) && bodyTextStorage != null)
                {
                    // 각 섹션 스트림에서 텍스트 추출
                    int sectionIndex = 0;
                    while (TryGetStream(bodyTextStorage, $"Section{sectionIndex}", out var sectionData) && sectionData != null)
                    {
                        var text = ExtractTextFromSectionData(sectionData);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine(text);
                            sb.AppendLine();
                        }
                        sectionIndex++;
                    }
                }

                // PrvText 스트림에서 미리보기 텍스트 추출 시도
                if (sb.Length == 0 && TryGetStream(rootStorage, "PrvText", out var prvTextData) && prvTextData != null)
                {
                    // PrvText는 UTF-16 LE 인코딩
                    var text = Encoding.Unicode.GetString(prvTextData).TrimEnd('\0');
                    sb.Append(text);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "OpenMcdf 파싱 실패, 대체 방법 시도: {FilePath}", filePath);
                throw;
            }

            return sb.ToString();
        }

        /// <summary>
        /// HWP 섹션 데이터에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromSectionData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();

            try
            {
                // HWP 섹션 데이터는 압축될 수 있음
                byte[] decompressed;
                if (IsCompressed(data))
                {
                    decompressed = DecompressData(data);
                }
                else
                {
                    decompressed = data;
                }

                // 텍스트 추출 (UTF-16 LE)
                for (int i = 0; i < decompressed.Length - 1; i += 2)
                {
                    var ch = (char)(decompressed[i] | (decompressed[i + 1] << 8));

                    // 인쇄 가능한 문자만 추출
                    if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ||
                        char.IsPunctuation(ch) || char.IsSymbol(ch))
                    {
                        sb.Append(ch);
                    }
                    else if (ch == '\n' || ch == '\r')
                    {
                        sb.Append('\n');
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "섹션 데이터 파싱 중 오류");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 데이터가 zlib 압축되었는지 확인
        /// </summary>
        private bool IsCompressed(byte[] data)
        {
            // zlib 헤더 확인 (0x78 0x9C 또는 0x78 0xDA)
            return data.Length > 2 &&
                   data[0] == 0x78 &&
                   (data[1] == 0x9C || data[1] == 0xDA || data[1] == 0x01 || data[1] == 0x5E);
        }

        /// <summary>
        /// zlib 압축 해제
        /// </summary>
        private byte[] DecompressData(byte[] data)
        {
            try
            {
                // zlib 헤더 스킵 (2바이트)
                using var inputStream = new MemoryStream(data, 2, data.Length - 2);
                using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
                using var outputStream = new MemoryStream();

                deflateStream.CopyTo(outputStream);
                return outputStream.ToArray();
            }
            catch
            {
                // 압축 해제 실패 시 원본 반환
                return data;
            }
        }

        /// <summary>
        /// 스토리지 안전하게 가져오기 (OpenMcdf 3.x)
        /// </summary>
        private bool TryGetStorage(OpenMcdf.Storage parent, string name, out OpenMcdf.Storage? storage)
        {
            try
            {
                storage = parent.OpenStorage(name);
                return storage != null;
            }
            catch
            {
                storage = null;
                return false;
            }
        }

        /// <summary>
        /// 스트림 안전하게 가져오기 (OpenMcdf 3.x) - 바이트 배열 반환
        /// </summary>
        private bool TryGetStream(OpenMcdf.Storage storage, string name, out byte[]? data)
        {
            try
            {
                using var stream = storage.OpenStream(name);
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                data = ms.ToArray();
                return data != null && data.Length > 0;
            }
            catch
            {
                data = null;
                return false;
            }
        }

        /// <summary>
        /// HWPX 파일에서 텍스트 추출 (OOXML Format)
        /// </summary>
        private string ExtractFromHwpx(string filePath)
        {
            var sb = new StringBuilder();

            try
            {
                // HWPX는 ZIP 압축 파일
                using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);

                // Contents/section*.xml 파일에서 텍스트 추출
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase)
                        && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var xml = reader.ReadToEnd();

                        // XML에서 텍스트 추출 (간단한 방식)
                        var text = ExtractTextFromXml(xml);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine(text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "HWPX 파싱 실패: {FilePath}", filePath);
                throw;
            }

            return sb.ToString();
        }

        /// <summary>
        /// XML에서 텍스트 노드 추출
        /// </summary>
        private string ExtractTextFromXml(string xml)
        {
            var sb = new StringBuilder();

            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);

                // 모든 텍스트 노드 수집
                ExtractTextNodes(doc.DocumentElement, sb);
            }
            catch
            {
                // XML 파싱 실패 시 정규식으로 태그 제거
                var text = System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                sb.Append(text.Trim());
            }

            return sb.ToString();
        }

        /// <summary>
        /// XML 노드에서 텍스트 재귀 추출
        /// </summary>
        private void ExtractTextNodes(System.Xml.XmlNode? node, StringBuilder sb)
        {
            if (node == null) return;

            if (node.NodeType == System.Xml.XmlNodeType.Text)
            {
                var text = node.Value?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    sb.Append(text);
                    sb.Append(' ');
                }
            }

            foreach (System.Xml.XmlNode child in node.ChildNodes)
            {
                ExtractTextNodes(child, sb);
            }
        }

        /// <summary>
        /// 폴백: 바이너리에서 텍스트 추출 시도
        /// </summary>
        private string TryFallbackExtraction(string filePath)
        {
            _logger.Information("폴백 텍스트 추출 시도: {FilePath}", filePath);

            try
            {
                var bytes = File.ReadAllBytes(filePath);
                var sb = new StringBuilder();

                // UTF-16 LE로 디코딩 시도 (HWP 내부 텍스트)
                for (int i = 0; i < bytes.Length - 1; i += 2)
                {
                    if (bytes[i] >= 0x20 && bytes[i] < 0x7F && bytes[i + 1] == 0)
                    {
                        // ASCII 범위
                        sb.Append((char)bytes[i]);
                    }
                    else if (bytes[i + 1] >= 0xAC && bytes[i + 1] <= 0xD7)
                    {
                        // 한글 범위 (가-힣)
                        var ch = (char)(bytes[i] | (bytes[i + 1] << 8));
                        sb.Append(ch);
                    }
                }

                var result = sb.ToString();

                // 의미 있는 텍스트가 추출되었는지 확인
                if (result.Length > 50)
                {
                    _logger.Information("폴백 추출 성공, 길이: {Length}", result.Length);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "폴백 추출 실패");
            }

            return $"[HWP 파일 텍스트 추출 실패: {Path.GetFileName(filePath)}]";
        }
    }
}
