using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopControl.Shared;
using HarmonyLib;
using RageCoopAdmin.Client;

// Snow tracks: the real switch runs here; only GTA's weather, tracks and sound
// banks are replaced by recorded fakes.
internal static class Program
{
    private static readonly List<string> Calls = new List<string>();
    private static int Previous;
    private static int Next;
    private static bool SoundsReady;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.snowtracks");
        try
        {
            foreach (string name in new[] { "PreviousWeather", "NextWeather", "SetTracks", "LoadSounds", "ReleaseSounds" })
                boundary.Patch(typeof(SnowNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            CheckSnow();
            Console.WriteLine("Snow track checks passed: " + Passes + ". The real switch runs; GTA's weather and effects are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static void CheckSnow()
    {
        int clear = WorldStateProtocol.WeatherHash("CLEAR");
        Check(SnowTracks.Christmas == WorldStateProtocol.WeatherHash(WorldOptions.WeatherCodes[Array.IndexOf(WorldOptions.WeatherNames, "Снег на земле")]),
            "the snow-on-the-ground choice in the shared weather list is the XMAS weather the tracks follow");
        var tracks = new SnowTracks();
        Previous = Next = clear;
        tracks.Tick(1_000);
        Check(!tracks.IsOn && Calls.Count == 0, "ordinary weather leaves the game's effects alone");
        Previous = clear;
        Next = SnowTracks.Christmas;
        tracks.Tick(1_500);
        Check(!tracks.IsOn, "the weather is checked once a second");
        tracks.Tick(2_000);
        Check(tracks.IsOn && Calls.SequenceEqual(new[] { "SetTracks(True)", "LoadSounds" }),
            "turning to snow on the ground switches ground snow, tracks and snow sounds on");
        Calls.Clear();
        tracks.Tick(3_016);
        tracks.Tick(3_032);
        Check(Calls.SequenceEqual(new[] { "LoadSounds", "LoadSounds" }), "the snow sounds are requested every frame until they load");
        SoundsReady = true;
        tracks.Tick(3_048);
        Calls.Clear();
        tracks.Tick(3_064);
        tracks.Tick(4_100);
        Check(Calls.Count == 0, "once loaded and on, nothing is repeated");
        Previous = Next = clear;
        tracks.Tick(5_200);
        Check(!tracks.IsOn && Calls.SequenceEqual(new[] { "SetTracks(False)", "ReleaseSounds" }), "another weather switches everything off");
        foreach (string code in new[] { "SNOW", "BLIZZARD", "SNOWLIGHT" })
        {
            var falling = new SnowTracks();
            Previous = Next = WorldStateProtocol.WeatherHash(code);
            falling.Tick(1_000);
            Check(falling.IsOn, code + " covers the ground with snow too");
            falling.Reset();
        }
        Calls.Clear();
        Check(!new[] { "EXTRASUNNY", "CLEAR", "RAIN", "THUNDER", "HALLOWEEN" }.Any(code => SnowTracks.Snowy.Contains(WorldStateProtocol.WeatherHash(code))),
            "rain, sun and Halloween leave the ground as it is");
        Previous = Next = SnowTracks.Christmas;
        tracks.Tick(6_300);
        Calls.Clear();
        tracks.Reset();
        Check(!tracks.IsOn && Calls.SequenceEqual(new[] { "SetTracks(False)", "ReleaseSounds" }), "leaving the server switches snow effects off");
        Calls.Clear();
        tracks.Reset();
        Check(Calls.Count == 0, "a reset with nothing on touches nothing");
        tracks.Tick(6_400);
        Check(tracks.IsOn, "the next session checks at once");
    }

    private static bool PreviousWeather(ref int __result) { __result = Previous; return false; }
    private static bool NextWeather(ref int __result) { __result = Next; return false; }
    private static bool SetTracks(bool on) { Calls.Add("SetTracks(" + on + ")"); return false; }
    private static bool LoadSounds(ref bool __result) { Calls.Add("LoadSounds"); __result = SoundsReady; return false; }
    private static bool ReleaseSounds() { Calls.Add("ReleaseSounds"); return false; }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL " + message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
