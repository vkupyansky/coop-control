using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GTA;
using HarmonyLib;
using RageCoopAdmin.Client;

// Story scripts: the real list and stopper run here; only GTA's script-thread
// calls are replaced by a recorded fake set of running scripts.
internal static class Program
{
    private static readonly HashSet<int> Running = new HashSet<int>();
    private static readonly List<string> Stopped = new List<string>();
    private static int Probes;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.storyscripts");
        try
        {
            boundary.Patch(typeof(StoryScriptNatives).GetMethod(nameof(StoryScriptNatives.IsRunning), BindingFlags.Static | BindingFlags.NonPublic)!,
                prefix: new HarmonyMethod(typeof(Program), nameof(IsRunning)));
            boundary.Patch(typeof(StoryScriptNatives).GetMethod(nameof(StoryScriptNatives.Stop), BindingFlags.Static | BindingFlags.NonPublic)!,
                prefix: new HarmonyMethod(typeof(Program), nameof(Stop)));
            foreach (string name in new[] { "First", "Next", "Exists", "IsShown", "Hide" })
                boundary.Patch(typeof(StoryBlipNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), "Blip" + name));
            CheckList();
            CheckStopper();
            CheckBlips();
            Console.WriteLine("Story script checks passed: " + Passes + ". The chosen list and stopper are the real code; only GTA's script threads and map icons are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static void CheckList()
    {
        string[] list = StoryScripts.Stopped;
        Check(list.Length == 351 && list.Distinct(StringComparer.Ordinal).Count() == list.Length && list.All(name => name == name.ToLowerInvariant()),
            "the chosen list holds 351 distinct lower-case script names");
        string[] kept =
        {
            "main", "main_persistent", "startup", "initial", "standard_global_init", "standard_global_reg", "respawn_controller",
            "player_controller", "building_controller", "vehicle_gen_controller", "pickup_controller", "cellphone_controller",
            "shop_controller", "carmod_shop", "gunclub_shop", "clothes_shop_sp", "hairdo_shop_sp", "tattoo_shop", "savegame_bed",
            "selector", "stats_controller", "social_controller", "ingamehud", "taxilauncher", "taxiservice", "appcamera", "appcontacts",
            "blip_controller",
        };
        Check(!kept.Any(list.Contains),
            "scripts that run the game, shops, phone, Los Santos Customs, respawn and map icons (shops' icons with them) are never stopped");
        Check(new[] { "flow_controller", "mission_triggerer_a", "mission_triggerer_b", "mission_triggerer_c", "mission_triggerer_d",
                "comms_controller", "randomchar_controller", "restrictedareas", "cheat_controller" }.All(list.Contains) &&
            list.Count(name => name.StartsWith("re_", StringComparison.Ordinal)) == 38,
            "story flow, mission icons, calls, all 38 random events and the player's extra choices are stopped");
    }

    private static void CheckStopper()
    {
        var logs = new List<string>();
        var stopper = new StoryScriptStopper(StoryScripts.Stopped, logs.Add);
        Running.Add(Hash("flow_controller"));
        Running.Add(Hash("re_accident"));
        Running.Add(Hash("main"));
        int frames = (StoryScripts.Stopped.Length + StoryScriptStopper.PerFrame - 1) / StoryScriptStopper.PerFrame;
        Probes = 0;
        stopper.Frame();
        Check(Probes == StoryScriptStopper.PerFrame, "each frame checks at most " + StoryScriptStopper.PerFrame + " scripts");
        for (int i = 1; i < frames; i++) stopper.Frame();
        Check(Stopped.OrderBy(name => name).SequenceEqual(new[] { "flow_controller", "re_accident" }) && Running.Contains(Hash("main")),
            "one pass over all frames stops exactly the chosen scripts that run and leaves the rest");
        Check(logs.Count == 2 && logs.All(line => line.StartsWith("Coop Control: stopped story script ", StringComparison.Ordinal)),
            "each stopped script is written to the log");
        Running.Add(Hash("re_accident"));
        for (int i = 0; i < frames; i++) stopper.Frame();
        Check(Stopped.Count(name => name == "re_accident") == 2 && logs.Count == 2 && stopper.StoppedCount == 2,
            "a random event the world starts again is stopped again without repeating the log line");
        Probes = 0;
        for (int i = 0; i < frames; i++) stopper.Frame();
        Check(Probes == frames * StoryScriptStopper.PerFrame && Stopped.Count == 3, "with nothing running a pass only checks");
    }

    // Fake map: blip handle -> (sprite, shown). GTA walks one sprite's icons with First/Next.
    private static readonly Dictionary<int, (int Sprite, bool Shown)> Blips = new Dictionary<int, (int, bool)>();
    private static readonly List<int> Walk = new List<int>();
    private static int Hidden;

    private static void CheckBlips()
    {
        var logs = new List<string>();
        var hider = new StoryBlipHider(logs.Add);
        Blips[11] = (78, true); // Michael's mission letter
        Blips[12] = (66, true); // a stranger's question mark
        Blips[13] = (66, false); // already hidden
        Blips[20] = (52, true); // a shop
        Blips[21] = (72, true); // Los Santos Customs
        Blips[30] = (798, true); // our island icon
        Blips[31] = (889, true); // our carrier icon
        Blips[40] = (1, true); // a RageCoop player
        hider.Tick(1_000);
        Check(!Blips[11].Shown && !Blips[12].Shown && Hidden == 2, "story characters' icons are hidden");
        Check(new[] { 20, 21, 30, 31, 40 }.All(blip => Blips[blip].Shown), "shops, Los Santos Customs, our places and players keep their icons");
        Check(logs.SequenceEqual(new[] { "Coop Control: hid 2 story map icons." }), "the first hiding is written to the log");
        Blips[11] = (78, true);
        hider.Tick(1_500);
        Check(Blips[11].Shown, "icons are checked once a second, not every frame");
        hider.Tick(2_000);
        Check(!Blips[11].Shown && Hidden == 3 && logs.Count == 1, "an icon shown again is hidden on the next check without a new log line");
        Check(new[] { 52, 72, 798, 890, 455, 911, 903, 135, 1, 40 }.All(sprite => !StoryBlipHider.Sprites.Contains(sprite)) &&
            StoryBlipHider.Sprites.Distinct().Count() == StoryBlipHider.Sprites.Length,
            "the hidden sprites never include shops, Los Santos Customs, our places' icons or players");
        hider.Reset();
        Blips[12] = (66, true);
        hider.Tick(2_100);
        Check(!Blips[12].Shown && logs.Count == 2, "a new session checks at once and logs again");
    }

    private static bool BlipFirst(int sprite, ref int __result)
    {
        Walk.Clear();
        Walk.AddRange(Blips.Where(pair => pair.Value.Sprite == sprite).Select(pair => pair.Key).OrderBy(handle => handle));
        return BlipNext(sprite, ref __result);
    }

    private static bool BlipNext(int sprite, ref int __result)
    {
        __result = 0;
        if (Walk.Count > 0)
        {
            __result = Walk[0];
            Walk.RemoveAt(0);
        }
        return false;
    }

    private static bool BlipExists(int blip, ref bool __result)
    {
        __result = Blips.ContainsKey(blip);
        return false;
    }

    private static bool BlipIsShown(int blip, ref bool __result)
    {
        __result = Blips[blip].Shown;
        return false;
    }

    private static bool BlipHide(int blip)
    {
        Blips[blip] = (Blips[blip].Sprite, false);
        Hidden++;
        return false;
    }

    private static int Hash(string name) => unchecked((int)StringHash.AtStringHash(name));

    private static bool IsRunning(int hash, ref bool __result)
    {
        Probes++;
        __result = Running.Contains(hash);
        return false;
    }

    private static bool Stop(string name)
    {
        Stopped.Add(name);
        Running.Remove(Hash(name));
        return false;
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
