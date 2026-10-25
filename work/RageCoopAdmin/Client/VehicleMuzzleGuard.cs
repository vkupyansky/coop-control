using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GTA;
using GTA.Native;
using HarmonyLib;
using RageCoop.Client;
using RageCoop.Core;

namespace RageCoopAdmin.Client;

// RageCoop knows the gun bones of a fixed list of vehicles (WeaponUtil.GetMuzzleIndex).
// For any other, the Lazer among them, it sends no shot to the other players and
// logs "Failed to get muzzle info" for every bullet: dozens of lines a second.
// GTA names vehicle gun bones weapon_1a..weapon_1d, so a vehicle RageCoop does
// not know fires from those it has, in turn. A vehicle without them is still
// reported, once per session instead of per bullet.
internal static class VehicleMuzzleGuard
{
    private const string PatchId = "friendslegacy.ragecoop.vehiclemuzzle";
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly VehicleMuzzleBones Bones = new VehicleMuzzleBones();
    private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);
    private static readonly MethodInfo Warning = typeof(Logger).GetMethod(nameof(Logger.Warning), new[] { typeof(string) })!;
    private static Harmony? _harmony;
    private static MethodInfo? _index;
    private static MethodInfo? _shot;

    internal static bool Install()
    {
        if (_harmony != null) { return true; }
        MethodInfo? index = FindIndexTarget(), shot = FindShotTarget();
        if (index == null || index.ReturnType != typeof(int) || shot == null ||
            CountWarnings(PatchProcessor.GetOriginalInstructions(shot)) != 1)
        {
            return false;
        }

        var harmony = new Harmony(PatchId);
        try
        {
            harmony.Patch(index, postfix: new HarmonyMethod(typeof(VehicleMuzzleGuard), nameof(AfterMuzzleIndex)));
            harmony.Patch(shot, transpiler: new HarmonyMethod(typeof(VehicleMuzzleGuard), nameof(WarnOncePerVehicle)));
        }
        catch
        {
            harmony.UnpatchAll(PatchId);
            throw;
        }

        _index = index;
        _shot = shot;
        _harmony = harmony;
        return true;
    }

    internal static void Uninstall()
    {
        if (_harmony == null) { return; }
        if (_index != null) _harmony.Unpatch(_index, HarmonyPatchType.Postfix, PatchId);
        if (_shot != null) _harmony.Unpatch(_shot, HarmonyPatchType.Transpiler, PatchId);
        _harmony = null;
        _index = null;
        _shot = null;
        lock (Warned) { Warned.Clear(); }
    }

    internal static MethodInfo? FindIndexTarget() =>
        typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.WeaponUtil", throwOnError: false)?
            .GetMethod("GetMuzzleIndex", Static, null, new[] { typeof(Vehicle) }, null);

    internal static MethodInfo? FindShotTarget()
    {
        Assembly engine = typeof(SyncedVehicle).Assembly;
        Type? ped = engine.GetType("RageCoop.Client.SyncedPed", throwOnError: false);
        return ped == null ? null : engine.GetType("RageCoop.Client.SyncEvents", throwOnError: false)?
            .GetMethod("TriggerVehBulletShot", Static, null, new[] { typeof(uint), typeof(Vehicle), ped }, null);
    }

    internal static int CountWarnings(IEnumerable<CodeInstruction> instructions) =>
        instructions.Count(instruction => instruction.Calls(Warning));

    internal static IEnumerable<CodeInstruction> WarnOncePerVehicle(IEnumerable<CodeInstruction> original)
    {
        List<CodeInstruction> instructions = original.Select(instruction => new CodeInstruction(instruction)).ToList();
        if (CountWarnings(instructions) != 1) { throw new InvalidOperationException("Unsupported RageCoop vehicle shot warning."); }
        CodeInstruction call = instructions.Single(instruction => instruction.Calls(Warning));
        // Same stack (logger, message): an instance call becomes a static one.
        call.opcode = OpCodes.Call;
        call.operand = typeof(VehicleMuzzleGuard).GetMethod(nameof(WarnOnce), Static);
        return instructions;
    }

    internal static void WarnOnce(Logger logger, string message)
    {
        if (FirstWarning(message)) logger.Warning(message + " (logged once per session)");
    }

    internal static bool FirstWarning(string message)
    {
        lock (Warned) { return Warned.Add(message); }
    }

    private static void AfterMuzzleIndex(Vehicle __0, ref int __result)
    {
        if (__result != -1 || __0 == null || !__0.Exists()) { return; }
        Vehicle vehicle = __0;
        __result = Bones.Next(vehicle.Model.Hash, name => Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, vehicle.Handle, name));
    }
}

// Gun bones by vehicle model, looked up once per model and used in turn.
internal sealed class VehicleMuzzleBones
{
    internal static readonly string[] Names = { "weapon_1a", "weapon_1b", "weapon_1c", "weapon_1d" };
    private readonly Dictionary<int, int[]> _byModel = new Dictionary<int, int[]>();
    private uint _shot;

    internal int Next(int model, Func<string, int> boneIndex)
    {
        if (!_byModel.TryGetValue(model, out int[]? bones))
        {
            bones = Names.Select(boneIndex).Where(index => index >= 0).ToArray();
            _byModel[model] = bones;
        }

        return bones.Length == 0 ? -1 : bones[unchecked(_shot++) % (uint)bones.Length];
    }
}
