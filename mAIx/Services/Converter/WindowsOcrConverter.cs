using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace mAIx.Services.Converter
{
    /// <summary>
    /// Windows 내장 OCR 변환기
    /// PowerShell을 통해 Windows.Media.Ocr API를 호출하여 이미지에서 텍스트 추출
    /// </summary>
    public class WindowsOcrConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private static readonly string[] _supportedExtensions =
            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tiff", ".tif", ".webp" };
        private readonly Lazy<bool> _isAvailable;

        public WindowsOcrConverter()
        {
            _logger = Log.ForContext<WindowsOcrConverter>();
            _isAvailable = new Lazy<bool>(CheckAvailability);
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "WindowsOcr";

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        public string DisplayName => "Windows OCR";

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
        /// Windows 10 이상에서만 사용 가능
        /// </summary>
        public bool IsAvailable => _isAvailable.Value;

        /// <summary>
        /// Windows OCR 사용 가능 여부 확인
        /// </summary>
        private bool CheckAvailability()
        {
            try
            {
                // Windows 10 이상 확인
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return false;
                }

                var version = Environment.OSVersion.Version;
                if (version.Major < 10)
                {
                    _logger.Debug("Windows 10 미만 버전에서는 Windows OCR을 사용할 수 없습니다.");
                    return false;
                }

                // PowerShell 사용 가능 여부 확인
                var pwshPath = FindPowerShell();
                if (string.IsNullOrEmpty(pwshPath))
                {
                    _logger.Warning("PowerShell을 찾을 수 없습니다.");
                    return false;
                }

                _logger.Information("Windows OCR 사용 가능 (PowerShell: {Path})", pwshPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Windows OCR 가용성 확인 실패");
                return false;
            }
        }

        /// <summary>
        /// PowerShell 경로 찾기
        /// </summary>
        private string? FindPowerShell()
        {
            // PowerShell 7+ (pwsh)
            var pwshPaths = new[]
            {
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                @"C:\Program Files (x86)\PowerShell\7\pwsh.exe",
                "pwsh.exe"
            };

            foreach (var path in pwshPaths)
            {
                if (File.Exists(path) || IsInPath(path))
                {
                    return path;
                }
            }

            // Windows PowerShell 5.1
            var winPwshPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
            if (File.Exists(winPwshPath))
            {
                return winPwshPath;
            }

            return null;
        }

        /// <summary>
        /// 실행 파일이 PATH에 있는지 확인
        /// </summary>
        private bool IsInPath(string fileName)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = fileName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(1000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
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
        /// 이미지 파일을 텍스트로 변환 (OCR)
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("이미지 파일을 찾을 수 없습니다.", filePath);

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            if (!IsAvailable)
                throw new InvalidOperationException("Windows OCR을 사용할 수 없습니다.");

            _logger.Debug("Windows OCR 변환 시작: {FilePath}", filePath);

            try
            {
                return await ExtractTextFromImageAsync(filePath, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Windows OCR 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Windows OCR 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// PowerShell을 통해 Windows OCR 수행
        /// </summary>
        private async Task<string> ExtractTextFromImageAsync(string filePath, CancellationToken ct)
        {
            var pwshPath = FindPowerShell();
            if (string.IsNullOrEmpty(pwshPath))
            {
                throw new InvalidOperationException("PowerShell을 찾을 수 없습니다.");
            }

            // PowerShell 스크립트: Windows.Media.Ocr 사용
            var script = $@"
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Media.Ocr.OcrEngine, Windows.Media.Ocr, ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]

# 파일 열기
$path = '{filePath.Replace("'", "''").Replace("\\", "\\\\")}'
$fileTask = [Windows.Storage.StorageFile]::GetFileFromPathAsync($path)
$file = $fileTask.GetAwaiter().GetResult()

# 이미지 디코딩
$streamTask = $file.OpenAsync([Windows.Storage.FileAccessMode]::Read)
$stream = $streamTask.GetAwaiter().GetResult()

$decoderTask = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)
$decoder = $decoderTask.GetAwaiter().GetResult()

$bitmapTask = $decoder.GetSoftwareBitmapAsync()
$bitmap = $bitmapTask.GetAwaiter().GetResult()

# OCR 엔진 생성 (한국어 우선)
$ocrEngine = $null
$korLang = [Windows.Globalization.Language]::new('ko')
if ([Windows.Media.Ocr.OcrEngine]::IsLanguageSupported($korLang)) {{
    $ocrEngine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($korLang)
}}
if ($null -eq $ocrEngine) {{
    $engLang = [Windows.Globalization.Language]::new('en')
    if ([Windows.Media.Ocr.OcrEngine]::IsLanguageSupported($engLang)) {{
        $ocrEngine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($engLang)
    }}
}}
if ($null -eq $ocrEngine) {{
    $availableLangs = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages
    if ($availableLangs.Count -gt 0) {{
        $ocrEngine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($availableLangs[0])
    }}
}}

if ($null -eq $ocrEngine) {{
    throw 'OCR 엔진을 초기화할 수 없습니다.'
}}

# OCR 수행
$resultTask = $ocrEngine.RecognizeAsync($bitmap)
$result = $resultTask.GetAwaiter().GetResult()

# 결과 출력
$result.Text

# 리소스 정리
$stream.Dispose()
";

            var startInfo = new ProcessStartInfo
            {
                FileName = pwshPath,
                Arguments = "-NoProfile -NonInteractive -Command -",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // 스크립트 전송
            await process.StandardInput.WriteLineAsync(script);
            process.StandardInput.Close();

            // 결과 읽기
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // 프로세스 종료 대기 (타임아웃: 60초)
            var completed = await Task.Run(() => process.WaitForExit(60000), ct);

            if (!completed)
            {
                process.Kill();
                throw new TimeoutException("Windows OCR 처리 시간 초과");
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.Warning("Windows OCR 오류: {Error}", error);
                throw new InvalidOperationException($"Windows OCR 실패: {error}");
            }

            var result = output.Trim();
            _logger.Information("Windows OCR 변환 완료: {FilePath}, 길이: {Length}", filePath, result.Length);
            return result;
        }
    }
}
