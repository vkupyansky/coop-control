using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GTA;
using HarmonyLib;
using RageCoopAdmin.Client;

// Story scripts: the real list and stopper run here, only GTA's script-thread
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
            foreach (string name in new[] { nameof(StoryScriptNatives.IsHelpShown), nameof(StoryScriptNatives.ClearHelp) })
                boundary.Patch(typeof(StoryScriptNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            foreach (string name in new[] { "First", "Next", "Exists", "IsShown", "Hide" })
                boundary.Patch(typeof(StoryBlipNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), "Blip" + name));
            foreach (string name in new[] { "IsRegistered", "State", "Unlock" })
                boundary.Patch(typeof(SafehouseDoorNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), "Door" + name));
            foreach (string name in new[] { "ReadModel", "WriteModel" })
                boundary.Patch(typeof(HeroLinkNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), "Hero" + name));
            CheckList();
            CheckStopper();
            CheckBlips();
            CheckSafehouseDoors();
            CheckHeroLink();
            Console.WriteLine("Story script checks passed: " + Passes + ". The chosen list and stopper are the real code; only GTA's script threads and map icons are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static void CheckList()
    {
        string[] list = StoryScripts.Stopped;
        Check(list.Length == 357 && list.Distinct(StringComparer.Ordinal).Count() == list.Length && list.All(name => name == name.ToLowerInvariant()),
            "the chosen list holds 357 distinct lower-case script names");
        Check(list.Contains("forsalesigns") && list.Contains("autosave_controller"),
            "Josh's For Sale signs are stopped with the other collectibles: a destroyed sign no longer counts or asks to save");
        Check(new[] { "clothes_shop_sp", "hairdo_shop_sp", "tattoo_shop", "wardrobe_sp" }.All(list.Contains) &&
            new[] { 71, 73, 75 }.All(StoryBlipHider.Sprites.Contains),
            "clothes shops, barbers, tattoo parlours and the safehouse wardrobe are closed and their map icons hidden");
        Check(list.Contains("selector") &&
            HeroSwitch.Keys.Select(action => (int)action).OrderBy(code => code).SequenceEqual(new[] { 19, 166, 167, 168, 169 }),
            "hero switching is off: the wheel's script is stopped and the wheel and the four hero keys are blocked");
        string[] kept =
        {
            "main", "main_persistent", "startup", "initial", "standard_global_init", "standard_global_reg", "respawn_controller",
            "player_controller", "building_controller", "vehicle_gen_controller", "pickup_controller", "cellphone_controller",
            "shop_controller", "carmod_shop", "gunclub_shop", "savegame_bed",
            "stats_controller", "social_controller", "ingamehud", "taxilauncher", "taxiservice", "appcamera", "appcontacts",
            "blip_controller",
        };
        Check(!kept.Any(list.Contains),
            "scripts that run the game, other shops, phone, Los Santos Customs, respawn and map icons are never stopped");
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
        Check(logs.Count == 2 && logs.All(line => line.StartsWith("OG Coop: stopped story script ", StringComparison.Ordinal)),
            "each stopped script is written to the log");
        Running.Add(Hash("re_accident"));
        for (int i = 0; i < frames; i++) stopper.Frame();
        Check(Stopped.Count(name => name == "re_accident") == 2 && logs.Count == 2 && stopper.StoppedCount == 2,
            "a random event the world starts again is stopped again without repeating the log line");
        Probes = 0;
        for (int i = 0; i < frames; i++) stopper.Frame();
        Check(Probes == frames * StoryScriptStopper.PerFrame && Stopped.Count == 3 && HelpCleared == 0, "with nothing running a pass only checks");
        ShownHelp = "SOME_OTHER_HELP";
        stopper.Frame();
        Check(HelpCleared == 0, "other help messages stay");
        ShownHelp = StoryScriptStopper.SaveOffHelp;
        stopper.Frame();
        Check(HelpCleared == 1 && ShownHelp == null, "the stuck \"autosave is off\" help is taken down");
    }

    private static string? ShownHelp;
    private static int HelpCleared;

    private static bool IsHelpShown(string label, ref bool __result)
    {
        __result = ShownHelp == label;
        return false;
    }

    private static bool ClearHelp()
    {
        HelpCleared++;
        ShownHelp = null;
        return false;
    }

    // Fake character table: the models of Michael, Franklin and Trevor.
    private static readonly int[] HeroTable = (int[])HeroLink.Heroes.Clone();
    private static int HeroWrites;
    private static bool HeroTableMissing;

    private static void CheckHeroLink()
    {
        const int male = 0x705E61F2, female = unchecked((int)0x9C9EFFD8), franklin = -1692214353, michael = 225514697;
        var logs = new List<string>();
        var link = new HeroLink(logs.Add);
        link.Sync(franklin, franklin);
        Check(HeroWrites == 0 && logs.Count == 0 && link.LinkedHero() == -1, "a story hero leaves the game's hero table untouched");
        link.Sync(franklin, male);
        Check(HeroTable[1] == male && HeroTable[0] == michael && HeroTable[2] == HeroLink.Heroes[2] && link.LinkedHero() == 1 &&
            logs.Last() == "OG Coop: the Online character counts as story hero 1 for the game.",
            "an Online body takes Franklin's entry, so the game treats the character as Franklin");
        int writes = HeroWrites;
        link.Sync(franklin, male);
        Check(HeroWrites == writes && logs.Count == 1, "a linked table is not written again and the link is logged once");
        link.Sync(franklin, female);
        Check(HeroTable[1] == female && logs.Count == 1, "changing to a woman's body keeps the link to the same hero");
        Check(new HeroLink().LinkedHero() == 1, "after a resource restart the table still tells which hero the character came from");
        new HeroLink().Sync(0, female);
        Check(HeroTable[1] == female && HeroTable[0] == michael, "with the hero unknown, the hero already linked keeps the body");
        link.Sync(franklin, franklin);
        Check(HeroTable.SequenceEqual(HeroLink.Heroes) && logs.Last() == "OG Coop: story hero table restored.",
            "becoming the story hero again restores the table");
        link.Sync(michael, male);
        Check(HeroTable[0] == male && HeroTable[1] == franklin, "a player who came from Michael is linked to Michael");
        link.Sync(michael, michael);

        HeroTable[2] = 12345;
        var guarded = new HeroLink(logs.Add);
        writes = HeroWrites;
        guarded.Sync(franklin, male);
        guarded.Sync(franklin, male);
        Check(HeroWrites == writes && logs.Count(line => line.Contains("not the supported one")) == 1 && guarded.LinkedHero() == -1,
            "on another game build, where the table does not hold the three heroes, nothing is written and this is logged once");
        HeroTable[2] = HeroLink.Heroes[2];
        HeroTableMissing = true;
        var missing = new HeroLink(logs.Add);
        missing.Sync(franklin, male);
        Check(HeroWrites == writes && logs.Count(line => line.Contains("not the supported one")) == 2,
            "a game without the table is reported and left alone");
        HeroTableMissing = false;
    }

    private static bool HeroReadModel(int hero, ref int __result)
    {
        if (HeroTableMissing) throw new IndexOutOfRangeException("no such global");
        __result = HeroTable[hero];
        return false;
    }

    private static bool HeroWriteModel(int hero, int model)
    {
        HeroTable[hero] = model;
        HeroWrites++;
        return false;
    }

    // Fake door system: door name -> state (0 unlocked, 1 locked); absent = not registered.
    private static readonly Dictionary<int, int> Doors = new Dictionary<int, int>();
    private static int Unlocks;

    private static void CheckSafehouseDoors()
    {
        var logs = new List<string>();
        var doors = new SafehouseDoors(logs.Add);
        const int franklinHills = 1201219326, michaelFront = -2097039789, michaelWindow = -1700375831, shopDoor = 1804701345;
        Doors[franklinHills] = 1;
        Doors[michaelFront] = 0;
        Doors[michaelWindow] = 1;
        Doors[shopDoor] = 1;
        doors.Tick(1_000);
        Check(Doors[franklinHills] == 0 && Unlocks == 1 && logs.SequenceEqual(new[] { "OG Coop: story safehouse doors unlocked for every player." }),
            "a safehouse door the game locked for a non-owner is unlocked, once, with one log line");
        Check(Doors[michaelWindow] == 1 && Doors[shopDoor] == 1, "Michael's bathroom window and other doors of the world are left alone");
        Doors[franklinHills] = 1;
        doors.Tick(1_500);
        Check(Doors[franklinHills] == 1, "doors are checked once a second, not every frame");
        doors.Tick(2_000);
        Check(Doors[franklinHills] == 0 && Unlocks == 2 && logs.Count == 1, "a door locked again is unlocked on the next check without a new log line");
        Check(SafehouseDoors.Doors.Distinct().Count() == 15 && !SafehouseDoors.Doors.Contains(michaelWindow),
            "Michael's, Franklin's and Trevor's 15 entrance doors and gates are covered");
        Doors.Remove(franklinHills);
        doors.Tick(3_000);
        Check(Unlocks == 2, "a door the game has not registered yet is not touched");
    }

    private static bool DoorIsRegistered(int door, ref bool __result)
    {
        __result = Doors.ContainsKey(door);
        return false;
    }

    private static bool DoorState(int door, ref int __result)
    {
        __result = Doors.TryGetValue(door, out int state) ? state : 0;
        return false;
    }

    private static bool DoorUnlock(int door)
    {
        Doors[door] = 0;
        Unlocks++;
        return false;
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
        Check(logs.SequenceEqual(new[] { "OG Coop: hid 2 story map icons." }), "the first hiding is written to the log");
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
