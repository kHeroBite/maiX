using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Identity.Client;

namespace MaiX.Services.Graph
{
    /// <summary>
    /// MSAL 토큰 캐시 헬퍼
    /// DPAPI를 사용하여 토큰을 안전하게 저장
    /// </summary>
    public static class TokenCacheHelper
    {
        /// <summary>
        /// 토큰 캐시 파일 경로
        /// </summary>
        private static readonly string CacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MaiX",
            "msal_token_cache.bin");

        private static readonly object FileLock = new object();

        /// <summary>
        /// 토큰 캐시 직렬화 활성화
        /// </summary>
        /// <param name="tokenCache">MSAL 토큰 캐시</param>
        public static void EnableSerialization(ITokenCache tokenCache)
        {
            tokenCache.SetBeforeAccess(BeforeAccessNotification);
            tokenCache.SetAfterAccess(AfterAccessNotification);
        }

        /// <summary>
        /// 캐시 접근 전 콜백 - 파일에서 캐시 로드
        /// </summary>
        private static void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            lock (FileLock)
            {
                try
                {
                    if (File.Exists(CacheFilePath))
                    {
                        byte[] encryptedData = File.ReadAllBytes(CacheFilePath);
                        byte[] decryptedData = ProtectedData.Unprotect(
                            encryptedData,
                            null,
                            DataProtectionScope.CurrentUser);
                        args.TokenCache.DeserializeMsalV3(decryptedData);
                    }
                }
                catch (CryptographicException)
                {
                    // 복호화 실패 시 캐시 파일 삭제
                    File.Delete(CacheFilePath);
                }
                catch (Exception)
                {
                    // 기타 오류 무시 - 새 토큰 획득 필요
                }
            }
        }

        /// <summary>
        /// 캐시 접근 후 콜백 - 캐시를 파일에 저장
        /// </summary>
        private static void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            if (args.HasStateChanged)
            {
                lock (FileLock)
                {
                    try
                    {
                        // 디렉토리 생성
                        string directory = Path.GetDirectoryName(CacheFilePath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        byte[] data = args.TokenCache.SerializeMsalV3();
                        byte[] encryptedData = ProtectedData.Protect(
                            data,
                            null,
                            DataProtectionScope.CurrentUser);
                        File.WriteAllBytes(CacheFilePath, encryptedData);
                    }
                    catch (Exception)
                    {
                        // 저장 실패 무시 - 다음 로그인 시 재인증 필요
                    }
                }
            }
        }

        /// <summary>
        /// 토큰 캐시 삭제
        /// </summary>
        public static void ClearCache()
        {
            lock (FileLock)
            {
                try
                {
                    if (File.Exists(CacheFilePath))
                    {
                        File.Delete(CacheFilePath);
                    }
                }
                catch (Exception)
                {
                    // 삭제 실패 무시
                }
            }
        }
    }
}
