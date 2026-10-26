using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using OGCoop.Shared;
using HarmonyLib;
using Lidgren.Network;
using RageCoop.Client;

namespace RageCoopAdmin.Client;

// The client message envelope reads type, ID, status and strings from the
// network before any packet guard runs; bound those reads. Full-snapshot
// damage reset lives in the shared codec guard for client and server alike.
internal static class EnginePacketGuard
{
    private const string PatchId = "friendslegacy.ragecoop.enginepacket";
    private static Harmony? _harmony;
    private static MethodInfo? _envelopeTarget;

    internal static MethodInfo? FindEnvelopeTarget() => typeof(SyncedEntity).Assembly.GetType("RageCoop.Client.Networking", false)?
        .GetMethod("ProcessMessage", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(NetIncomingMessage) }, null);

    internal static bool HasEnvelopeContract(MethodInfo target)
    {
        if (target != FindEnvelopeTarget() || target.ReturnType != typeof(void)) { return false; }
        return EngineContract.HasBody(target, "2eb20f51ef3a68412335373ce6772092f3f84d3344188fed8aca4c555b74abea");
    }

    internal static void Install()
    {
        if (_harmony != null) { return; }
        MethodInfo envelope = FindEnvelopeTarget() ?? throw Unsupported("Networking.ProcessMessage signature");
        if (!HasEnvelopeContract(envelope)) { throw Unsupported("Networking.ProcessMessage IL"); }
        var harmony = new Harmony(PatchId);
        harmony.Patch(envelope, transpiler: new HarmonyMethod(typeof(EnginePacketGuard), nameof(BoundEnvelopeReads)));
        _harmony = harmony;
        _envelopeTarget = envelope;
    }

    internal static void Uninstall()
    {
        if (_harmony == null || _envelopeTarget == null) { return; }
        _harmony.Unpatch(_envelopeTarget, HarmonyPatchType.Transpiler, PatchId);
        _harmony = null;
        _envelopeTarget = null;
    }

    internal static IEnumerable<CodeInstruction> BoundEnvelopeReads(IEnumerable<CodeInstruction> original)
    {
        var replacements = new Dictionary<MethodInfo, MethodInfo>();
        foreach (string name in new[] { "ReadByte", "ReadInt32", "ReadString" })
        {
            replacements.Add(typeof(NetBuffer).GetMethod(name, Type.EmptyTypes)!,
                typeof(EngineCodecGuard).GetMethod("Bounded" + name, BindingFlags.Static | BindingFlags.NonPublic)!);
        }
        foreach (CodeInstruction source in original)
        {
            var code = new CodeInstruction(source);
            if (code.operand is MethodInfo reader && replacements.TryGetValue(reader, out MethodInfo bounded))
            {
                code.opcode = OpCodes.Call;
                code.operand = bounded;
            }
            yield return code;
        }
    }

    private static InvalidOperationException Unsupported(string contract) =>
        new InvalidOperationException("Unsupported RageCoop engine packet contract: " + contract + ".");
}
