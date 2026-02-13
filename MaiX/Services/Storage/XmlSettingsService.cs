using System;
using System.IO;
using System.Xml.Serialization;
using MaiX.Utils;

namespace MaiX.Services.Storage;

/// <summary>
/// 범용 XML 설정 파일 로더/저장기
/// </summary>
/// <typeparam name="T">설정 클래스 타입</typeparam>
public class XmlSettingsService<T> where T : class, new()
{
    private readonly string _filePath;
    private readonly XmlSerializer _serializer;
    private readonly string _confPath;

    /// <summary>
    /// XML 설정 서비스 생성
    /// </summary>
    /// <param name="fileName">설정 파일명 (예: apikeys.xml)</param>
    public XmlSettingsService(string fileName)
    {
        _confPath = Path.Combine(App.AppDataPath, "conf");
        _filePath = Path.Combine(_confPath, fileName);
        _serializer = new XmlSerializer(typeof(T));
    }

    /// <summary>
    /// 설정 파일 경로
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 설정 파일 존재 여부
    /// </summary>
    public bool Exists => File.Exists(_filePath);

    /// <summary>
    /// 설정 로드 (파일이 없으면 기본값 반환)
    /// </summary>
    /// <returns>설정 객체</returns>
    public T Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                Log4.Debug($"[XmlSettings] 설정 파일 없음, 기본값 사용: {_filePath}");
                return new T();
            }

            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
            var settings = _serializer.Deserialize(stream) as T;
            Log4.Debug($"[XmlSettings] 설정 로드 완료: {_filePath}");
            return settings ?? new T();
        }
        catch (Exception ex)
        {
            Log4.Error($"[XmlSettings] 설정 로드 실패: {_filePath} - {ex.Message}");
            return new T();
        }
    }

    /// <summary>
    /// 설정 저장
    /// </summary>
    /// <param name="settings">저장할 설정</param>
    public void Save(T settings)
    {
        try
        {
            // conf 디렉토리 생성
            Directory.CreateDirectory(_confPath);

            using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write);
            _serializer.Serialize(stream, settings);
            Log4.Debug($"[XmlSettings] 설정 저장 완료: {_filePath}");
        }
        catch (Exception ex)
        {
            Log4.Error($"[XmlSettings] 설정 저장 실패: {_filePath} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 설정 파일 삭제
    /// </summary>
    public void Delete()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                Log4.Debug($"[XmlSettings] 설정 파일 삭제: {_filePath}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[XmlSettings] 설정 파일 삭제 실패: {_filePath} - {ex.Message}");
        }
    }
}
