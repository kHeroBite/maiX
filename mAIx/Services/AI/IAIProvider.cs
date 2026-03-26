using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace mAIx.Services.AI
{
    /// <summary>
    /// AI Provider 공통 인터페이스
    /// 다양한 AI 서비스(Claude, OpenAI, Gemini, Ollama, LM Studio)를 통합 관리
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>
        /// Provider 이름 (예: "Claude", "OpenAI", "Gemini")
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// 현재 설정된 모델명 (예: "gpt-4o-mini", "claude-3-5-sonnet")
        /// </summary>
        string ModelName { get; }

        /// <summary>
        /// Provider 사용 가능 여부 (API 키 설정, 서버 연결 상태 등)
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 동기식 텍스트 완성 요청
        /// </summary>
        /// <param name="prompt">사용자 프롬프트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>AI 응답 텍스트</returns>
        Task<string> CompleteAsync(string prompt, CancellationToken ct = default);

        /// <summary>
        /// 스트리밍 텍스트 완성 요청
        /// </summary>
        /// <param name="prompt">사용자 프롬프트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>스트리밍 응답 (토큰 단위)</returns>
        Task<IAsyncEnumerable<string>> StreamCompleteAsync(string prompt, CancellationToken ct = default);

        /// <summary>
        /// Provider 설정 구성
        /// </summary>
        /// <param name="apiKey">API 키</param>
        /// <param name="baseUrl">기본 URL (선택)</param>
        /// <param name="model">모델명 (선택)</param>
        void Configure(string apiKey, string baseUrl = null, string model = null);
    }
}
