namespace OGCoop.Shared;

// Invitation to one of the hidden map places under Teleports. The place catalog
// and loading belong to the client, the server relays only a known place number.
internal static class SpecialPlaceProtocol
{
    internal const int Invite = 20774170;
    internal const int Offer = 20774171;
    internal const int PlaceCount = 15;
    internal const int MaximumNameLength = 64;

    internal static bool IsPlace(int place) => place >= 0 && place < PlaceCount;

    internal static bool TryReadInvite(object[]? args, out int place)
    {
        place = -1;
        if (args is not { Length: 1 } || args[0] is not int value || !IsPlace(value)) return false;
        place = value;
        return true;
    }

    internal static object[] EncodeOffer(int place, string? inviter) => new object[] { place, CleanName(inviter) };

    internal static bool TryReadOffer(object[]? args, out int place, out string inviter)
    {
        place = -1;
        inviter = string.Empty;
        if (args is not { Length: 2 } || args[0] is not int value || !IsPlace(value) || args[1] is not string name) return false;
        place = value;
        inviter = CleanName(name);
        return true;
    }

    // A nickname is shown in a notification: drop GTA formatting codes and
    // control characters and keep it short, without rewriting the name itself.
    internal static string CleanName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var builder = new System.Text.StringBuilder(name!.Length);
        foreach (char symbol in name)
        {
            if (symbol == '~' || char.IsControl(symbol)) continue;
            if (builder.Length == MaximumNameLength) break;
            builder.Append(symbol);
        }
        return builder.ToString().Trim();
    }
}
