using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using OGCoop.Shared;
using GTA.Native;
using HarmonyLib;
using RageCoop.Client;
using RageCoop.Client.Scripting;
using RageCoopAdmin.Client;

internal static partial class Program
{
    private static readonly List<string> Natives = new List<string>();
    private static readonly Dictionary<int, RageCoop.Client.Player> EmptyPlayers = new Dictionary<int, RageCoop.Client.Player>();
    private static bool FailClockRelease;
    private static int PlayerReads;
    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.worldstateboundary");
        var sent = new List<Tuple<int, object[]>>();
        var controller = new WorldStateController((kind, args) => sent.Add(Tuple.Create(kind, args)));
        MethodInfo host = typeof(SyncedEntity).Assembly.GetType("RageCoop.Client.Scripting.BaseScript", true)!
            .GetMethod("WeatherTimeSync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            boundary.Patch(typeof(API).GetProperty("IsOnServer")!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(OnServer)));
            boundary.Patch(typeof(API).GetProperty("Players")!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(Players)));
            foreach (MethodInfo native in typeof(WorldStateNatives).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
                boundary.Patch(native, prefix: new HarmonyMethod(typeof(Program), nameof(Native)));
            controller.Start();
            int session = controller.SessionToken;
            Check(session > 0 && sent.Count == 1 && sent[0].Item1 == WorldStateProtocol.Request, "start advertises capability only after installing the real host handler guard");
            Check(Harmony.GetPatchInfo(host)!.Owners.Contains("friendslegacy.ragecoop.worldstate"), "actual installed RageCoop WeatherTimeSync has the world prefix");
            string epoch = new string('a', 32);
            var initial = new WorldStateSnapshot(epoch, 0, false, false, false, -1, 0, 0, -1);
            Check(!controller.Receive(WorldStateProtocol.Encode(initial, 0)), "an unsolicited initial broadcast cannot bind the resource generation");
            Check(!controller.Receive(WorldStateProtocol.Encode(initial, session == 1 ? 2 : 1)), "a targeted response for an old session cannot bind the new one");
            Check(controller.Receive(WorldStateProtocol.Encode(initial, session)) && controller.Snapshot!.Revision == 0, "matching fetch establishes confirmed identity without inventing world state");
            int first = controller.Request(WorldStateChange.FreezeTime, 14, 12, true);
            Check(first > 0 && controller.Request(WorldStateChange.GhostTraffic, 0, 0, true) == 0, "only one correlated change can be pending");
            var frozen = new WorldStateSnapshot(epoch, 1, true, false, false, 14, 12, 30, 6);
            controller.Receive(WorldStateProtocol.Encode(frozen, 0, change: WorldStateChange.FreezeTime));
            Check(controller.PendingRequestId == first && controller.Snapshot!.TimeFrozen, "another player's broadcast updates confirmed state without confirming this request");
            int acknowledgements = 0;
            controller.Changed += (_, id, _, _) => { if (id != 0) acknowledgements++; };
            controller.Receive(WorldStateProtocol.Encode(frozen, session, first, true, WorldStateChange.FreezeTime));
            Check(controller.PendingRequestId == 0 && acknowledgements == 1, "only the matching target acknowledgement resolves the request");
            controller.Receive(WorldStateProtocol.Encode(initial, session));
            Check(controller.Snapshot!.Revision == 1 && controller.Snapshot.TimeFrozen, "an older fetch cannot roll back a confirmed freeze");
            int second = controller.Request(WorldStateChange.FreezeWeather, 6, 0, true);
            var both = new WorldStateSnapshot(epoch, 3, true, true, false, 14, 12, 30, 6);
            controller.Receive(WorldStateProtocol.Encode(both, 0, change: WorldStateChange.FreezeWeather));
            controller.Receive(WorldStateProtocol.Encode(frozen, session, second, true, WorldStateChange.FreezeWeather));
            Check(controller.PendingRequestId == 0 && controller.Snapshot!.Revision == 3 && controller.Snapshot.WeatherFrozen,
                "an older targeted ack resolves pending UI without rolling back a later authoritative state");
            Natives.Clear();
            CallHost(host, 2, 3, 4);
            Check(Natives.Count == 0, "the actual host handler cannot overwrite either frozen component");
            var weatherOnly = new WorldStateSnapshot(epoch, 4, false, true, false, 14, 12, 30, 6);
            controller.Receive(WorldStateProtocol.Encode(weatherOnly, 0, change: WorldStateChange.FreezeTime));
            Natives.Clear();
            CallHost(host, 2, 3, 4);
            Check(Natives.SequenceEqual(new[] { "SetClock" }), "weather freeze preserves the host's natural clock section");
            var timeOnly = new WorldStateSnapshot(epoch, 5, true, false, false, 2, 3, 4, 6);
            controller.Receive(WorldStateProtocol.Encode(timeOnly, 0, change: WorldStateChange.FreezeWeather));
            Check(Natives.Contains("ClearWeather"), "weather thaw clears both persistent and override natives");
            Natives.Clear();
            CallHost(host, 2, 3, 4);
            Check(Natives.SequenceEqual(new[] { "ApplyNaturalWeather" }), "time freeze preserves the host's natural weather section");
            Natives.Clear();
            controller.Tick();
            Check(Natives.Contains("SetClock") && Natives.Contains("PauseClock"), "game tick enforces frozen time before host observation");
            int playerReads = PlayerReads;
            for (int frame = 0; frame < 100; frame++) controller.Tick();
            Check(PlayerReads == playerReads, "a non-host does not allocate a fresh player dictionary on every frame between one-second observations");
            int third = controller.Request(WorldStateChange.GhostTraffic, 0, 0, true);
            bool expired = false;
            controller.PendingExpired += () => expired = true;
            typeof(WorldStateController).GetField("pendingSince", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(controller, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 6);
            controller.Tick();
            Check(third > 0 && expired && controller.PendingRequestId == 0 && !controller.Snapshot!.GhostTrafficEnabled,
                "timeout clears pending UI and never invents a successful traffic toggle");
            Natives.Clear();
            controller.Stop();
            Check(controller.Snapshot == null && controller.SessionToken == 0 && Natives.Contains("PauseClock"), "stop releases owned clock state and discards old identity");
            Check(Harmony.GetPatchInfo(host)?.Owners.Contains("friendslegacy.ragecoop.worldstate") != true, "stop removes only this controller's real host-handler prefix");
            Check(!controller.Receive(WorldStateProtocol.Encode(frozen, session)), "a queued callback after stop cannot revive a freeze");
            controller.Start();
            int refreshedSession = controller.SessionToken;
            controller.Receive(WorldStateProtocol.Encode(both, refreshedSession));
            FailClockRelease = true;
            bool releaseFailure = false;
            Natives.Clear();
            try { controller.Stop(); }
            catch (InvalidOperationException) { releaseFailure = true; }
            finally { FailClockRelease = false; }
            Check(releaseFailure && controller.Snapshot == null && controller.SessionToken == 0 && Natives.Contains("ClearWeather"),
                "clock release failure still clears weather, identity and pending state");
            Check(Harmony.GetPatchInfo(host)?.Owners.Contains("friendslegacy.ragecoop.worldstate") != true,
                "native cleanup failure cannot leave the host handler patched");
            CheckMenu(boundary);
            Console.WriteLine("World state client checks passed; GTA native calls were isolated at the WorldStateNatives boundary.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            controller.Stop();
            foreach (MethodBase method in Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(boundary.Id) == true).ToArray())
                boundary.Unpatch(method, HarmonyPatchType.All, boundary.Id);
        }
    }

    private static void CallHost(MethodInfo method, int hour, int minute, int second)
    {
        var args = (CustomEventReceivedArgs)FormatterServices.GetUninitializedObject(typeof(CustomEventReceivedArgs));
        typeof(CustomEventReceivedArgs).GetProperty("Args")!.SetValue(args,
            new object[] { hour, minute, second, WorldStateProtocol.WeatherHash("CLEAR"), WorldStateProtocol.WeatherHash("RAIN"), 0.5f });
        method.Invoke(FormatterServices.GetUninitializedObject(method.DeclaringType!), new object[] { args });
    }
    private static bool OnServer(ref bool __result) { __result = true; return false; }
    private static bool Players(ref Dictionary<int, RageCoop.Client.Player> __result) { PlayerReads++; __result = EmptyPlayers; return false; }
    private static bool Native(MethodBase __originalMethod)
    {
        Natives.Add(__originalMethod.Name);
        if (FailClockRelease && __originalMethod.Name == "PauseClock") throw new InvalidOperationException("Isolated native release failure.");
        return false;
    }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
