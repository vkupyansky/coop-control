using System;
using System.Drawing;
using System.Reflection;
using GTA;
using GTA.Input;
using GTA.UI;
using RageCoop.Client;

namespace RageCoopAdmin.Client;

// RageCoop's push-to-talk voice: GTA's "Push to talk" control (N on the
// keyboard) records the default microphone and the server relays it to every
// player. OG Coop turns it on by default with an off switch in Settings.
// RageCoop stops recording only at the next key press, so a released key kept
// the microphone open; here recording stops as soon as the key is released.
internal sealed class VoiceChat
{
    private const int PushToTalk = 249;
    private static readonly Assembly Engine = typeof(SyncedPed).Assembly;
    private static readonly Type? VoiceType = Engine.GetType("RageCoop.Client.Voice", false);
    private static readonly FieldInfo? SettingsField = Engine.GetType("RageCoop.Client.Main", false)?.GetField("Settings", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly PropertyInfo? VoiceSetting = SettingsField?.FieldType.GetProperty("Voice", BindingFlags.Instance | BindingFlags.Public);
    private static readonly MethodInfo? WasInitialized = Method("WasInitialized");
    private static readonly MethodInfo? IsRecording = Method("IsRecording");
    private static readonly MethodInfo? Init = Method("Init");
    private static readonly MethodInfo? ClearAll = Method("ClearAll");
    private static readonly MethodInfo? StopRecording = Method("StopRecording");
    private readonly Action<string> _log;
    private bool _reported;

    internal VoiceChat(Action<string> log) => _log = log;

    internal static bool IsAvailable => VoiceType != null && SettingsField != null && VoiceSetting?.PropertyType == typeof(bool) &&
        WasInitialized?.ReturnType == typeof(bool) && IsRecording?.ReturnType == typeof(bool) && Init != null && ClearAll != null && StopRecording != null;

    // `quiet`: no map switch or teleport is loading the world; playback (the
    // sound device) starts only then and stays on afterwards.
    internal void Tick(bool enabled, bool quiet, string speakingText)
    {
        if (!IsAvailable)
        {
            if (!_reported) _log("OG Coop: voice chat controls are unavailable in this RageCoop build.");
            _reported = true;
            return;
        }

        object? settings = SettingsField!.GetValue(null);
        if (settings == null) return;
        bool current = (bool)VoiceSetting!.GetValue(settings)!;
        if (!enabled)
        {
            if (!current) return;
            VoiceSetting.SetValue(settings, false);
            ClearAll!.Invoke(null, null);
            _log("OG Coop: voice chat off");
            return;
        }

        if (!current || !(bool)WasInitialized!.Invoke(null, null)!)
        {
            // While off, RageCoop drops voice from others and ignores the key.
            if (!quiet) return;
            if (!current)
            {
                VoiceSetting.SetValue(settings, true);
                _log("OG Coop: voice chat on");
            }

            if (!(bool)WasInitialized!.Invoke(null, null)!) Init!.Invoke(null, null);
        }

        if (!(bool)IsRecording!.Invoke(null, null)!) return;
        if (!Controls.IsDisabledControlPressed(ControlType.PlayerControl, (ControlAction)PushToTalk))
        {
            StopRecording!.Invoke(null, null);
            return;
        }

        VoiceIndicator.Draw(speakingText);
    }

    // Leaving the server or stopping: voice goes off, so RageCoop does not start
    // it on the next joining before the map has loaded.
    internal void Reset()
    {
        if (!IsAvailable) return;
        object? settings = SettingsField!.GetValue(null);
        if (settings == null || !(bool)VoiceSetting!.GetValue(settings)!) return;
        VoiceSetting.SetValue(settings, false);
        ClearAll!.Invoke(null, null);
    }

    private static MethodInfo? Method(string name) =>
        VoiceType?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
}

// The "microphone on" line at the bottom of the screen; created in the game only.
internal static class VoiceIndicator
{
    private static TextElement? _text;

    internal static void Draw(string caption)
    {
        _text ??= new TextElement(caption, new PointF(640f, 680f), 0.38f, Palette.WithAlpha(Palette.Orange, 230), GTA.UI.Font.ChaletLondon, Alignment.Center, true, true);
        if (_text.Caption != caption) _text.Caption = caption;
        _text.Draw();
    }
}
