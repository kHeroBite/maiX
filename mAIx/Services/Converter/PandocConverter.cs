using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace mAIx.Services.Converter
{
    /// <summary>
    /// Pandoc CLI 기반 문서 변환기
    /// .docx, .doc, .pdf, .pptx, .xlsx, .odt, .rtf 등 지원
    /// </summary>
    public class PandocConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private readonly string _pandocPath;
        private bool? _isAvailable;

        private static readonly string[] _supportedExtensions =
        {
            ".docx", ".doc", ".pdf", ".pptx", ".xlsx",
            ".odt", ".ods", ".odp", ".rtf", ".epub",
            ".html", ".htm", ".md", ".markdown", ".txt",
            ".csv", ".tsv", ".xml", ".json"
        };

        public PandocConverter()
        {
            _logger = Log.ForContext<PandocConverter>();
            _pandocPath = FindPandocPath();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "Pandoc";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "Pandoc (범용)";

        /// <summary>
        /// 우선순위 (낮을수록 우선)
        /// </summary>
        public int Priority => 100;

        /// <summary>
        /// 지원 확장자 목록
        /// </summary>
        public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

        /// <summary>
        /// Pandoc 설치 여부 확인
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    _isAvailable = CheckPandocInstalled();
                }
                return _isAvailable.Value;
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
        /// 문서를 텍스트로 변환 (Pandoc 사용)
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("파일을 찾을 수 없습니다.", filePath);

            if (!IsAvailable)
                throw new InvalidOperationException("Pandoc이 설치되어 있지 않습니다.");

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("Pandoc 변환 시작: {FilePath}", filePath);

            try
            {
                // PDF는 별도 처리 (pdftotext 또는 Pandoc + pdflatex)
                if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return await ConvertPdfAsync(filePath, ct).ConfigureAwait(false);
                }

                return await RunPandocAsync(filePath, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Pandoc 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Pandoc 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// Pandoc 실행
        /// </summary>
        private async Task<string> RunPandocAsync(string filePath, CancellationToken ct)
        {
            var arguments = $"--to plain --wrap=none \"{filePath}\"";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pandocPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    error.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 타임아웃 60초
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw;
            }

            if (process.ExitCode != 0)
            {
                var errorMsg = error.ToString().Trim();
                _logger.Warning("Pandoc 오류 (ExitCode={ExitCode}): {Error}",
                    process.ExitCode, errorMsg);

                // 부분적인 출력이 있으면 반환
                if (output.Length > 0)
                {
                    return output.ToString().Trim();
                }

                throw new InvalidOperationException($"Pandoc 변환 실패: {errorMsg}");
            }

            var result = output.ToString().Trim();
            _logger.Information("Pandoc 변환 완료: {FilePath}, 길이: {Length}",
                filePath, result.Length);

            return result;
        }

        /// <summary>
        /// PDF 변환 (pdftotext 또는 Pandoc)
        /// </summary>
        private async Task<string> ConvertPdfAsync(string filePath, CancellationToken ct)
        {
            // pdftotext (Poppler) 우선 시도
            var pdftotextPath = FindPdfToTextPath();
            if (!string.IsNullOrEmpty(pdftotextPath))
            {
                try
                {
                    return await RunPdfToTextAsync(filePath, pdftotextPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "pdftotext 실패, Pandoc으로 시도");
                }
            }

            // Pandoc으로 PDF 변환 시도
            try
            {
                return await RunPandocAsync(filePath, ct).ConfigureAwait(false);
            }
            catch
            {
                // 최후 수단: 바이너리에서 텍스트 추출
                return TryExtractTextFromPdf(filePath);
            }
        }

        /// <summary>
        /// pdftotext (Poppler) 실행
        /// </summary>
        private async Task<string> RunPdfToTextAsync(string filePath, string pdftotextPath, CancellationToken ct)
        {
            // - : 표준 출력으로 결과 전송
            var arguments = $"-layout -enc UTF-8 \"{filePath}\" -";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pdftotextPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    output.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            return output.ToString().Trim();
        }

        /// <summary>
        /// PDF에서 텍스트 추출 (최후 수단)
        /// </summary>
        private string TryExtractTextFromPdf(string filePath)
        {
            _logger.Information("PDF 바이너리 텍스트 추출 시도: {FilePath}", filePath);

            try
            {
                var bytes = File.ReadAllBytes(filePath);
                var content = Encoding.UTF8.GetString(bytes);

                var sb = new StringBuilder();
                var inStream = false;
                var streamContent = new StringBuilder();

                foreach (var line in content.Split('\n'))
                {
                    if (line.Contains("stream"))
                    {
                        inStream = true;
                        continue;
                    }
                    if (line.Contains("endstream"))
                    {
                        inStream = false;
                        // 텍스트 추출 시도
                        var text = ExtractReadableText(streamContent.ToString());
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine(text);
                        }
                        streamContent.Clear();
                        continue;
                    }

                    if (inStream)
                    {
                        streamContent.AppendLine(line);
                    }
                }

                var result = sb.ToString().Trim();
                if (result.Length > 100)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "PDF 바이너리 추출 실패");
            }

            return $"[PDF 텍스트 추출 실패: {Path.GetFileName(filePath)}]";
        }

        /// <summary>
        /// 바이너리에서 읽을 수 있는 텍스트 추출
        /// </summary>
        private string ExtractReadableText(string content)
        {
            var sb = new StringBuilder();
            foreach (var ch in content)
            {
                if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Pandoc 경로 찾기
        /// </summary>
        private string FindPandocPath()
        {
            // 1. PATH에서 검색
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];

            foreach (var dir in pathDirs)
            {
                var pandocPath = Path.Combine(dir, "pandoc.exe");
                if (File.Exists(pandocPath))
                    return pandocPath;

                // Unix 계열
                pandocPath = Path.Combine(dir, "pandoc");
                if (File.Exists(pandocPath))
                    return pandocPath;
            }

            // 2. 기본 설치 경로 확인 (Windows)
            var defaultPaths = new[]
            {
                @"C:\Program Files\Pandoc\pandoc.exe",
                @"C:\Program Files (x86)\Pandoc\pandoc.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Pandoc", "pandoc.exe")
            };

            foreach (var path in defaultPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // 3. winget 설치 경로 확인
            try
            {
                var wingetPackagesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Packages");

                if (Directory.Exists(wingetPackagesDir))
                {
                    var pandocDirs = Directory.GetDirectories(wingetPackagesDir, "JohnMacFarlane.Pandoc*");
                    foreach (var packageDir in pandocDirs)
                    {
                        var versionDirs = Directory.GetDirectories(packageDir, "pandoc-*");
                        foreach (var versionDir in versionDirs)
                        {
                            var exePath = Path.Combine(versionDir, "pandoc.exe");
                            if (File.Exists(exePath))
                                return exePath;
                        }

                        // 버전 디렉토리 없이 직접 존재하는 경우
                        var directPath = Path.Combine(packageDir, "pandoc.exe");
                        if (File.Exists(directPath))
                            return directPath;
                    }
                }
            }
            catch
            {
                // winget 경로 탐색 실패 시 무시
            }

            // 기본값
            return "pandoc";
        }

        /// <summary>
        /// pdftotext (Poppler) 경로 찾기
        /// </summary>
        private string? FindPdfToTextPath()
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];

            foreach (var dir in pathDirs)
            {
                var path = Path.Combine(dir, "pdftotext.exe");
                if (File.Exists(path))
                    return path;

                path = Path.Combine(dir, "pdftotext");
                if (File.Exists(path))
                    return path;
            }

            // Poppler 기본 설치 경로
            var defaultPaths = new[]
            {
                @"C:\Program Files\poppler\Library\bin\pdftotext.exe",
                @"C:\poppler\Library\bin\pdftotext.exe"
            };

            foreach (var path in defaultPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Pandoc 설치 확인
        /// </summary>
        private bool CheckPandocInstalled()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _pandocPath,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                var installed = output.Contains("pandoc", StringComparison.OrdinalIgnoreCase);
                _logger.Information("Pandoc 설치 확인: {Installed}, 경로: {Path}",
                    installed, _pandocPath);

                return installed;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Pandoc 설치 확인 실패");
                return false;
            }
        }
    }
}
