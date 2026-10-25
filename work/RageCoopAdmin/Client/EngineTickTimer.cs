using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RageCoop.Client;

namespace RageCoopAdmin.Client;

// Freeze diagnostics only: times RageCoop's own per-frame work so a freeze in
// the log says whether RageCoop's sync (Main), its world script (WorldThread) or
// the resources' tick (our menu among them) held the frame, or none of them
// (then the game itself stood still). No behaviour changes. Game thread only.
internal static class EngineTickTimer
{
    private const string PatchId = "friendslegacy.ragecoop.ticktimer";
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly List<MethodInfo> Patched = new List<MethodInfo>();
    private static Harmony? _harmony;

    // Longest run of each since the last sample, in milliseconds.
    internal static long Main { get; private set; }
    internal static long World { get; private set; }
    internal static long Resources { get; private set; }

    // Ticks started and not finished, for the stopped-game log (HangWatch),
    // which reads them from its timer thread.
    private static volatile bool _mainRunning;
    private static volatile bool _worldRunning;
    private static volatile bool _resourcesRunning;

    internal static string Running()
    {
        string running = (_mainRunning ? "Main " : "") + (_worldRunning ? "WorldThread " : "") + (_resourcesRunning ? "resources" : "");
        return running.Length == 0 ? "none" : running.TrimEnd();
    }

    internal static MethodInfo? FindMain() => FindTick("RageCoop.Client.Main");
    internal static MethodInfo? FindWorld() => FindTick("RageCoop.Client.WorldThread");
    internal static MethodInfo? FindResources() =>
        typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.Scripting.API+Events", throwOnError: false)?
            .GetMethod("InvokeTick", Static, null, Type.EmptyTypes, null);

    internal static bool Install()
    {
        if (_harmony != null) { return true; }
        MethodInfo? main = FindMain(), world = FindWorld(), resources = FindResources();
        if (main == null || world == null || resources == null) { return false; }
        var harmony = new Harmony(PatchId);
        try
        {
            Patch(harmony, main, nameof(BeginMain), nameof(EndMain));
            Patch(harmony, world, nameof(BeginWorld), nameof(EndWorld));
            Patch(harmony, resources, nameof(BeginResources), nameof(EndResources));
        }
        catch
        {
            harmony.UnpatchAll(PatchId);
            Patched.Clear();
            throw;
        }

        _harmony = harmony;
        return true;
    }

    internal static void Uninstall()
    {
        if (_harmony == null) { return; }
        foreach (MethodInfo target in Patched)
        {
            _harmony.Unpatch(target, HarmonyPatchType.Prefix, PatchId);
            _harmony.Unpatch(target, HarmonyPatchType.Postfix, PatchId);
        }

        Patched.Clear();
        _harmony = null;
        _mainRunning = _worldRunning = _resourcesRunning = false;
        Sample();
    }

    // Called once a frame by the freeze log, after reading the values.
    internal static void Sample()
    {
        Main = 0;
        World = 0;
        Resources = 0;
    }

    private static MethodInfo? FindTick(string type) =>
        typeof(SyncedVehicle).Assembly.GetType(type, throwOnError: false)?
            .GetMethod("OnTick", Instance, null, new[] { typeof(object), typeof(EventArgs) }, null);

    private static void Patch(Harmony harmony, MethodInfo target, string begin, string end)
    {
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(EngineTickTimer), begin),
            postfix: new HarmonyMethod(typeof(EngineTickTimer), end));
        Patched.Add(target);
    }

    private static void BeginMain(out long __state) { _mainRunning = true; __state = Stopwatch.GetTimestamp(); }
    private static void BeginWorld(out long __state) { _worldRunning = true; __state = Stopwatch.GetTimestamp(); }
    private static void BeginResources(out long __state) { _resourcesRunning = true; __state = Stopwatch.GetTimestamp(); }
    private static void EndMain(long __state) { _mainRunning = false; Main = Math.Max(Main, Elapsed(__state)); }
    private static void EndWorld(long __state) { _worldRunning = false; World = Math.Max(World, Elapsed(__state)); }
    private static void EndResources(long __state) { _resourcesRunning = false; Resources = Math.Max(Resources, Elapsed(__state)); }
    private static long Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency;
}
