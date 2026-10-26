using System;
using System.Reflection;
using OGCoop.Shared;
using HarmonyLib;
using RageCoop.Client;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// The stock handler always assigns both world components. Filter its two parts
// independently while a freeze is active, so a queued host update cannot undo it.
internal static class WorldStateEngineGuard
{
    private const string Owner = "friendslegacy.ragecoop.worldstate";
    private static readonly MethodInfo Target = typeof(SyncedEntity).Assembly
        .GetType("RageCoop.Client.Scripting.BaseScript", true)!
        .GetMethod("WeatherTimeSync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static Harmony? harmony;
    private static WorldStateController? controller;

    internal static void Install(WorldStateController value)
    {
        if (ReferenceEquals(controller, value)) return;
        if (controller != null) throw new InvalidOperationException("A world state controller is already active.");
        if (Target.ReturnType != typeof(void) || Target.GetParameters().Length != 1 ||
            Target.GetParameters()[0].ParameterType != typeof(CustomEventReceivedArgs) ||
            !EngineContract.HasBody(Target, "7402f1ff56b6b6fc10d2a97e7dd36316c44a872ce5630da3b79662cf8f31a622"))
            throw new InvalidOperationException("Unsupported RageCoop weather/time handler.");
        var patch = new Harmony(Owner);
        try
        {
            patch.Patch(Target, prefix: new HarmonyMethod(typeof(WorldStateEngineGuard), nameof(BeforeHostWorld)));
        }
        catch
        {
            patch.Unpatch(Target, HarmonyPatchType.Prefix, Owner);
            throw;
        }
        harmony = patch;
        controller = value;
    }

    internal static void Uninstall(WorldStateController value)
    {
        if (!ReferenceEquals(controller, value)) return;
        controller = null;
        harmony?.Unpatch(Target, HarmonyPatchType.Prefix, Owner);
        harmony = null;
    }

    private static bool BeforeHostWorld(CustomEventReceivedArgs e)
    {
        WorldStateSnapshot? state = controller?.Snapshot;
        if (state == null || (!state.TimeFrozen && !state.WeatherFrozen)) return true;
        object[] args = e.Args;
        if (args.Length != 6 || args[0] is not int hour || args[1] is not int minute || args[2] is not int second ||
            !WorldStateProtocol.IsClock(hour, minute, second) || args[3] is not int weather1 || args[4] is not int weather2 ||
            args[5] is not float blend || !Finite.Is(blend) || blend < 0 || blend > 1 ||
            WorldStateProtocol.WeatherIndexForHash(weather1) < 0 || WorldStateProtocol.WeatherIndexForHash(weather2) < 0) return false;
        if (!state.TimeFrozen) WorldStateNatives.SetClock(hour, minute, second);
        if (!state.WeatherFrozen) WorldStateNatives.ApplyNaturalWeather(weather1, weather2, blend);
        return false;
    }
}
