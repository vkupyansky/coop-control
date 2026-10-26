using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OGCoop.Shared;

namespace RageCoopAdmin.Client;

// What this game knows about yachts: the player's own one, as the server keeps
// it, and those of the other players on the server by RageCoop name. Game
// thread only.
internal sealed class YachtLooks
{
    private readonly Dictionary<string, YachtLook> _others = new Dictionary<string, YachtLook>(StringComparer.OrdinalIgnoreCase);
    private readonly Action<int, object[]> _send;

    internal YachtLooks(Action<int, object[]> send) => _send = send;

    internal bool OwnKnown { get; private set; }
    // Null while the player has no yacht.
    internal YachtLook? Own { get; private set; }
    // Changes whenever a yacht on the map appears, moves, changes or goes.
    internal int Revision { get; private set; }

    internal void Request() => _send(YachtProtocol.Request, Array.Empty<object>());

    internal void Save(YachtLook look) => _send(YachtProtocol.Save, new object[] { YachtCodec.Encode(look) });

    internal bool ApplyState(object[] args)
    {
        if (args is not { Length: 1 } || args[0] is not string xml) return false;
        YachtLook? look = null;
        if (xml.Length != 0 && !TryDecode(xml, out look)) return false;
        Own = look;
        OwnKnown = true;
        Revision++;
        return true;
    }

    // Another player's yacht; an empty one means it left with its owner.
    internal bool ApplyLook(object[] args)
    {
        if (args is not { Length: 2 } || args[0] is not string name || name.Length == 0 || args[1] is not string xml) return false;
        if (xml.Length == 0)
        {
            if (_others.Remove(name)) Revision++;
            return true;
        }
        if (!TryDecode(xml, out YachtLook? look)) return false;
        _others[name] = look!;
        Revision++;
        return true;
    }

    internal void Saved(YachtLook look)
    {
        Own = YachtCodec.Copy(look);
        Revision++;
    }

    // Every yacht to put on the map: the owner's name (empty for this player's).
    internal IEnumerable<(string Owner, YachtLook Look)> All()
    {
        if (Own != null) yield return (string.Empty, Own);
        foreach (KeyValuePair<string, YachtLook> other in _others.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            yield return (other.Key, other.Value);
    }

    // A berth another player's yacht stands at.
    internal bool TakenByOther(int berth) => _others.Values.Any(look => look.Berth == berth);

    internal void Clear()
    {
        _others.Clear();
        Own = null;
        OwnKnown = false;
        Revision++;
    }

    private static bool TryDecode(string xml, out YachtLook? look)
    {
        try
        {
            look = YachtCodec.Decode(xml);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            look = null;
            return false;
        }
    }
}
