using System;
using CoopAdmin.Server;
using CoopControl.Shared;

internal static class Program
{
    private static int Main()
    {
        var state = new WorldStateAuthority();
        WorldStateSnapshot initial = state.Snapshot(1000);
        Check(!initial.TimeFrozen && !initial.WeatherFrozen && !initial.GhostTrafficEnabled && initial.Hour == -1 && initial.WeatherIndex == -1,
            "all three modes default off without invented clock/weather");
        var restarted = new WorldStateAuthority();
        restarted.RestoreGhostTraffic(true);
        WorldStateSnapshot restored = restarted.Snapshot(1000);
        Check(restored.GhostTrafficEnabled && !restored.TimeFrozen && !restored.WeatherFrozen,
            "after a server restart the saved traffic pass-through is on again; frozen time and weather start fresh");
        state.ObserveHost(23, 59, 30, 6, 2000, 1000);
        Check(state.Snapshot(3000).Hour == 0 && state.Snapshot(3000).Minute == 0 && state.Snapshot(3000).Second == 30,
            "natural host clock projects across midnight at the actual host rate");
        Check(state.TryChange(state.Epoch, WorldStateChange.FreezeTime, 5, 1, true, 3000, out var frozen), "freeze request accepted");
        Check(frozen.Hour == 0 && frozen.Minute == 0 && frozen.Second == 30 && frozen.TimeFrozen,
            "freeze captures shared host clock rather than another player's different local fallback");
        state.ObserveHost(14, 0, 0, 8, 2000, 5000);
        Check(state.Snapshot(100000).Second == 30 && state.Snapshot(100000).WeatherIndex == 8,
            "host clock cannot overwrite frozen time while natural weather still changes");
        Check(state.TryChange(state.Epoch, WorldStateChange.FreezeWeather, -1, 0, true, 5000, out var both) && both.WeatherFrozen,
            "weather freezes independently using the latest actual host weather");
        state.ObserveHost(9, 0, 0, 2, 1000, 9000);
        Check(state.Snapshot(9000).WeatherIndex == 8, "later host weather cannot overwrite a freeze");
        Check(state.TryChange(state.Epoch, WorldStateChange.SetTime, 12, 15, false, 9000, out var changed) && changed.TimeFrozen && changed.Hour == 12,
            "manual time selection preserves the enabled freeze");
        Check(state.TryChange(state.Epoch, WorldStateChange.SetWeather, 3, 0, false, 9000, out changed) && changed.WeatherFrozen && changed.WeatherIndex == 3,
            "manual weather selection preserves the independent weather freeze");
        Check(state.TryChange(state.Epoch, WorldStateChange.GhostTraffic, 0, 0, true, 9000, out changed) && changed.GhostTrafficEnabled && changed.TimeFrozen && changed.WeatherFrozen,
            "traffic mode changes neither world freeze");
        Check(state.TryChange(state.Epoch, WorldStateChange.FreezeTime, 12, 15, false, 9000, out var thaw) && !thaw.TimeFrozen,
            "unfreezing leaves shared weather enabled");
        Check(state.Snapshot(11000).Minute == 16 && state.Snapshot(11000).WeatherFrozen, "clock really advances after thaw");
        long revision = state.Snapshot(11000).Revision;
        Check(!state.TryChange(new string('a', 32), WorldStateChange.GhostTraffic, 0, 0, false, 11000, out _) && state.Snapshot(11000).Revision == revision,
            "stale server epoch cannot mutate current state");
        Check(!state.TryChange(state.Epoch, WorldStateChange.SetTime, 24, 0, false, 11000, out _) &&
            !state.TryChange(state.Epoch, WorldStateChange.SetWeather, 15, 0, false, 11000, out _) &&
            !state.TryChange(state.Epoch, WorldStateChange.GhostTraffic, 1, 0, true, 11000, out _) &&
            !state.TryChange(state.Epoch, (WorldStateChange)999, 0, 0, false, 11000, out _), "strict command ranges are enforced");
        object[] wire = WorldStateProtocol.Encode(state.Snapshot(11000), 123, 5, true, WorldStateChange.FreezeTime);
        Check(WorldStateProtocol.TryRead(wire, out int session, out int requestId, out bool accepted, out _, out var decoded) &&
            session == 123 && requestId == 5 && accepted && decoded!.Revision == revision, "snapshot correlation and all confirmed flags round trip");
        wire[6] = 1;
        Check(!WorldStateProtocol.TryRead(wire, out _, out _, out _, out _, out _), "integer is never accepted in place of bool");
        wire[6] = false;
        wire[2] = (int)revision;
        Check(!WorldStateProtocol.TryRead(wire, out _, out _, out _, out _, out _), "wrong revision wire type is rejected");
        Check(!WorldStateProtocol.TryRead(null, out _, out _, out _, out _, out _), "missing snapshot is rejected");
        Check(!WorldStateProtocol.TryRead(new object[12], out _, out _, out _, out _, out _), "truncated snapshot is rejected");
        Check(WorldStateProtocol.WeatherHash("CLEAR") == 0x36A83D84 && WorldStateProtocol.WeatherIndexForHash(unchecked((int)0x97AA0A79)) == 0,
            "canonical GTA weather hashes match the checked engine host contract");
        Check(state.TryChange(state.Epoch, WorldStateChange.SetTime, 8, 0, false, 12000, out var current), "new time selection accepted");
        state.ObserveHost(1, 0, 0, 6, 2000, 12001, expectedRevision: current.Revision - 1);
        Check(state.Snapshot(12001).Hour == 8, "an in-flight host observation from an older control revision cannot undo a new choice");
        Check(state.ReleaseModes(12002, out var released) && !released.TimeFrozen && !released.WeatherFrozen && !released.GhostTrafficEnabled,
            "mixed-version safety releases every shared mode together");
        Check(!state.ReleaseModes(12003, out _), "unchanged safety state does not repeatedly broadcast a new revision");
        object[] broadcast = WorldStateProtocol.Encode(released, 0, change: WorldStateChange.FreezeWeather);
        Check(WorldStateProtocol.TryRead(broadcast, out _, out int uncorrelated, out _, out var reason, out _) &&
            uncorrelated == 0 && reason == WorldStateChange.FreezeWeather, "all-client command broadcasts retain the apply reason without borrowing a request token");
        state.Reset();
        Check(!state.Snapshot(12000).TimeFrozen && !state.GhostTrafficEnabled && state.Snapshot(12000).Hour == -1,
            "resource cleanup releases all modes and observed state");
        Console.WriteLine("World state checks passed");
        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}
