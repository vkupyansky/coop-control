using System;
using System.Linq;
using OGCoop.Shared;
using Lidgren.Network;

internal static partial class Program
{
    private static void CheckInstalledWireCodecs()
    {
        long serial = (long)int.MaxValue + 170;
        var world = new WorldStateSnapshot(Epoch, serial, true, true, true, 23, 59, 58, 6);
        object[][] inputs =
        {
            WorldStateProtocol.Encode(world, 3),
            GhostTrafficProtocol.EncodeReport(3, Epoch, serial, new[] { new GhostTrafficClaim(-101, int.MinValue, true), new GhostTrafficClaim(3, int.MaxValue, false) }),
            GhostTrafficProtocol.EncodeState(3, Epoch, serial, Enumerable.Range(1, GhostTrafficProtocol.MaximumEntries)
                .Select(id => new GhostTrafficEntry(id, 100 + id, -20)).ToArray())
        };
        foreach (bool guarded in new[] { false, true })
        {
            if (guarded) EngineCodecGuard.Install();
            try
            {
                foreach (bool queued in new[] { false, true })
                {
                    object[][] outputs = inputs.Select(args => InstalledWireRoundtrip(args, queued)).ToArray();
                    Assert(WorldStateProtocol.TryRead(outputs[0], out int session, out _, out _, out _, out WorldStateSnapshot? actualWorld) &&
                        session == 3 && actualWorld != null && actualWorld.Revision == serial && actualWorld.TimeFrozen && actualWorld.WeatherFrozen &&
                        actualWorld.GhostTrafficEnabled && actualWorld.Hour == 23 && actualWorld.Minute == 59 && actualWorld.Second == 58 && actualWorld.WeatherIndex == 6,
                        "Actual installed Core preserves the 13-field world snapshot, including Int64 revision and bit-unaligned booleans.");
                    Assert(GhostTrafficProtocol.TryReadReport(outputs[1], out _, out _, out long reportSerial, out GhostTrafficClaim[] report) &&
                        reportSerial == serial && report.Length == 2 && report[0].ModelHash == int.MinValue && report[0].Ambient && !report[1].Ambient,
                        "Actual installed Core preserves ghost report Int64 sequence and signed binary claim payload.");
                    Assert(GhostTrafficProtocol.TryReadState(outputs[2], out _, out _, out long stateSerial, out GhostTrafficEntry[] state) &&
                        stateSerial == serial && state.Length == GhostTrafficProtocol.MaximumEntries && state.All(entry => entry.OwnerId == -20),
                        "Actual installed Core preserves the maximum bounded ghost registry byte array and Int64 revision.");
                }
            }
            finally { if (guarded) EngineCodecGuard.Uninstall(); }
        }
    }

    private static object[] InstalledWireRoundtrip(object[] expected, bool queued)
    {
        Type packetType = EngineCodecGuard.FindCustomEventTarget().DeclaringType!;
        object sent = Activator.CreateInstance(packetType, new object?[] { null, queued })!;
        packetType.GetProperty("Hash")!.SetValue(sent, GhostTrafficProtocol.State);
        packetType.GetProperty("Args")!.SetValue(sent, expected);
        object received = Activator.CreateInstance(packetType, new object?[] { null, queued })!;
        var output = (NetOutgoingMessage)Activator.CreateInstance(typeof(NetOutgoingMessage), true)!;
        packetType.GetMethod("Pack")!.Invoke(sent, new object[] { output });
        var input = (NetIncomingMessage)Activator.CreateInstance(typeof(NetIncomingMessage), true)!;
        input.Data = output.Data; input.LengthBits = output.LengthBits;
        input.ReadByte();
        packetType.GetMethod("Deserialize")!.Invoke(received, new object[] { input });
        object[] actual = (object[])packetType.GetProperty("Args")!.GetValue(received)!;
        Assert(input.Position == input.LengthBits && actual.Length == expected.Length &&
            (int)packetType.GetProperty("Hash")!.GetValue(received)! == GhostTrafficProtocol.State &&
            actual.Where((value, index) => value.GetType() != expected[index].GetType() ||
                (value is byte[] bytes ? !bytes.SequenceEqual((byte[])expected[index]) : !value.Equals(expected[index]))).Count() == 0,
            "Real CustomEvent Pack/Deserialize consumes exactly the written bits and preserves every argument's runtime type and value.");
        return actual;
    }
}
