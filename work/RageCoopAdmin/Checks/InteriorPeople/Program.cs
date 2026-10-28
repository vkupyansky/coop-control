using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GTA.Math;
using HarmonyLib;
using RageCoopAdmin.Client;

// People in interiors: the real data and rules, GTA calls replaced.
internal static class Program
{
    private const int Casino = 7;
    private const int Hangar = 8;
    private const int Player = 1;
    private static readonly Dictionary<int, (uint Model, Vector3 At)> Peds = new Dictionary<int, (uint, Vector3)>();
    private static readonly Dictionary<int, int> Things = new Dictionary<int, int>();
    private static readonly List<string> Calls = new List<string>();
    private static readonly List<string> Log = new List<string>();
    private static readonly HashSet<uint> Released = new HashSet<uint>();
    private static readonly HashSet<uint> NeverLoads = new HashSet<uint>();
    private static readonly HashSet<string> MissingAnimations = new HashSet<string>();
    private static bool _modelsLoaded;
    private static bool _animationsLoaded;
    private static int _inside;
    private static Vector3 _at;
    private static int _next = 1000;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.interiorpeople");
        try
        {
            foreach (MethodInfo native in typeof(InteriorPeopleNatives).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(method => method.DeclaringType == typeof(InteriorPeopleNatives)))
            {
                boundary.Patch(native, prefix: new HarmonyMethod(typeof(Program), native.Name));
            }
            foreach (string name in new[] { "InteriorAt", "IsReady" })
                boundary.Patch(typeof(InteriorNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            CheckData();
            CheckPlacing();
            CheckKeeping();
            CheckLeaving();
            CheckProblems();
            CheckOutdoor();
            Console.WriteLine("Interior people checks passed: " + Passes + ". The Online data and placing rules are the real code; GTA calls are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static void CheckData()
    {
        InteriorPedSet[] sets = InteriorPedData.Sets;
        Dictionary<string, OnlineInterior> interiors = OnlineInteriors.All.ToDictionary(interior => interior.Key);
        Check(sets.Length == 23 && sets.Select(set => set.Interior).Distinct().Count() == sets.Length &&
            sets.Where(set => !interiors.ContainsKey(set.Interior)).Select(set => set.Interior).SequenceEqual(new[] { InteriorPeople.CayoBeachParty }),
            "22 of our interiors and the Cayo Perico beach party have their Online people, each once and under its own key");
        Check(sets.All(set => set.People.Length is > 0 and <= 30) && sets.Sum(set => set.People.Length) == 232,
            "a few per place, as asked: 232 in all, at most 30 in one");
        Check(sets.Where(set => interiors.ContainsKey(set.Interior)).All(set => set.People.All(person => Near(person.Position, interiors[set.Interior]))) &&
            sets.Single(set => set.Interior == InteriorPeople.CayoBeachParty).People.All(person =>
                new Vector2(person.Position.X - 4893f, person.Position.Y + 4906f).Length() < 80f && person.Position.Z > 0f && person.Position.Z < 10f),
            "everyone stands inside their interior, near its inside point; the beach party on the island's northern beach");
        Check(new[] { "casino_penthouse", "agents_factory_inside", "mansion_vinewood_workshop", "smoke_on_the_water_office", "car_meet_track" }
                .All(key => sets.Any(set => set.Interior == key)) && sets.Single(set => set.Interior == "casino_penthouse").People.Length == 16,
            "the new places have people too: the penthouse party, the factory, the Kortz workshop, the Smoke on the Water office, the test track");
        Check(sets.SelectMany(set => set.People).All(person => person.Model != 0 && Finite(person.Position) && person.Heading >= 0f && person.Heading < 360f),
            "every person has a model, a place and a heading");
        Check(sets.SelectMany(set => set.People).All(person => person.Scenario.Length > 0 ? person.Animations.Length == 0 && person.Clip.Length == 0 :
                person.Animations.Length > 0 && person.Clip.Length > 0),
            "everyone does something: a looping animation or a scenario");
        Check(sets.SelectMany(set => set.People).All(person => InteriorPedLook.TryParse(person.Look, out PedPart[] clothes, out PedPart[] props) &&
                clothes.All(part => part.Slot < InteriorPedLook.Slots) && props.All(part => part.Slot < InteriorPedLook.PropSlots) &&
                InteriorPedLook.TryParseHeld(person.Held, out HeldItem[] items) && items.All(item => item.Bone > 0)),
            "the clothes, props and things in hand read back from the data");
        Check(InteriorPedLook.TryParse("4/0 0/0 4/1 6/1 4/2 0/0 0/0 0/0 0/0 0/0 - - 1:0/2", out PedPart[] worn, out PedPart[] extra) &&
            worn.Length == 10 && worn[2].Slot == 2 && worn[2].Drawable == 4 && worn[2].Texture == 1 && extra.Single().Slot == 1 && extra[0].Texture == 2 &&
            !InteriorPedLook.TryParse("4/0 0/0", out _, out _) && !InteriorPedLook.TryParse("x/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0", out _, out _) &&
            !InteriorPedLook.TryParse("0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 0/0 9:0/0", out _, out _),
            "a look gives the slots it names and keeps the model's own where it says -; bad text is refused");
        Check(InteriorPedLook.TryParseHeld("28BE7556@28422@0,0,0.08@0,0,0;3A1B896A@60309@0,0,0@-90,0,0", out HeldItem[] held) && held.Length == 2 &&
            held[0].Model == 0x28BE7556u && held[0].Bone == 28422 && held[0].Offset.Z == 0.08f && held[1].Rotation.X == -90f &&
            !InteriorPedLook.TryParseHeld("0@28422@0,0,0@0,0,0", out _) && !InteriorPedLook.TryParseHeld("28BE7556@28422@0,0@0,0,0", out _),
            "things in hand give the model, the hand's bone and how they sit in it");
        // 1 loop, 2 hold the last frame, 4096 the clip's offset, which moves a standing person a second time.
        InteriorPed[] everyone = sets.SelectMany(set => set.People).ToArray();
        Check(everyone.All(person => (person.Flags & 1) == 1 && (person.Flags & 2) == 0) &&
            sets.Single(set => set.Interior == "casino_floor").People.Count(person => (person.Flags & 4096) == 0) >= 15 &&
            everyone.Count(person => (person.Flags & 4096) != 0) < everyone.Length / 4,
            "animations loop and keep their script's flags; the clip's own offset only where the script uses it, so nobody hangs in the air");
        InteriorPed[] staff = sets.Single(set => set.Interior == "casino_floor").People;
        Check(staff.Count(person => person.Model == 0x1422D45Bu) == 5 && staff.Any(person => person.Model == 0xBB0D72F5u) && staff.Any(person => person.Model == 0x035456A4u),
            "the casino has its guards, the cashier and Tom, then groups of guests");
    }

    private static void CheckPlacing()
    {
        Reset();
        var people = new InteriorPeople(InteriorPedData.Sets, OnlineInteriors.All, Log.Add);
        OnlineInterior casino = OnlineInteriors.All.Single(interior => interior.Key == "casino_floor");
        int expected = InteriorPedData.Sets.Single(set => set.Interior == "casino_floor").People.Length;
        _at = new Vector3(casino.X, casino.Y, casino.Z + 100f);
        _inside = 0;
        _modelsLoaded = _animationsLoaded = true;
        for (int frame = 0; frame < 10; frame++) people.Tick(frame * 16);
        Check(Peds.Count == 0, "above the casino, outside it, nobody is placed");
        _at = new Vector3(casino.X, casino.Y, casino.Z);
        _inside = Casino;
        _modelsLoaded = _animationsLoaded = false;
        people.Tick(2000);
        Check(Peds.Count == 0 && Calls.Count(call => call.StartsWith("LoadModel", StringComparison.Ordinal)) >= expected,
            "inside, the models and animations are asked for first; nobody appears before they are in memory");
        _modelsLoaded = _animationsLoaded = true;
        people.Tick(2016);
        Check(Peds.Count == InteriorPeople.CreatePerFrame, "then a couple appear each frame, so the game does not stall");
        for (int frame = 2; frame < 40; frame++) people.Tick(2000 + frame * 16);
        Check(Peds.Count == expected && Log.Count(line => line == "OG Coop: interior people in casino_floor: " + expected + " of " + expected + ".") == 1,
            "soon all of the casino's people are there, reported once");
        InteriorPed first = InteriorPedData.Sets.Single(set => set.Interior == "casino_floor").People[0];
        Check(Peds.Values.Any(ped => ped.Model == first.Model && ped.At == first.Position) &&
            Calls.Contains("Play(" + first.Animations + ", " + first.Clip + ", " + first.Heading.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", " + first.Flags + ")"),
            "each stands where Online has them and plays Online's animation facing Online's way, with its script's flags");
        Check(Things.Count == InteriorPedData.Sets.Single(set => set.Interior == "casino_floor").People.Sum(person =>
                InteriorPedLook.TryParseHeld(person.Held, out HeldItem[] items) ? items.Length : 0),
            "glasses, bottles and phones are in their hands");
        Check(LocalPeds.Without(Peds.Keys.Select(NativePed).ToArray()).Length == 0,
            "none of them goes to the other players: each game places its own");
        Check(!Peds.Values.Any(ped => InteriorPedData.Sets.Where(set => set.Interior != "casino_floor").Any(set => set.People.Any(person => person.Position == ped.At))),
            "people of other interiors stay away");
        people.Reset();
        Check(Peds.Count == 0 && Things.Count == 0 && LocalPeds.Without(new[] { NativePed(1001) }).Length == 1, "leaving the server takes them all away");
    }

    private static void CheckKeeping()
    {
        Reset();
        var people = new InteriorPeople(InteriorPedData.Sets, OnlineInteriors.All, Log.Add);
        OnlineInterior hangar = OnlineInteriors.All.Single(interior => interior.Key == "hangar_inside");
        _at = new Vector3(hangar.X, hangar.Y, hangar.Z);
        _inside = Hangar;
        _modelsLoaded = _animationsLoaded = true;
        for (int frame = 0; frame < 10; frame++) people.Tick(frame * 16);
        InteriorPed[] crew = InteriorPedData.Sets.Single(set => set.Interior == "hangar_inside").People;
        Check(Peds.Count == crew.Length && Calls.Count(call => call.StartsWith("Scenario(", StringComparison.Ordinal)) == crew.Count(person => person.Scenario.Length > 0),
            "in the hangar Rooster and the crew stand at their posts, some in their scenarios");
        int gone = Peds.Keys.First();
        Peds.Remove(gone);
        people.Tick(2000);
        people.Tick(2016);
        Check(Peds.Count == crew.Length && !Peds.ContainsKey(gone), "someone GTA removed comes back");
        Calls.Clear();
        _playing = false;
        people.Tick(4000);
        Check(Calls.Count(call => call.StartsWith("Play(", StringComparison.Ordinal)) == crew.Count(person => person.Scenario.Length == 0) &&
            Calls.Count(call => call.StartsWith("Scenario(", StringComparison.Ordinal)) == crew.Count(person => person.Scenario.Length > 0),
            "an animation or scenario that stopped starts again");
        _playing = true;
        people.Reset();
    }

    private static void CheckLeaving()
    {
        Reset();
        var people = new InteriorPeople(InteriorPedData.Sets, OnlineInteriors.All, Log.Add);
        OnlineInterior hangar = OnlineInteriors.All.Single(interior => interior.Key == "hangar_inside");
        _at = new Vector3(hangar.X, hangar.Y, hangar.Z);
        _inside = Hangar;
        _modelsLoaded = _animationsLoaded = true;
        for (int frame = 0; frame < 10; frame++) people.Tick(frame * 16);
        int[] placed = Peds.Keys.ToArray();
        _inside = 0;
        people.Tick(1000);
        people.Tick(2900);
        Check(Peds.Count == placed.Length, "passing a door does not take them away at once");
        people.Tick(3100);
        Check(Peds.Count == 0 && Released.Count > 0 && Log.Contains("OG Coop: interior people in hangar_inside removed."),
            "two seconds after the player left they go, and their models are let go");
        Check(LocalPeds.Without(placed.Select(NativePed).ToArray()).Length == placed.Length,
            "their handles are free again for RageCoop");
        _inside = Hangar;
        for (int frame = 0; frame < 10; frame++) people.Tick(4000 + frame * 16);
        Check(Peds.Count == placed.Length, "coming back brings them back");
        people.Reset();
    }

    private static void CheckProblems()
    {
        Reset();
        InteriorPed[] crew = InteriorPedData.Sets.Single(set => set.Interior == "hangar_inside").People;
        InteriorPed animated = crew.First(person => person.Scenario.Length == 0);
        MissingAnimations.Add(animated.Animations);
        NeverLoads.Add(crew.Last().Model);
        var people = new InteriorPeople(InteriorPedData.Sets, OnlineInteriors.All, Log.Add);
        OnlineInterior hangar = OnlineInteriors.All.Single(interior => interior.Key == "hangar_inside");
        _at = new Vector3(hangar.X, hangar.Y, hangar.Z);
        _inside = Hangar;
        _modelsLoaded = _animationsLoaded = true;
        for (int frame = 0; frame < 10; frame++) people.Tick(frame * 16);
        Check(Peds.Values.Any(ped => ped.At == animated.Position) && !Calls.Any(call => call.StartsWith("Play(" + animated.Animations, StringComparison.Ordinal)),
            "someone whose animation the game lacks still stands in place");
        int waiting = Peds.Count;
        people.Tick(10200);
        people.Tick(10216);
        Check(Peds.Count == waiting && Log.Any(line => line.StartsWith("OG Coop: interior person in hangar_inside skipped (" + crew.Last().Model.ToString("X8"), StringComparison.Ordinal)) &&
            Log.Any(line => line.StartsWith("OG Coop: interior people in hangar_inside: " + waiting + " of " + crew.Length + ", 1 without animation", StringComparison.Ordinal)),
            "a model that never loads is skipped after ten seconds, the rest stay, and the log says so");
        people.Reset();
    }

    // The beach party shows only while the island is loaded and the player is near.
    private static void CheckOutdoor()
    {
        Reset();
        bool island = false;
        var people = new InteriorPeople(InteriorPedData.Sets, OnlineInteriors.All, Log.Add, place => place == InteriorPeople.CayoBeachParty && island);
        InteriorPed[] party = InteriorPedData.Sets.Single(set => set.Interior == InteriorPeople.CayoBeachParty).People;
        _at = party[0].Position + new Vector3(20f, 0f, 0f);
        _inside = 0;
        _modelsLoaded = _animationsLoaded = true;
        for (int frame = 0; frame < 20; frame++) people.Tick(frame * 16);
        Check(Peds.Count == 0, "on the beach while the island is not loaded nobody is placed");
        island = true;
        for (int frame = 0; frame < 20; frame++) people.Tick(1000 + frame * 16);
        Check(Peds.Count == party.Length && LocalPeds.Without(Peds.Keys.Select(NativePed).ToArray()).Length == 0,
            "with the island loaded the beach party is there, every game places its own");
        _at = party[0].Position + new Vector3(400f, 0f, 0f);
        people.Tick(2000);
        people.Tick(4100);
        Check(Peds.Count == 0 && Log.Contains("OG Coop: interior people in cayo_beach_party removed."), "sailing away takes the party away");
        people.Reset();
    }

    private static bool Near(Vector3 at, OnlineInterior interior) =>
        new Vector2(at.X - interior.X, at.Y - interior.Y).Length() < 250f && Math.Abs(at.Z - interior.Z) < 20f;

    private static bool Finite(Vector3 at) => !float.IsNaN(at.X + at.Y + at.Z) && !float.IsInfinity(at.X + at.Y + at.Z);

    private static GTA.Ped NativePed(int handle) => (GTA.Ped)typeof(GTA.Ped)
        .GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)!.Invoke(new object[] { handle });

    private static void Reset()
    {
        Peds.Clear(); Things.Clear(); Calls.Clear(); Log.Clear(); Released.Clear(); NeverLoads.Clear(); MissingAnimations.Clear();
        _modelsLoaded = _animationsLoaded = false;
        _playing = true;
    }

    private static bool _playing = true;

    // A stand-in world: the player, the interiors at the casino and the hangar, the people placed.
    private static bool InteriorAt(float x, float y, float z, ref int __result)
    {
        __result = Within(x, y, "casino_floor") ? Casino : Within(x, y, "hangar_inside") ? Hangar : 0;
        return false;
    }

    private static bool Within(float x, float y, string key)
    {
        OnlineInterior interior = OnlineInteriors.All.Single(item => item.Key == key);
        return new Vector2(x - interior.X, y - interior.Y).Length() < 120f;
    }

    private static bool IsReady(ref bool __result) { __result = true; return false; }
    private static bool PlayerPed(ref int __result) { __result = Player; return false; }
    private static bool InteriorOf(ref int __result) { __result = _inside; return false; }
    private static bool Position(ref Vector3 __result) { __result = _at; return false; }
    private static bool Exists(int entity, ref bool __result) { __result = Peds.ContainsKey(entity); return false; }
    private static bool LoadModel(uint model, ref bool __result)
    {
        Calls.Add("LoadModel(" + model.ToString("X8") + ")");
        __result = _modelsLoaded && !NeverLoads.Contains(model);
        return false;
    }

    private static bool ReleaseModel(uint model) { Released.Add(model); return false; }
    private static bool AnimationsExist(string animations, ref bool __result) { __result = !MissingAnimations.Contains(animations); return false; }
    private static bool LoadAnimations(ref bool __result) { __result = _animationsLoaded; return false; }
    private static bool ReleaseAnimations() => false;

    private static bool CreatePed(uint model, Vector3 at, ref int __result)
    {
        __result = ++_next;
        Peds[__result] = (model, at);
        return false;
    }

    private static bool Dress() => false;

    private static bool PlayAnimation(string animations, string clip, float heading, int flags)
    {
        Calls.Add("Play(" + animations + ", " + clip + ", " + heading.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", " + flags + ")");
        return false;
    }

    private static bool IsPlaying(ref bool __result) { __result = _playing; return false; }
    private static bool StartScenario(string scenario) { Calls.Add("Scenario(" + scenario + ")"); return false; }
    private static bool UsesScenario(ref bool __result) { __result = _playing; return false; }

    private static bool Hold(int ped, ref int __result)
    {
        __result = ++_next;
        Things[__result] = ped;
        return false;
    }

    private static bool Delete(int entity)
    {
        Peds.Remove(entity);
        Things.Remove(entity);
        return false;
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL: " + message);
        Passes++;
        Console.WriteLine("PASS: " + message);
    }
}
