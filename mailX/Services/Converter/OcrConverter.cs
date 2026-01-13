using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Tesseract;

namespace mailX.Services.Converter
{
    /// <summary>
    /// 이미지 OCR 변환기 (Tesseract 사용)
    /// .png, .jpg, .jpeg, .gif, .bmp, .tiff 지원
    /// </summary>
    public class OcrConverter : IDocumentConverter
    {
        private readonly ILogger _logger;
        private readonly string _tessdataPath;
        private bool? _isAvailable;

        // 기본 언어 설정 (한국어 + 영어)
        private const string DefaultLanguages = "kor+eng";

        private static readonly string[] _supportedExtensions =
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp",
            ".tiff", ".tif", ".webp"
        };

        public OcrConverter()
        {
            _logger = Log.ForContext<OcrConverter>();
            _tessdataPath = FindTessdataPath();
        }

        /// <summary>
        /// 변환기 이름
        /// </summary>
        public string Name => "OcrConverter";

        /// <summary>
        /// 지원 확장자 목록
        /// </summary>
        public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

        /// <summary>
        /// Tesseract 사용 가능 여부
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    _isAvailable = CheckTesseractAvailable();
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
        /// 이미지에서 텍스트 추출 (OCR)
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("이미지 파일을 찾을 수 없습니다.", filePath);

            if (!IsAvailable)
                throw new InvalidOperationException("Tesseract OCR이 사용 불가능합니다. tessdata 폴더를 확인하세요.");

            var extension = Path.GetExtension(filePath);
            if (!CanConvert(extension))
                throw new NotSupportedException($"지원하지 않는 확장자: {extension}");

            _logger.Debug("OCR 변환 시작: {FilePath}", filePath);

            try
            {
                return await Task.Run(() => PerformOcr(filePath), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("OCR 변환 취소됨: {FilePath}", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OCR 변환 실패: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// OCR 실행
        /// </summary>
        private string PerformOcr(string filePath)
        {
            try
            {
                using var engine = new TesseractEngine(_tessdataPath, DefaultLanguages, EngineMode.Default);

                // 이미지 로드
                using var img = Pix.LoadFromFile(filePath);

                // OCR 수행
                using var page = engine.Process(img);

                var text = page.GetText();
                var confidence = page.GetMeanConfidence();

                _logger.Information("OCR 완료: {FilePath}, 신뢰도: {Confidence:P0}, 길이: {Length}",
                    filePath, confidence, text.Length);

                // 신뢰도가 낮으면 경고
                if (confidence < 0.5)
                {
                    _logger.Warning("OCR 신뢰도가 낮음: {FilePath}, 신뢰도: {Confidence:P0}",
                        filePath, confidence);
                }

                return text.Trim();
            }
            catch (TesseractException ex)
            {
                _logger.Error(ex, "Tesseract 오류: {FilePath}", filePath);

                // 언어 데이터 누락 시 영어만으로 재시도
                if (ex.Message.Contains("kor") || ex.Message.Contains("Failed loading language"))
                {
                    return TryOcrWithEnglishOnly(filePath);
                }

                throw;
            }
        }

        /// <summary>
        /// 영어만으로 OCR 재시도
        /// </summary>
        private string TryOcrWithEnglishOnly(string filePath)
        {
            _logger.Information("영어 전용 OCR 재시도: {FilePath}", filePath);

            try
            {
                using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromFile(filePath);
                using var page = engine.Process(img);

                return page.GetText().Trim();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "영어 전용 OCR도 실패");
                return $"[OCR 실패: {Path.GetFileName(filePath)}]";
            }
        }

        /// <summary>
        /// tessdata 경로 찾기
        /// </summary>
        private string FindTessdataPath()
        {
            // 1. 환경 변수 확인
            var envPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            // 2. 실행 파일 경로 기준
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var tessdataInExe = Path.Combine(exeDir, "tessdata");
            if (Directory.Exists(tessdataInExe))
            {
                return tessdataInExe;
            }

            // 3. 기본 설치 경로 확인 (Windows)
            var defaultPaths = new[]
            {
                @"C:\Program Files\Tesseract-OCR\tessdata",
                @"C:\Program Files (x86)\Tesseract-OCR\tessdata",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Tesseract-OCR", "tessdata"),
                // vcpkg 설치 경로
                @"C:\vcpkg\installed\x64-windows\share\tessdata",
                // Chocolatey 설치 경로
                @"C:\tools\tesseract\tessdata"
            };

            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    return path;
                }
            }

            // 4. 사용자 프로필 내 설치 경로
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userTessdata = Path.Combine(userProfile, ".tessdata");
            if (Directory.Exists(userTessdata))
            {
                return userTessdata;
            }

            // 기본값 (실행 시 오류 발생 가능)
            _logger.Warning("tessdata 폴더를 찾을 수 없음. 기본 경로 사용: {Path}", tessdataInExe);
            return tessdataInExe;
        }

        /// <summary>
        /// Tesseract 사용 가능 여부 확인
        /// </summary>
        private bool CheckTesseractAvailable()
        {
            try
            {
                if (!Directory.Exists(_tessdataPath))
                {
                    _logger.Warning("tessdata 폴더 없음: {Path}", _tessdataPath);
                    return false;
                }

                // 최소 영어 데이터 파일 확인
                var engData = Path.Combine(_tessdataPath, "eng.traineddata");
                if (!File.Exists(engData))
                {
                    _logger.Warning("영어 학습 데이터 없음: {Path}", engData);
                    return false;
                }

                // 한국어 데이터 확인 (선택적)
                var korData = Path.Combine(_tessdataPath, "kor.traineddata");
                if (!File.Exists(korData))
                {
                    _logger.Information("한국어 학습 데이터 없음 (영어만 사용 가능): {Path}", korData);
                }

                // 실제 엔진 초기화 테스트
                using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);
                _logger.Information("Tesseract OCR 사용 가능, tessdata: {Path}", _tessdataPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Tesseract 초기화 실패");
                return false;
            }
        }

        /// <summary>
        /// 설치된 언어 목록 조회
        /// </summary>
        public List<string> GetInstalledLanguages()
        {
            var languages = new List<string>();

            if (!Directory.Exists(_tessdataPath))
                return languages;

            foreach (var file in Directory.GetFiles(_tessdataPath, "*.traineddata"))
            {
                var lang = Path.GetFileNameWithoutExtension(file);
                languages.Add(lang);
            }

            return languages;
        }

        /// <summary>
        /// 사용자 지정 언어로 OCR 수행
        /// </summary>
        public async Task<string> ConvertToTextAsync(string filePath, string languages, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("이미지 파일을 찾을 수 없습니다.", filePath);

            if (!IsAvailable)
                throw new InvalidOperationException("Tesseract OCR이 사용 불가능합니다.");

            _logger.Debug("OCR 변환 시작 (언어: {Languages}): {FilePath}", languages, filePath);

            return await Task.Run(() =>
            {
                using var engine = new TesseractEngine(_tessdataPath, languages, EngineMode.Default);
                using var img = Pix.LoadFromFile(filePath);
                using var page = engine.Process(img);
                return page.GetText().Trim();
            }, ct);
        }
    }
}
