using System;
using System.IO;
using System.Xml.Serialization;
using mailX.Models;
using mailX.Utils;

namespace mailX.Services.Storage;

/// <summary>
/// 로그인 설정 파일 관리 서비스
/// 저장 위치: %APPDATA%\mailX\conf\autologin.xml
/// </summary>
public class LoginSettingsService
{
    private static readonly string ConfPath = Path.Combine(App.AppDataPath, "conf");
    private static readonly string SettingsFilePath = Path.Combine(ConfPath, "autologin.xml");
    private static readonly XmlSerializer _serializer = new(typeof(LoginSettings));

    /// <summary>
    /// 로그인 설정 로드
    /// </summary>
    /// <returns>저장된 설정 또는 null</returns>
    public LoginSettings? Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Log4.Debug($"[LoginSettings] 설정 파일 없음: {SettingsFilePath}");
                return null;
            }

            using var stream = new FileStream(SettingsFilePath, FileMode.Open, FileAccess.Read);
            var settings = _serializer.Deserialize(stream) as LoginSettings;
            Log4.Debug($"[LoginSettings] 설정 로드 완료 - Email: {settings?.Email}");
            return settings;
        }
        catch (Exception ex)
        {
            Log4.Error($"[LoginSettings] 설정 로드 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 로그인 설정 저장
    /// </summary>
    /// <param name="settings">저장할 설정</param>
    public void Save(LoginSettings settings)
    {
        try
        {
            // conf 디렉토리 생성
            Directory.CreateDirectory(ConfPath);

            using var stream = new FileStream(SettingsFilePath, FileMode.Create, FileAccess.Write);
            _serializer.Serialize(stream, settings);
            Log4.Debug($"[LoginSettings] 설정 저장 완료 - Email: {settings.Email}");
        }
        catch (Exception ex)
        {
            Log4.Error($"[LoginSettings] 설정 저장 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 로그인 설정 삭제
    /// </summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Delete(SettingsFilePath);
                Log4.Debug("[LoginSettings] 설정 파일 삭제 완료");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[LoginSettings] 설정 삭제 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 설정 파일 존재 여부 확인
    /// </summary>
    public bool Exists()
    {
        return File.Exists(SettingsFilePath);
    }
}
