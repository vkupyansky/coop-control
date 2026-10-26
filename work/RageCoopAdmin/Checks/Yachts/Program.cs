using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GTA.Math;
using HarmonyLib;
using OGCoop.Shared;
using RageCoopAdmin.Client;

// Yachts on the map: the real berths, looks and building order run here; only
// GTA's map-part, object and ped calls are replaced by a recorded fake world.
internal static class Program
{
    private static readonly HashSet<string> Ipls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, (string Model, Vector3 At)> Objects = new Dictionary<int, (string, Vector3)>();
    private static readonly List<string> Calls = new List<string>();
    private static readonly HashSet<int> Captains = new HashSet<int>();
    private static int _hull;
    private static bool _modelsLoaded;
    private static bool _animationsLoaded;
    private static int _next = 1000;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.yachts");
        try
        {
            foreach (MethodInfo native in typeof(YachtNatives).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(method => method.DeclaringType == typeof(YachtNatives) && method.Name != "Delete" && method.Name != "Joaat"))
            {
                boundary.Patch(native, prefix: new HarmonyMethod(typeof(Program), native.Name));
            }
            CheckBerths();
            CheckBuilding();
            CheckChanges();
            Console.WriteLine("Yacht checks passed: " + Passes + ". Berths, looks and building order are the real code; GTA calls are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static void CheckBerths()
    {
        // HKH191 found the hull of berth 34 (Paleto Bay 2) here and of berth 2 here.
        Vector3 paleto = YachtRuntime.HullPosition(Yachts.Berths[34]), chumash = YachtRuntime.HullPosition(Yachts.Berths[2]);
        Check(new Vector2(paleto.X + 777.4865f, paleto.Y - 6566.907f).Length() < 0.5f && new Vector2(chumash.X + 3280.501f, chumash.Y - 2140.507f).Length() < 0.5f,
            "the hull is found where it stands, from the yacht script's berth point and heading");
    }

    private static void CheckBuilding()
    {
        Reset();
        var sent = new List<object[]>();
        var looks = new YachtLooks((_, args) => sent.Add(args));
        var runtime = new YachtRuntime();
        var look = new YachtLook { Berth = 33, Model = 2, Color = 7, GoldRails = true, Lighting = 6, Flag = 3 };
        looks.ApplyState(new object[] { YachtCodec.Encode(look) });
        runtime.Tick(looks);
        Check(Ipls.SetEquals(new[] { "apa_yacht_grp12_1", "apa_yacht_grp12_1_int" }) && Objects.Count == 0,
            "the berth's hull and interior go on the map; nothing is built before the hull streams in");
        Check(runtime.Arrival(string.Empty) is Vector3 point && point.X == Yachts.Berths[33].X && !runtime.HullIn(string.Empty),
            "far away a player is sent to the berth's point");
        _hull = 77;
        runtime.Tick(looks);
        Check(Objects.Count == 0 && !Calls.Contains("Tint(77, 7)"), "the objects wait until their models are in memory");
        _modelsLoaded = true;
        runtime.Tick(looks);
        string[] models = Objects.Values.Select(item => item.Model).ToArray();
        string[] parts = Yachts.Parts(look).Select(part => part.Model).ToArray();
        Check(parts.All(models.Contains) && models.Count(model => model == Yachts.Flags[2].Model) == 3 && Captains.Count == 1 &&
            Calls.Contains("Tint(77, 7)") && Calls.Contains($"Attach({Objects.First(item => item.Value.Model == "apa_mp_apa_yacht_option3").Key}, 77, 0, 0, 14.5, 0)"),
            "the Aquaris decks, gold rails, lights, door and three flags go on the hull, tinted in its colour, with the captain on the bridge");
        Check(Calls.Any(call => call.StartsWith($"Tint({Objects.First(item => item.Value.Model == "apa_mp_apa_yacht_o3_rail_b").Key}, 0)", StringComparison.Ordinal)) &&
            Calls.Contains($"Attach({Objects.First(item => item.Value.Model == "apa_mp_apa_yacht_door").Key}, 77, -36.8, -2.7, 0.6, 90)"),
            "the rails keep their own colour and the door turns across the deck");
        Check(runtime.HullIn(string.Empty) && runtime.Arrival(string.Empty) == new Vector3(-30f, -2f, 1f), "with the hull in, a player arrives on its aft deck");
        int captain = Captains.Single();
        Check(LocalPeds.Without(new[] { NativePed(captain), NativePed(5) }).Select(ped => ped.Handle).SequenceEqual(new[] { 5 }),
            "every game has its own captain: RageCoop does not send him to the other players");
        Check(Calls.Contains("PlayCaptainIdle") && !Calls.Contains("PlayCaptainIdle:played"), "the captain waits for his animation");
        _animationsLoaded = true;
        runtime.Tick(looks);
        runtime.Tick(looks);
        Check(Calls.Count(call => call == "PlayCaptainIdle") == 2 && Calls.Count(call => call == "PlayCaptainIdle:played") == 1,
            "then he stands at the controls, started once");
        _hull = 0;
        runtime.Tick(looks);
        Check(Objects.Count == 0 && Captains.Count == 0 && Ipls.Count == 2, "when the hull streams out, everything on it goes; the map parts stay");
        Check(LocalPeds.Without(new[] { NativePed(captain) }).Length == 1, "a captain taken off no longer hides his handle from RageCoop");
        _hull = 78;
        runtime.Tick(looks);
        Check(Objects.Count == Yachts.Parts(look).Length + 3 && Captains.Count == 1, "and comes back with it");
        runtime.Reset();
        Check(Objects.Count == 0 && Captains.Count == 0 && Ipls.Count == 0 && !runtime.HasState, "leaving the server takes the yachts off the map");
    }

    private static void CheckChanges()
    {
        Reset();
        _hull = 77;
        _modelsLoaded = true;
        var looks = new YachtLooks((_, _) => { });
        var runtime = new YachtRuntime();
        var moves = new List<(string, int, int)>();
        runtime.Moved += (owner, from, to) => moves.Add((owner, from, to));
        var look = new YachtLook { Berth = 10, Model = 0 };
        looks.ApplyState(new object[] { YachtCodec.Encode(look) });
        looks.ApplyLook(new object[] { "Pilot", YachtCodec.Encode(new YachtLook { Berth = 20, Model = 1, Flag = 0 }) });
        runtime.Tick(looks);
        Check(Ipls.SetEquals(new[] { Yachts.Berths[10].Hull, Yachts.Berths[10].Interior, Yachts.Berths[20].Hull, Yachts.Berths[20].Interior }),
            "this player's and the partner's yachts are both on the map");
        int built = Objects.Count;
        var recolored = YachtCodec.Copy(look);
        recolored.Color = 3;
        looks.Saved(recolored);
        Calls.Clear();
        runtime.Tick(looks);
        Check(!Calls.Any(call => call.StartsWith("RemoveIpl", StringComparison.Ordinal)) && Calls.Contains("Tint(77, 3)") && Objects.Count == built && moves.Count == 0,
            "a new colour rebuilds the objects on the same hull without reloading the map");
        var moved = YachtCodec.Copy(recolored);
        moved.Berth = 30;
        looks.Saved(moved);
        runtime.Tick(looks);
        Check(!Ipls.Contains(Yachts.Berths[10].Hull) && Ipls.Contains(Yachts.Berths[30].Hull) && moves.SequenceEqual(new[] { (string.Empty, 10, 30) }),
            "the captain moves the yacht: the old berth empties, the new one fills, and whoever was aboard is told");
        looks.ApplyLook(new object[] { "pilot", string.Empty });
        runtime.Tick(looks);
        Check(!Ipls.Contains(Yachts.Berths[20].Hull) && Ipls.Contains(Yachts.Berths[30].Hull), "a partner who left takes the yacht along");
        runtime.Reset();
    }

    private static void Reset()
    {
        Ipls.Clear(); Objects.Clear(); Calls.Clear(); Captains.Clear();
        _hull = 0; _modelsLoaded = false; _animationsLoaded = false;
    }

    private static string F(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool RequestIpl(string name) { Ipls.Add(name); Calls.Add("RequestIpl(" + name + ")"); return false; }
    private static bool RemoveIpl(string name) { Ipls.Remove(name); Calls.Add("RemoveIpl(" + name + ")"); return false; }
    private static bool FindObject(ref int __result) { __result = _hull; return false; }
    private static bool Exists(int entity, ref bool __result) { __result = entity != 0 && entity == _hull; return false; }
    // The fake hull stands at the origin facing north.
    private static bool Offset(Vector3 offset, ref Vector3 __result) { __result = offset; return false; }
    private static bool Heading(ref float __result) { __result = 0f; return false; }
    private static bool LoadModel(ref bool __result) { __result = _modelsLoaded; return false; }
    private static bool ReleaseModel() => false;
    private static bool CreateObject(string model, Vector3 at, ref int __result) { __result = ++_next; Objects[__result] = (model, at); return false; }
    private static bool Attach(int item, int hull, Vector3 offset, float turn)
    {
        Calls.Add($"Attach({item}, {hull}, {F(offset.X)}, {F(offset.Y)}, {F(offset.Z)}, {F(turn)})");
        return false;
    }
    private static bool Tint(int item, int tint) { Calls.Add($"Tint({item}, {tint})"); return false; }
    private static bool Lean() => false;
    private static bool DeleteObject(int item) { Objects.Remove(item); return false; }
    private static bool CreateCaptain(ref int __result) { __result = ++_next; Captains.Add(__result); return false; }
    private static bool PlayCaptainIdle(ref bool __result)
    {
        Calls.Add("PlayCaptainIdle");
        __result = _animationsLoaded;
        if (__result) Calls.Add("PlayCaptainIdle:played");
        return false;
    }
    private static bool DeletePed(int ped) { Captains.Remove(ped); return false; }

    private static GTA.Ped NativePed(int handle) => (GTA.Ped)typeof(GTA.Ped)
        .GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)!.Invoke(new object[] { handle });

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL " + message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
