using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RageCoopAdmin.Client;

// Online radio stations: the real switch runs here; only GTA's radio calls are
// replaced by recorded fakes.
internal static class Program
{
    private static readonly List<string> Shown = new List<string>();
    private static int Unlocked = 20;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.onlineradio");
        try
        {
            foreach (string name in new[] { "Show", "UnlockedCount" })
                boundary.Patch(typeof(OnlineRadioNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            var log = new List<string>();
            var radio = new OnlineRadio(log.Add);
            radio.Tick();
            Check(Shown.SequenceEqual(OnlineRadio.Stations) && OnlineRadio.Stations.Length == 7,
                "on the server the seven Online stations are put on the radio wheel");
            Check(!OnlineRadio.Stations.Contains("RADIO_36_AUDIOPLAYER") && OnlineRadio.Stations.All(name => name.StartsWith("RADIO_", StringComparison.Ordinal)),
                "Media Player, which has no music in story mode, stays hidden");
            Check(log.Single() == "OG Coop: Online radio stations shown: 20 -> 27", "the log tells how many stations the wheel has before and after");
            Shown.Clear();
            radio.Tick();
            Check(Shown.Count == 0, "they are shown once per connection, not every frame");
            radio.Reset();
            radio.Tick();
            Check(Shown.Count == OnlineRadio.Stations.Length, "after reconnecting they are shown again");
            Console.WriteLine("Online radio checks passed: " + Passes + ". The real switch runs; GTA's radio is isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static bool Show(string station)
    {
        Shown.Add(station);
        Unlocked++;
        return false;
    }

    private static bool UnlockedCount(ref int __result)
    {
        __result = Unlocked;
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
