using System;
using System.Linq;
using System.Reflection;
using CoopControl.Shared;
using Lidgren.Network;

internal static partial class Program
{
    private static void CheckVehicleDamageModel(bool guarded)
    {
        MethodInfo reader = EngineCodecGuard.FindPacketReaders().Single(method => method.DeclaringType!.Name == "VehicleSync");
        Type type = reader.DeclaringType!;
        PropertyInfo damage = type.GetProperty("DamageModel")!;
        PropertyInfo flags = type.GetProperty("Flags")!;
        object intact = Activator.CreateInstance(damage.PropertyType)!;
        object Damage(byte doors, byte windows = 0, short tires = 0)
        {
            object value = Activator.CreateInstance(damage.PropertyType)!;
            damage.PropertyType.GetProperty("BrokenDoors")!.SetValue(value, doors);
            damage.PropertyType.GetProperty("BrokenWindows")!.SetValue(value, windows);
            damage.PropertyType.GetProperty("BurstedTires")!.SetValue(value, tires);
            return value;
        }
        object Packet(int id, object model, bool full = true)
        {
            object packet = CodecSample(reader);
            type.GetProperty("ID")!.SetValue(packet, id);
            type.GetProperty("ModelHash")!.SetValue(packet, 1000 + id);
            damage.SetValue(packet, model);
            if (!full)
                flags.SetValue(packet, Enum.ToObject(flags.PropertyType,
                    Convert.ToInt32(flags.GetValue(packet)) & ~Convert.ToInt32(Enum.Parse(flags.PropertyType, "IsFullSync"))));
            return packet;
        }
        int Id(object packet) => (int)type.GetProperty("ID")!.GetValue(packet)!;

        object damaged = Packet(71, Damage(1, 3, 0x0f));
        object repaired = Packet(72, intact);
        object received = Activator.CreateInstance(type)!;
        CodecRoundtrip(damaged, received);
        CodecRoundtrip(repaired, received);
        if (!guarded)
        {
            Assert(damage.GetValue(received)!.Equals(Damage(1, 3, 0x0f)) && Id(received) == 72,
                "The actual original reused VehicleSync reader carries the prior vehicle's damage across a full intact packet.");
            object fresh = Activator.CreateInstance(type)!;
            CodecRoundtrip(repaired, fresh);
            Assert(damage.GetValue(fresh)!.Equals(intact), "A fresh original reader yields the intact model for the same packet.");
            return;
        }
        Assert(damage.GetValue(received)!.Equals(intact) && Id(received) == 72 && (int)type.GetProperty("ModelHash")!.GetValue(received)! == 1072,
            "A full intact packet clears the previous vehicle's damage and keeps its own ID and model.");

        CodecRoundtrip(damaged, received);
        CodecRoundtrip(Packet(71, intact), received);
        Assert(damage.GetValue(received)!.Equals(intact), "The same vehicle repaired in a later full packet becomes intact.");

        object shortPacket = Packet(73, intact, full: false);
        CodecRoundtrip(damaged, received);
        CodecRoundtrip(shortPacket, received);
        Assert(damage.GetValue(received)!.Equals(Damage(1, 3, 0x0f)),
            "A short packet carries no damage presence field and preserves the reusable full-state cache.");

        object replacement = Packet(74, Damage(0, 0, 0x01));
        CodecRoundtrip(damaged, received);
        CodecRoundtrip(replacement, received);
        Assert(damage.GetValue(received)!.Equals(Damage(0, 0, 0x01)), "A later full damaged model replaces every prior damage field.");
        Assert(CodecPack(replacement).LengthBits == CodecPack(repaired).LengthBits + 56,
            "A present damage model consumes its six fields after the presence bit; an intact model writes only the bit.");

        foreach (object packet in new[] { repaired, replacement, shortPacket })
        {
            NetOutgoingMessage wire = CodecPack(packet);
            for (int length = 8; length < wire.LengthBits; length++)
            {
                CodecRoundtrip(damaged, received);
                NetIncomingMessage input = CodecIncoming(wire);
                input.LengthBits = length; input.ReadByte();
                CodecReject(() => CodecDeserialize(received, input), "A truncated VehicleSync damage shape consumed backing-buffer data.");
                Assert(input.Position <= input.LengthBits, "Damage reset retains the packet reader's bit bounds.");
            }
            CodecRoundtrip(packet, received);
        }
        Console.WriteLine("PASS reused actual VehicleSync codec: damaged→intact for one/different IDs, replacement, short-state preservation and every-bit truncation.");
    }
}
