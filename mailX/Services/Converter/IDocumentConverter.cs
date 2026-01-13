using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace mailX.Services.Converter
{
    /// <summary>
    /// 문서 변환 인터페이스
    /// 다양한 문서 형식을 텍스트로 변환
    /// </summary>
    public interface IDocumentConverter
    {
        /// <summary>
        /// 변환기 고유 이름 (코드용)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// UI 표시 이름
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// 우선순위 (낮을수록 우선, 기본값 100)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 지원하는 파일 확장자 목록 (.hwp, .docx 등)
        /// </summary>
        IReadOnlyList<string> SupportedExtensions { get; }

        /// <summary>
        /// 해당 확장자를 변환할 수 있는지 확인
        /// </summary>
        /// <param name="extension">파일 확장자 (.포함)</param>
        /// <returns>변환 가능 여부</returns>
        bool CanConvert(string extension);

        /// <summary>
        /// 문서를 텍스트로 변환
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>추출된 텍스트</returns>
        Task<string> ConvertToTextAsync(string filePath, CancellationToken ct = default);

        /// <summary>
        /// 변환기 사용 가능 여부
        /// (외부 도구 의존 시 설치 확인)
        /// </summary>
        bool IsAvailable { get; }
    }

    /// <summary>
    /// 문서 변환 결과
    /// </summary>
    public class ConversionResult
    {
        /// <summary>
        /// 변환 성공 여부
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 추출된 텍스트
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 오류 메시지 (실패 시)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 원본 파일 경로
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 변환에 사용된 변환기 이름
        /// </summary>
        public string ConverterName { get; set; } = string.Empty;

        /// <summary>
        /// 변환 소요 시간
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 메타데이터 (표 개수, 이미지 개수 등)
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// 성공 결과 생성
        /// </summary>
        public static ConversionResult Succeeded(string text, string filePath, string converterName)
        {
            return new ConversionResult
            {
                Success = true,
                Text = text,
                FilePath = filePath,
                ConverterName = converterName
            };
        }

        /// <summary>
        /// 실패 결과 생성
        /// </summary>
        public static ConversionResult Failed(string errorMessage, string filePath, string converterName)
        {
            return new ConversionResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                FilePath = filePath,
                ConverterName = converterName
            };
        }
    }
}
