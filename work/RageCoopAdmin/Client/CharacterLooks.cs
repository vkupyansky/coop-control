using System;
using System.Collections.Generic;
using System.IO;
using OGCoop.Shared;

namespace RageCoopAdmin.Client;

// What this game knows about characters: the player's own one, as the server
// keeps it, and the other players' ones by RageCoop name (RageCoop compares
// names case-insensitively). Game thread only.
internal sealed class CharacterLooks
{
    private readonly Dictionary<string, CharacterLook> _others = new Dictionary<string, CharacterLook>(StringComparer.OrdinalIgnoreCase);
    private readonly Action<int, object[]> _send;

    internal CharacterLooks(Action<int, object[]> send) => _send = send;

    // The server has answered; until then nothing is offered or applied.
    internal bool OwnKnown { get; private set; }
    // Null while the player has not made a character yet.
    internal CharacterLook? Own { get; private set; }

    internal void Request() => _send(CharacterProtocol.Request, Array.Empty<object>());

    internal void Save(CharacterLook look) => _send(CharacterProtocol.Save, new object[] { CharacterCodec.Encode(look) });

    internal bool ApplyState(object[] args)
    {
        if (args is not { Length: 1 } || args[0] is not string xml) return false;
        CharacterLook? look = null;
        if (xml.Length != 0 && !TryDecode(xml, out look)) return false;
        Own = look;
        OwnKnown = true;
        return true;
    }

    internal bool ApplyLook(object[] args, out string username)
    {
        username = string.Empty;
        if (args is not { Length: 2 } || args[0] is not string name || name.Length == 0 || args[1] is not string xml ||
            !TryDecode(xml, out CharacterLook? look)) return false;
        _others[name] = look!;
        username = name;
        return true;
    }

    internal void Saved(CharacterLook look) => Own = look;

    internal CharacterLook? Of(string username) => _others.TryGetValue(username ?? string.Empty, out CharacterLook? look) ? look : null;

    internal void Clear()
    {
        _others.Clear();
        Own = null;
        OwnKnown = false;
    }

    private static bool TryDecode(string xml, out CharacterLook? look)
    {
        try
        {
            look = CharacterCodec.Decode(xml);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            look = null;
            return false;
        }
    }
}
