using System.Linq;

namespace OGCoop.Shared;

// A player's home is an interior key of the client catalog (OnlineInteriors). The server
// keeps it per RageCoop name beside the garage, and only the player sees it.
internal static class HomeProtocol
{
    // Client to server: []. Server to client: [string home], "" without a home.
    internal const int Request = 20774200;
    internal const int State = 20774201;
    // Client to server: [string home]. Server to client: [bool saved].
    internal const int Save = 20774202;
    internal const int Saved = 20774203;
}

internal static class HomeCodec
{
    internal const int MaximumKeyLength = 64;

    // An empty key means no home, otherwise lower-case Latin letters, digits and underscores.
    internal static bool IsKey(string? key) =>
        key != null && key.Length <= MaximumKeyLength && key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}
