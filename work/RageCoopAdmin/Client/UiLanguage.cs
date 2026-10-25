using System.Xml.Serialization;

namespace RageCoopAdmin.Client;

public enum UiLanguage
{
    [XmlEnum("ru")]
    Russian = 0,
    [XmlEnum("en")]
    English = 1
}
