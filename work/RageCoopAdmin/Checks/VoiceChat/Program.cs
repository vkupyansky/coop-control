using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GTA.Input;
using GTA.UI;
using HarmonyLib;
using RageCoop.Client;
using RageCoopAdmin.Client;

// Voice chat against the actual RageCoop client: its Voice class and settings
// are found by the real reflection, and its microphone, playback, GTA input and
// drawing are replaced by recorded fakes.
internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<string> Calls = new List<string>();
    private static bool _initialized;
    private static bool _recording;
    private static bool _keyHeld;
    private static int _drawn;
    private static int _passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.voicechat");
        try
        {
            Type voice = typeof(SyncedPed).Assembly.GetType("RageCoop.Client.Voice", true)!;
            foreach (string name in new[] { "WasInitialized", "IsRecording", "Init", "ClearAll", "StopRecording" })
                boundary.Patch(voice.GetMethod(name, Static, null, Type.EmptyTypes, null)!, prefix: new HarmonyMethod(typeof(Program), name));
            boundary.Patch(typeof(Controls).GetMethod(nameof(Controls.IsDisabledControlPressed), new[] { typeof(ControlType), typeof(ControlAction) })!,
                prefix: new HarmonyMethod(typeof(Program), nameof(Pressed)));
            boundary.Patch(typeof(VoiceIndicator).GetMethod(nameof(VoiceIndicator.Draw), Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(Draw)));
            FieldInfo settingsField = typeof(SyncedPed).Assembly.GetType("RageCoop.Client.Main", true)!.GetField("Settings", Static)!;
            var settings = new Settings { Voice = false };
            settingsField.SetValue(null, settings);

            Check(VoiceChat.IsAvailable, "the installed RageCoop has the voice class, its methods and the Voice setting the switch needs");
            var log = new List<string>();
            var chat = new VoiceChat(log.Add);
            chat.Tick(true, false, "Microphone on");
            Check(!settings.Voice && Calls.Count == 0 && log.Count == 0,
                "while the map switch or a teleport loads the world the sound device is not opened");
            chat.Tick(true, true, "Microphone on");
            Check(settings.Voice && Calls.SequenceEqual(new[] { "Init" }) && log.Single() == "Coop Control: voice chat on",
                "on by default: once the world is quiet RageCoop's voice setting is turned on and playback starts");
            Calls.Clear();
            chat.Tick(true, false, "Microphone on");
            Check(settings.Voice && Calls.Count == 0, "a later teleport leaves started voice on");
            _recording = true;
            _keyHeld = true;
            chat.Tick(true, true, "Microphone on");
            Check(Calls.Count == 0 && _drawn == 1, "while push-to-talk is held the microphone stays on and the player sees it");
            _keyHeld = false;
            chat.Tick(true, true, "Microphone on");
            Check(Calls.SequenceEqual(new[] { "StopRecording" }) && !_recording && _drawn == 1,
                "releasing push-to-talk stops the microphone at once, not at the next key press");
            Calls.Clear();
            chat.Tick(false, true, "Microphone on");
            Check(!settings.Voice && Calls.SequenceEqual(new[] { "ClearAll" }) && log.Last() == "Coop Control: voice chat off",
                "turning voice chat off in Settings stops playback and recording");
            Calls.Clear();
            chat.Tick(false, true, "Microphone on");
            Check(Calls.Count == 0, "while off nothing is started again");
            chat.Tick(true, true, "Microphone on");
            Calls.Clear();
            chat.Reset();
            Check(!settings.Voice && Calls.SequenceEqual(new[] { "ClearAll" }),
                "leaving the server turns voice off, so RageCoop does not open the sound device on the next joining before the map loads");
            Calls.Clear();
            chat.Reset();
            Check(Calls.Count == 0, "leaving again with voice off does nothing");
            Console.WriteLine("Voice chat checks passed: " + _passes + ". The actual RageCoop voice members are used; audio and GTA input are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    private static bool WasInitialized(ref bool __result) { __result = _initialized; return false; }
    private static bool IsRecording(ref bool __result) { __result = _recording; return false; }
    private static bool Init() { Calls.Add("Init"); _initialized = true; return false; }
    private static bool ClearAll() { Calls.Add("ClearAll"); _initialized = false; _recording = false; return false; }
    private static bool StopRecording() { Calls.Add("StopRecording"); _recording = false; return false; }
    private static bool Pressed(ControlAction action, ref bool __result) { __result = _keyHeld && (int)action == 249; return false; }
    private static bool Draw() { _drawn++; return false; }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + message);
        _passes++;
        Console.WriteLine("PASS " + message);
    }
}
