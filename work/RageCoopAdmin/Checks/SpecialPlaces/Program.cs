using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopControl.Shared;
using GTA.Math;
using HarmonyLib;
using RageCoopAdmin.Client;

// Hidden places: the real catalog, protocol and loading rules run here; only the
// game's map-part, island and minimap calls are replaced by a recorded fake.
internal static class Program
{
    private static readonly List<string> Calls = new List<string>();
    private static readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool OnlineMap;
    private static bool IslandWater;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.specialplaces");
        try
        {
            foreach (MethodInfo native in typeof(SpecialPlaceNatives).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(method => method.DeclaringType == typeof(SpecialPlaceNatives)))
            {
                string prefix = native.ReturnType == typeof(bool) ? nameof(BoolNative) : nameof(VoidNative);
                boundary.Patch(native, prefix: new HarmonyMethod(typeof(Program), prefix));
            }

            CheckProtocol();
            CheckCatalog();
            CheckAlwaysLoaded();
            CheckIsland();
            CheckYanktonConflicts();
            CheckReset();
            CheckSettle();
            Console.WriteLine("Special places checks passed: " + Passes + ". Catalog, protocol and loading rules are the real code; only game calls are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static void CheckProtocol()
    {
        Check(SpecialPlaceProtocol.TryReadInvite(new object[] { 3 }, out int place) && place == 3, "a known place number is a valid invitation");
        Check(!SpecialPlaceProtocol.TryReadInvite(new object[] { SpecialPlaceProtocol.PlaceCount }, out _) &&
            !SpecialPlaceProtocol.TryReadInvite(new object[] { -1 }, out _) &&
            !SpecialPlaceProtocol.TryReadInvite(new object[] { "3" }, out _) &&
            !SpecialPlaceProtocol.TryReadInvite(new object[] { 1, 2 }, out _) &&
            !SpecialPlaceProtocol.TryReadInvite(null, out _), "unknown numbers, other types and extra arguments are rejected");
        Check(SpecialPlaceProtocol.TryReadOffer(SpecialPlaceProtocol.EncodeOffer(0, "Rider"), out int offered, out string name) &&
            offered == 0 && name == "Rider", "an offer carries the place and the inviter's nickname unchanged");
        Check(SpecialPlaceProtocol.CleanName("~r~Pilot\n") == "rPilot" && SpecialPlaceProtocol.CleanName(null) == string.Empty &&
            SpecialPlaceProtocol.CleanName(new string('x', 100)).Length == SpecialPlaceProtocol.MaximumNameLength,
            "a nickname loses formatting codes and control characters and is kept short");
        Check(!SpecialPlaceProtocol.TryReadOffer(new object[] { 99, "Rider" }, out _, out _) &&
            !SpecialPlaceProtocol.TryReadOffer(new object[] { 1, 5 }, out _, out _), "an offer for an unknown place or without a name is ignored");
    }

    private static void CheckCatalog()
    {
        SpecialPlace[] all = SpecialPlaces.All;
        Check(all.Length == SpecialPlaceProtocol.PlaceCount && all.Select((item, index) => item.Id == index).All(ok => ok),
            "the client catalog matches the protocol's place numbers");
        Check(all.Select(item => item.Key).Distinct().Count() == all.Length && all.Select(item => item.Name).Distinct().Count() == all.Length,
            "every place has its own key and name");
        Check(all.All(item => item.Contains(new Vector3(item.X, item.Y, item.Z), 0f)), "each arrival point lies inside its own area");
        Check(all.All(item => item.Kind == SpecialPlaceKind.Island ? item.Ipls.Length == 0 : item.Ipls.Length > 0),
            "map-part places list their parts; the island uses the game's island switch");
        Check(all.Count(item => item.ExplicitOnly) == 1 && all[SpecialPlaces.NorthYankton].ExplicitOnly,
            "only North Yankton loads solely by choosing it");
        Check(all.Count(item => item.LoadsNearby) == 1 && all[SpecialPlaces.CayoPerico].LoadsNearby,
            "only the island loads by distance: its switch hides distant Los Santos");
        Check(all.All(item => item.ExplicitOnly ? item.Icon == 0 : item.Icon > 0),
            "every always-loaded place has a map icon; North Yankton, reached only by teleport, has none");
        var conflicts = new List<(int, int)>();
        for (int a = 0; a < all.Length; a++)
            for (int b = 0; b < all.Length; b++)
                if (SpecialPlaces.Conflict(a, b)) conflicts.Add((a, b));
        Check(conflicts.Count == 6 && conflicts.All(pair => SpecialPlaces.Conflict(pair.Item2, pair.Item1)) &&
            conflicts.All(pair => pair.Item1 == SpecialPlaces.NorthYankton || pair.Item2 == SpecialPlaces.NorthYankton) &&
            SpecialPlaces.Conflict(SpecialPlaces.NorthYankton, SpecialPlaces.YachtEast),
            "North Yankton conflicts symmetrically with exactly Cayo Perico, the eastern carrier and the yacht beside it");
        Check(all.Count(item => item.Key.StartsWith("yacht_", StringComparison.Ordinal)) == 8 &&
            all.Where(item => item.Key.StartsWith("yacht_", StringComparison.Ordinal)).All(item => item.Icon == 455 && item.OnFootOnly),
            "eight yachts, all with the yacht icon and reached on foot");
    }

    private static readonly Vector3 City = new Vector3(0f, 0f, 30f);

    private static SpecialPlaceRuntime Ready(Action<string>? log = null)
    {
        var runtime = new SpecialPlaceRuntime(SpecialPlaces.All, log);
        runtime.Tick(500, City);
        runtime.EnterOnlineMap();
        return runtime;
    }

    private static void CheckAlwaysLoaded()
    {
        Reset();
        var logs = new List<string>();
        var runtime = new SpecialPlaceRuntime(SpecialPlaces.All, logs.Add);
        runtime.Tick(1_000, City);
        Check(!runtime.IsActive(SpecialPlaces.CayoPerico) && runtime.IsActive(Find("red_carpet")) && runtime.NeedsOnlineMap &&
            !Calls.Contains("EnterOnlineMap") && SpecialPlaces.All.Where(place => place.Kind == SpecialPlaceKind.OnlineIpl).All(place => !runtime.IsActive(place.Id)),
            "story-mode places load at once (the island only near it); Online places wait for the switch the script hides behind its loading card");
        runtime.EnterOnlineMap();
        runtime.Tick(2_000, City);
        Check(SpecialPlaces.All.All(place => runtime.IsActive(place.Id) == AlwaysOn(place)) && !runtime.NeedsOnlineMap &&
            Calls.Count(call => call == "EnterOnlineMap") == 1 &&
            SpecialPlaces.All.Where(AlwaysOn).All(place => place.Ipls.All(Active.Contains)),
            "after the one switch every place except North Yankton and the island is on the map with all its parts");
        Check(!Calls.Any(call => call.StartsWith("SetIslandEnabled", StringComparison.Ordinal)) && !Calls.Contains("SetIslandRoads(True)") && !runtime.IslandShown,
            "in the city the island stays off, so distant Los Santos keeps its models");
        Calls.Clear();
        runtime.Tick(100_000, City);
        Check(Calls.Count == 0 && SpecialPlaces.All.All(place => runtime.IsActive(place.Id) == AlwaysOn(place)),
            "loaded places stay without further calls while nobody is near them");
        Check(logs.Any(line => line.Contains("switched to the Online map")) && logs.Any(line => line.Contains("yacht_bluffs loaded: 14/14 map parts (Online map).")),
            "the log records the switch and how many map parts the game reports");

        Reset();
        OnlineMap = true;
        runtime = new SpecialPlaceRuntime(SpecialPlaces.All);
        runtime.Tick(1_000, City);
        Check(!runtime.NeedsOnlineMap && !Calls.Contains("EnterOnlineMap") && SpecialPlaces.All.All(place => runtime.IsActive(place.Id) == AlwaysOn(place)),
            "when an earlier session already switched this GTA to the Online map, everything loads at once without a loading card");

        Reset();
        runtime = new SpecialPlaceRuntime(SpecialPlaces.All);
        runtime.Tick(1_000, City);
        runtime.Target(Find("yacht_paleto"), 1_500);
        Check(Calls.Count(call => call == "EnterOnlineMap") == 1 && runtime.IsActive(Find("yacht_paleto")) && !runtime.NeedsOnlineMap,
            "a teleport chosen before the card switches the map itself: the player leaves the reloading area");

        Reset();
        int carrier = Find("carrier_zancudo");
        foreach (string part in SpecialPlaces.All[carrier].Ipls) Missing.Add(part);
        runtime = Ready();
        runtime.Tick(1_000, City);
        Check(runtime.LoadedParts(carrier) == 0, "a place absent from this game version reports no map parts, so its teleport is refused");
    }

    private static void CheckIsland()
    {
        Reset();
        var runtime = Ready();
        SpecialPlace cayo = SpecialPlaces.All[SpecialPlaces.CayoPerico];
        SpecialPlace east = SpecialPlaces.All[SpecialPlaces.EastCarrier];
        Vector3 island = new Vector3(cayo.X, cayo.Y, cayo.Z);
        Vector3 deck = new Vector3(east.X, east.Y, east.Z);
        runtime.Tick(1_000, City);
        Check(!runtime.IsActive(SpecialPlaces.CayoPerico) && !Calls.Contains("SetIslandEnabled(True)"), "far from the island it is not loaded");
        runtime.Tick(2_000, deck);
        Check(runtime.IsActive(SpecialPlaces.CayoPerico) && Calls.Contains("SetIslandEnabled(True)"), "from the eastern carrier the island is near and loads");
        Check(!runtime.IslandShown && !Calls.Contains("SetIslandMap(True)") && !Calls.Contains("SetIslandRoads(True)"),
            "on the eastern carrier the usual map stays: the island map shows only over the island");
        runtime.Tick(3_000, island);
        Check(runtime.IslandShown && Calls.Contains("SetIslandWater(True)") && Calls.Contains("SetIslandMap(True)") && Calls.Contains("SetIslandRoads(True)"),
            "over the island its water, map and roads are on");
        Calls.Clear();
        runtime.Frame();
        Check(Calls.SequenceEqual(new[] { "DrawIslandRadarThisFrame()" }), "every frame over the island draws the island minimap");
        runtime.Tick(4_000, deck);
        Check(!runtime.IslandShown && Calls.Contains("SetIslandWater(False)") && Calls.Contains("SetIslandMap(False)") &&
            !Calls.Contains("SetIslandRoads(False)") && runtime.IslandRoads,
            "flying off the island restores water and map but keeps its roads near it");
        Calls.Clear();
        runtime.Frame();
        Check(Calls.Count == 0, "away from the island the minimap is left alone");
        runtime.Tick(4_500, City);
        Check(Calls.Contains("SetIslandRoads(False)") && !runtime.IslandRoads && runtime.IsActive(SpecialPlaces.CayoPerico),
            "far from the island the city roads return at once, the island waits a moment");
        runtime.Tick(5_000, City);
        Check(!runtime.IsActive(SpecialPlaces.CayoPerico) && Calls.Contains("SetIslandEnabled(False)"),
            "a second after leaving it unloads, so distant Los Santos comes back in time");

        Reset();
        runtime = Ready();
        float load = cayo.Radius + SpecialPlaceRuntime.IslandLoadMargin;
        float keep = cayo.Radius + SpecialPlaceRuntime.IslandKeepMargin;
        Vector3 Away(float distance) => new Vector3(cayo.CenterX - distance, cayo.CenterY, 300f);
        runtime.Tick(1_000, Away(load + 50f));
        Check(!runtime.IsActive(SpecialPlaces.CayoPerico), "just outside the loading distance the island is not loaded");
        runtime.Tick(2_000, Away(load - 50f));
        Check(runtime.IsActive(SpecialPlaces.CayoPerico), "flying in, it loads 600 m beyond its area");
        runtime.Tick(20_000, Away((load + keep) / 2));
        Check(runtime.IsActive(SpecialPlaces.CayoPerico), "flying back and forth at the edge does not reload it");
        runtime.Tick(21_000, Away(keep + 50f));
        runtime.Tick(22_000, Away(keep + 50f));
        Check(!runtime.IsActive(SpecialPlaces.CayoPerico), "beyond the wider distance it unloads after a second");
        Check(load == 2600f && keep == 2900f && SpecialPlaceRuntime.IslandLeaveMilliseconds == 1_000,
            "the island loads within 2.6 km of its area centre and unloads beyond 2.9 km");
        Check(Vector3.Distance2D(new Vector3(cayo.CenterX, cayo.CenterY, 0f), City) > keep + 2000f,
            "the city itself is far outside the island's distance");

        Reset();
        runtime = Ready();
        runtime.Tick(1_000, City);
        runtime.Target(SpecialPlaces.CayoPerico, 1_500);
        Check(runtime.IsActive(SpecialPlaces.CayoPerico) && Calls.Contains("SetIslandEnabled(True)"), "a teleport to the island loads it from anywhere");
    }

    private static bool AlwaysOn(SpecialPlace place) => !place.ExplicitOnly && !place.LoadsNearby;

    private static void CheckYanktonConflicts()
    {
        Reset();
        var runtime = Ready();
        SpecialPlace cayo = SpecialPlaces.All[SpecialPlaces.CayoPerico];
        SpecialPlace yankton = SpecialPlaces.All[SpecialPlaces.NorthYankton];
        Vector3 island = new Vector3(cayo.X, cayo.Y, cayo.Z);
        Vector3 town = new Vector3(yankton.X, yankton.Y, yankton.Z);
        runtime.Tick(1_000, island);
        Calls.Clear();
        runtime.Target(SpecialPlaces.NorthYankton, 2_000);
        Check(runtime.IsActive(SpecialPlaces.NorthYankton) && Calls.Contains("SetPrologueRoads(True)") && yankton.Ipls.All(Active.Contains),
            "choosing North Yankton requests all its map parts and roads");
        runtime.Tick(3_000, island);
        Check(runtime.IsActive(SpecialPlaces.CayoPerico) && runtime.IsActive(SpecialPlaces.EastCarrier),
            "the island and the carrier wait briefly while the teleport leaves");
        runtime.Tick(5_100, town);
        Check(!runtime.IsActive(SpecialPlaces.CayoPerico) && !runtime.IsActive(SpecialPlaces.EastCarrier) && !runtime.IsActive(SpecialPlaces.YachtEast) &&
            Calls.Contains("SetIslandEnabled(False)") && !Calls.Contains("SetIslandRoads(False)") && !runtime.IslandShown,
            "North Yankton replaces the island and the eastern carrier, without unloading island roads near it");
        Check(Calls.Contains("SetPrologueMap(True)"), "in North Yankton the game shows its own minimap");
        runtime.Tick(40_000, town);
        Check(runtime.IsActive(SpecialPlaces.NorthYankton) && !runtime.IsActive(SpecialPlaces.CayoPerico),
            "North Yankton stays while the player is there and the island stays away");

        Calls.Clear();
        runtime.Tick(41_000, City);
        Check(runtime.IsActive(SpecialPlaces.NorthYankton) && !runtime.IsActive(SpecialPlaces.CayoPerico) && runtime.IsActive(SpecialPlaces.EastCarrier),
            "after leaving North Yankton for the city the carrier returns at once, the island waits for someone to come near, Yankton waits");
        runtime.Tick(52_000, City);
        Check(!runtime.IsActive(SpecialPlaces.NorthYankton) && yankton.Ipls.All(part => !Active.Contains(part)) &&
            Calls.Contains("SetPrologueRoads(False)") && Calls.Contains("SetPrologueMap(False)") && Calls.Contains("SetIslandRoads(False)"),
            "ten seconds later North Yankton unloads with its roads and minimap; island roads are reset far away");

        Reset();
        runtime = Ready();
        runtime.Tick(1_000, City);
        runtime.Target(SpecialPlaces.NorthYankton, 2_000);
        runtime.Tick(6_000, town);
        runtime.Tick(40_000, town);
        runtime.Target(SpecialPlaces.CayoPerico, 41_000);
        runtime.Tick(42_000, town);
        runtime.Tick(44_100, island);
        Check(runtime.IsActive(SpecialPlaces.CayoPerico) && !runtime.IsActive(SpecialPlaces.NorthYankton) && runtime.IsActive(SpecialPlaces.EastCarrier),
            "choosing the island from North Yankton unloads Yankton and brings the carrier back");
    }

    private static void CheckReset()
    {
        Reset();
        var runtime = Ready();
        SpecialPlace cayo = SpecialPlaces.All[SpecialPlaces.CayoPerico];
        runtime.Tick(1_000, new Vector3(cayo.X, cayo.Y, cayo.Z));
        Check(runtime.IsInsideSeparatePlace(new Vector3(cayo.X, cayo.Y, cayo.Z)) && !runtime.IsInsideSeparatePlace(City),
            "the island counts as a place away from the usual streets");
        runtime.Reset(City);
        Check(SpecialPlaces.All.All(place => !runtime.IsActive(place.Id)) && Active.Count == 0 && !runtime.IslandShown &&
            Calls.Contains("SetIslandRoads(False)") && Calls.Contains("SetIslandWater(False)") && Calls.Contains("SetIslandEnabled(False)"),
            "a disconnect unloads every place and restores the island water, map and roads");
        Check(!runtime.HasState, "after a reset nothing is left to undo");
        Calls.Clear();
        runtime.Reset(null);
        Check(Calls.Count == 0, "a repeated reset changes nothing");
        Check(!new SpecialPlaceRuntime(SpecialPlaces.All).HasState, "a fresh session has nothing to undo, so stopping does not touch the game");
    }

    private static void CheckSettle()
    {
        Reset();
        IslandWater = true;
        var runtime = new SpecialPlaceRuntime(SpecialPlaces.All);
        runtime.Tick(1_000, City);
        Check(Calls.Contains("SetIslandRoads(False)") && Calls.Contains("SetIslandWater(False)") && Calls.Contains("SetIslandMap(False)"),
            "a session starting in the city resets island roads, water and map a previous session may have left");
        Calls.Clear();
        runtime.Tick(2_000, City);
        Check(!Calls.Any(call => call.StartsWith("SetIsland", StringComparison.Ordinal)), "the reset happens once per session");

        Reset();
        var plain = new SpecialPlaceRuntime(SpecialPlaces.All);
        plain.Tick(1_000, City);
        Check(!Calls.Contains("SetIslandWater(False)"), "the usual water file is not reloaded needlessly");

        Reset();
        var near = new SpecialPlaceRuntime(SpecialPlaces.All);
        near.Tick(1_000, new Vector3(SpecialPlaces.IslandCenterX, SpecialPlaces.IslandCenterY + 2000f, 5f));
        Check(!Calls.Contains("SetIslandRoads(False)") && near.IslandRoads, "starting next to the island never unloads its roads there");
        near.Tick(2_000, City);
        Check(Calls.Contains("SetIslandRoads(False)"), "leaving the island area afterwards resets its roads");
    }

    private static int Find(string key) => SpecialPlaces.All.Single(place => place.Key == key).Id;

    private static void Reset()
    {
        Calls.Clear();
        Active.Clear();
        Missing.Clear();
        OnlineMap = false;
        IslandWater = false;
    }

    private static bool OnlineOnly(string part) =>
        part.StartsWith("hei_", StringComparison.OrdinalIgnoreCase) || part.StartsWith("gr_", StringComparison.OrdinalIgnoreCase) ||
        part.StartsWith("m23_", StringComparison.OrdinalIgnoreCase) || part.StartsWith("m24_", StringComparison.OrdinalIgnoreCase);

    private static bool VoidNative(MethodBase __originalMethod, object[] __args)
    {
        string name = __originalMethod.Name;
        Calls.Add(__args.Length == 0 ? name : name + "(" + string.Join(", ", __args) + ")");
        switch (name)
        {
            case "RequestIpl":
                string part = (string)__args[0];
                if (!Missing.Contains(part) && (OnlineMap || !OnlineOnly(part))) Active.Add(part);
                break;
            case "RemoveIpl":
                Active.Remove((string)__args[0]);
                break;
            case "EnterOnlineMap":
                Calls[Calls.Count - 1] = name;
                OnlineMap = true;
                break;
            case "SetIslandWater":
                IslandWater = (bool)__args[0];
                break;
            case "DrawIslandRadarThisFrame":
                Calls[Calls.Count - 1] = name + "()";
                break;
        }
        return false;
    }

    private static bool BoolNative(MethodBase __originalMethod, object[] __args, ref bool __result)
    {
        if (__originalMethod.Name == "IsIslandWater")
        {
            Calls.Add("IsIslandWater()");
            __result = IslandWater;
            return false;
        }

        Calls.Add(__originalMethod.Name + "(" + __args[0] + ")");
        __result = Active.Contains((string)__args[0]);
        return false;
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
