using System.Linq;

namespace OGCoop.Shared;

// своё жильё игрока: ключ интерьера из каталога клиента (OnlineInteriors),
// сервер хранит его по имени RageCoop рядом с гаражом, жильё видит только сам игрок
internal static class HomeProtocol
{
    // клиент серверу: [], сервер в ответ: [string home], пустая строка, если жилья нет
    internal const int Request = 20774200;
    internal const int State = 20774201;
    // клиент серверу: [string home], сервер в ответ: [bool saved]
    internal const int Save = 20774202;
    internal const int Saved = 20774203;
}

internal static class HomeCodec
{
    internal const int MaximumKeyLength = 64;

    // пустой ключ значит без жилья, иначе строчные латинские буквы, цифры и подчёркивание
    internal static bool IsKey(string? key) =>
        key != null && key.Length <= MaximumKeyLength && key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}
