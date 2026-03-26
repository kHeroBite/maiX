using System;
using System.Xml.Serialization;

namespace mAIx.Models.Settings;

/// <summary>
/// 데이터베이스 연결 설정
/// </summary>
[Serializable]
[XmlRoot("DatabaseSettings")]
public class DatabaseSettings
{
    /// <summary>
    /// SQLite 연결 문자열
    /// 기본값: %APPDATA%\mAIx\mAIx.db
    /// </summary>
    [XmlElement("ConnectionString")]
    public string ConnectionString { get; set; } = "Data Source=mAIx.db";
}
