using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MaiX.Services.Converter
{
    /// <summary>
    /// HwpSharp 라이브러리를 사용한 HWP 파일 변환기
    /// .hwp, .hwpx 파일을 텍스트로 변환
    /// </summary>
    public class HwpSharpConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions = { ".hwp", ".hwpx" };
        private readonly Lazy<bool> _isAvailable;
        private readonly Lazy<Type?> _hwpDocumentType;

        public HwpSharpConverter()
        {
            _logger = Log.ForContext<HwpSharpConverter>();
            _hwpDocumentType = new Lazy<Type?>(FindHwpDocumentType);
            _isAvailable = new Lazy<bool>(() => _hwpDocumentType.Value != null);
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "HwpSharp";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "HwpSharp (HWP)";

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
        public bool IsAvailable => _isAvailable.Value;

        /// <summary>
        /// HwpSharp 라이브러리에서 HwpDocument 타입 찾기
        /// </summary>
        private Type? FindHwpDocumentType()
        {
            try
            {
                // HwpSharp 어셈블리 로드 시도
                var assembly = Assembly.Load("HwpSharp");
                if (assembly == null)
                {
                    _logger.Warning("HwpSharp 어셈블리를 로드할 수 없습니다.");
                    return null;
                }

                // HwpDocument 또는 유사한 타입 찾기
                var types = assembly.GetExportedTypes();
                var hwpDocType = types.FirstOrDefault(t =>
                    t.Name.Contains("HwpDocument") ||
                    t.Name.Contains("Document") ||
                    t.Name.Contains("Hwp"));

                if (hwpDocType != null)
                {
                    _logger.Information("HwpSharp 타입 발견: {TypeName}", hwpDocType.FullName);
                }
                else
                {
                    _logger.Warning("HwpSharp에서 문서 타입을 찾을 수 없습니다. 사용 가능한 타입: {Types}",
                        string.Join(", ", types.Select(t => t.Name)));
                }

                return hwpDocType;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "HwpSharp 어셈블리 검사 중 오류");
                return null;
            }
        }

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

            if (!IsAvailable)
            {
                _logger.Warning("HwpSharp를 사용할 수 없어 폴백 방식으로 처리합니다.");
                return await Task.Run(() => ExtractTextFallback(filePath), ct);
            }

            _logger.Debug("HwpSharp 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => ExtractTextFromHwp(filePath), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("HwpSharp 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "HwpSharp 변환 실패, 폴백 시도: {FilePath}", filePath);
                return await Task.Run(() => ExtractTextFallback(filePath), ct);
            }
        }

        /// <summary>
        /// HwpSharp를 사용하여 HWP 파일에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromHwp(string filePath)
        {
            var sb = new StringBuilder();

            try
            {
                var hwpDocType = _hwpDocumentType.Value;
                if (hwpDocType == null)
                {
                    return ExtractTextFallback(filePath);
                }

                // 리플렉션을 통한 동적 호출
                // Open 또는 Load 메서드 찾기
                var openMethod = hwpDocType.GetMethod("Open", BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(string) }, null);

                if (openMethod == null)
                {
                    openMethod = hwpDocType.GetMethod("Load", BindingFlags.Static | BindingFlags.Public,
                        null, new[] { typeof(string) }, null);
                }

                if (openMethod == null)
                {
                    // 생성자 사용 시도
                    var constructor = hwpDocType.GetConstructor(new[] { typeof(string) });
                    if (constructor != null)
                    {
                        var doc = constructor.Invoke(new object[] { filePath });
                        return ExtractTextFromDocument(doc);
                    }
                }
                else
                {
                    var doc = openMethod.Invoke(null, new object[] { filePath });
                    if (doc != null)
                    {
                        return ExtractTextFromDocument(doc);
                    }
                }

                // 메서드를 찾지 못한 경우 폴백
                return ExtractTextFallback(filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "HwpSharp 텍스트 추출 실패: {FilePath}", filePath);
                return ExtractTextFallback(filePath);
            }
        }

        /// <summary>
        /// 문서 객체에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromDocument(object doc)
        {
            var sb = new StringBuilder();
            var docType = doc.GetType();

            try
            {
                // GetText 메서드 찾기
                var getTextMethod = docType.GetMethod("GetText", BindingFlags.Instance | BindingFlags.Public);
                if (getTextMethod != null)
                {
                    var text = getTextMethod.Invoke(doc, null) as string;
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }

                // Text 속성 찾기
                var textProperty = docType.GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                if (textProperty != null)
                {
                    var text = textProperty.GetValue(doc) as string;
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }

                // BodyText 또는 Sections 속성 탐색
                var bodyTextProp = docType.GetProperty("BodyText", BindingFlags.Instance | BindingFlags.Public);
                if (bodyTextProp != null)
                {
                    var bodyText = bodyTextProp.GetValue(doc);
                    if (bodyText != null)
                    {
                        return ExtractTextFromBodyText(bodyText);
                    }
                }

                // ToString 폴백
                var toStringResult = doc.ToString();
                if (!string.IsNullOrEmpty(toStringResult) && toStringResult != docType.FullName)
                {
                    return toStringResult;
                }
            }
            finally
            {
                // IDisposable 처리
                if (doc is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// BodyText 객체에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromBodyText(object bodyText)
        {
            var sb = new StringBuilder();
            var bodyType = bodyText.GetType();

            // Sections 속성 찾기
            var sectionsProp = bodyType.GetProperty("Sections", BindingFlags.Instance | BindingFlags.Public);
            if (sectionsProp != null)
            {
                var sections = sectionsProp.GetValue(bodyText) as System.Collections.IEnumerable;
                if (sections != null)
                {
                    foreach (var section in sections)
                    {
                        var sectionText = ExtractTextFromSection(section);
                        if (!string.IsNullOrWhiteSpace(sectionText))
                        {
                            sb.AppendLine(sectionText);
                        }
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Section 객체에서 텍스트 추출
        /// </summary>
        private string ExtractTextFromSection(object section)
        {
            var sb = new StringBuilder();
            var sectionType = section.GetType();

            // Paragraphs 속성 찾기
            var paragraphsProp = sectionType.GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.Public);
            if (paragraphsProp != null)
            {
                var paragraphs = paragraphsProp.GetValue(section) as System.Collections.IEnumerable;
                if (paragraphs != null)
                {
                    foreach (var paragraph in paragraphs)
                    {
                        var paraType = paragraph.GetType();
                        var textProp = paraType.GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                        if (textProp != null)
                        {
                            var text = textProp.GetValue(paragraph) as string;
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                sb.AppendLine(text);
                            }
                        }
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 폴백: HWPX (ZIP/OOXML) 또는 바이너리에서 텍스트 추출
        /// </summary>
        private string ExtractTextFallback(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (extension == ".hwpx")
                {
                    return ExtractFromHwpx(filePath);
                }
                else
                {
                    return ExtractFromHwpBinary(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "폴백 추출 실패: {FilePath}", filePath);
                return $"[HWP 파일 텍스트 추출 실패: {Path.GetFileName(filePath)}]";
            }
        }

        /// <summary>
        /// HWPX 파일에서 텍스트 추출 (OOXML Format)
        /// </summary>
        private string ExtractFromHwpx(string filePath)
        {
            var sb = new StringBuilder();

            using var archive = ZipFile.OpenRead(filePath);

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase)
                    && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var xml = reader.ReadToEnd();

                    // XML에서 텍스트 추출
                    var text = ExtractTextFromXml(xml);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                }
            }

            var result = sb.ToString().Trim();
            _logger.Information("HWPX 변환 완료: {FilePath}, 길이: {Length}", filePath, result.Length);
            return result;
        }

        /// <summary>
        /// XML에서 텍스트 노드 추출
        /// </summary>
        private string ExtractTextFromXml(string xml)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);

                var sb = new StringBuilder();
                ExtractTextNodes(doc.DocumentElement, sb);
                return sb.ToString();
            }
            catch
            {
                // XML 파싱 실패 시 정규식으로 태그 제거
                var text = System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                return text.Trim();
            }
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
        /// HWP 바이너리에서 텍스트 추출 시도
        /// </summary>
        private string ExtractFromHwpBinary(string filePath)
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

            if (result.Length > 50)
            {
                _logger.Information("HWP 바이너리 추출 완료, 길이: {Length}", result.Length);
                return result;
            }

            return $"[HWP 파일 텍스트 추출 실패: {Path.GetFileName(filePath)}]";
        }
    }
}
