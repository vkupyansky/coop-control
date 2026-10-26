using System;
using System.Collections.Generic;
using System.IO;

namespace OGCoop.Shared;

internal sealed class GhostTrafficClaim
{
    internal GhostTrafficClaim(int id, int modelHash, bool ambient) { Id = id; ModelHash = modelHash; Ambient = ambient; }
    internal int Id { get; }
    internal int ModelHash { get; }
    internal bool Ambient { get; }
}

internal sealed class GhostTrafficEntry
{
    internal GhostTrafficEntry(int id, int modelHash, int ownerId) { Id = id; ModelHash = modelHash; OwnerId = ownerId; }
    internal int Id { get; }
    internal int ModelHash { get; }
    internal int OwnerId { get; }
}

// CustomEvent supports byte[], not int[]. BinaryReader/BinaryWriter preserve the
// installed byte-array codec and give every report one bounded, atomic payload.
internal static class GhostTrafficProtocol
{
    internal const int Report = 20774145;
    internal const int State = 20774146;
    internal const int MaximumClaims = 128;
    internal const int MaximumEntries = 1024;
    private const int EntryBytes = 12;

    internal static object[] EncodeReport(int session, string epoch, long sequence, GhostTrafficClaim[] claims)
    {
        if (claims.Length > MaximumClaims) throw new ArgumentOutOfRangeException(nameof(claims));
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                foreach (GhostTrafficClaim claim in claims) { writer.Write(claim.Id); writer.Write(claim.ModelHash); writer.Write(claim.Ambient ? 1 : 0); }
                return new object[] { session, epoch, sequence, stream.ToArray() };
            }
        }
    }

    internal static object[] EncodeState(int session, string epoch, long revision, GhostTrafficEntry[] entries)
    {
        if (entries.Length > MaximumEntries) throw new ArgumentOutOfRangeException(nameof(entries));
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                foreach (GhostTrafficEntry entry in entries) { writer.Write(entry.Id); writer.Write(entry.ModelHash); writer.Write(entry.OwnerId); }
                return new object[] { session, epoch, revision, stream.ToArray() };
            }
        }
    }

    internal static bool TryReadReport(object[] args, out int session, out string epoch, out long sequence, out GhostTrafficClaim[] claims)
    {
        claims = Array.Empty<GhostTrafficClaim>();
        if (!TryReadHeader(args, MaximumClaims, out session, out epoch, out sequence, out byte[] payload) || sequence == 0) return false;
        var parsed = new GhostTrafficClaim[payload.Length / EntryBytes];
        var seen = new HashSet<int>();
        using (var reader = new BinaryReader(new MemoryStream(payload, false)))
        {
            for (int i = 0; i < parsed.Length; i++)
            {
                int id = reader.ReadInt32(), model = reader.ReadInt32(), kind = reader.ReadInt32();
                if (id == 0 || model == 0 || kind < 0 || kind > 1 || !seen.Add(id)) return false;
                parsed[i] = new GhostTrafficClaim(id, model, kind == 1);
            }
        }
        claims = parsed;
        return true;
    }

    internal static bool TryReadState(object[] args, out int session, out string epoch, out long revision, out GhostTrafficEntry[] entries)
    {
        entries = Array.Empty<GhostTrafficEntry>();
        if (!TryReadHeader(args, MaximumEntries, out session, out epoch, out revision, out byte[] payload)) return false;
        var parsed = new GhostTrafficEntry[payload.Length / EntryBytes];
        var seen = new HashSet<int>();
        using (var reader = new BinaryReader(new MemoryStream(payload, false)))
        {
            for (int i = 0; i < parsed.Length; i++)
            {
                int id = reader.ReadInt32(), model = reader.ReadInt32(), owner = reader.ReadInt32();
                if (id == 0 || model == 0 || owner == 0 || !seen.Add(id)) return false;
                parsed[i] = new GhostTrafficEntry(id, model, owner);
            }
        }
        entries = parsed;
        return true;
    }

    private static bool TryReadHeader(object[] args, int maximum, out int session, out string epoch, out long revision, out byte[] payload)
    {
        session = 0; epoch = string.Empty; revision = 0; payload = Array.Empty<byte>();
        if (args.Length != 4 || args[0] is not int token || token <= 0 ||
            args[1] is not string world || !IsEpoch(world) || args[2] is not long serial || serial < 0 ||
            args[3] is not byte[] body || body.Length > maximum * EntryBytes || body.Length % EntryBytes != 0) return false;
        session = token; epoch = world; revision = serial; payload = body;
        return true;
    }

    private static bool IsEpoch(string value)
    {
        if (value.Length != 32) return false;
        foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
        return true;
    }
}
