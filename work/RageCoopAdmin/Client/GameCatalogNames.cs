using System;
using System.Text.RegularExpressions;
using GTA;

namespace RageCoopAdmin.Client;

internal static class GameCatalogNames
{
    private static readonly Regex WordBoundary = new Regex("([a-z0-9])([A-Z])|([A-Z])([A-Z][a-z])", RegexOptions.CultureInvariant);

    // GXT uses GTA's selected language. English menu names come from the
    // supported SDK's stable model identifiers rather than changing GTA globally.
    internal static string Weapon(WeaponHash hash, string gameName) =>
        Enum.GetName(typeof(WeaponHash), hash) is string identifier ? Readable(identifier) : gameName;

    internal static string Vehicle(int hash, string gameName)
    {
        bool cyrillic = false;
        foreach (char value in gameName) cyrillic |= value >= '\u0400' && value <= '\u052f';
        if (!cyrillic) return gameName;
        return Enum.GetName(typeof(VehicleHash), unchecked((uint)hash)) is string identifier ? Readable(identifier) : gameName;
    }

    private static string Readable(string identifier) =>
        WordBoundary.Replace(identifier, match => match.Groups[1].Success
            ? match.Groups[1].Value + " " + match.Groups[2].Value
            : match.Groups[3].Value + " " + match.Groups[4].Value).Replace("Mk2", "Mk II");
}
