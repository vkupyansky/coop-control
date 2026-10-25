using System;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Radio stations of GTA Online updates are hidden from the story-mode radio
// wheel. While the player is on the server they are shown; the game keeps them
// until it is restarted. Every player's game shows the same list, so the radio
// station number RageCoop sends between games names the same station.
// Media Player is left out: in story mode it has no music to play.
internal sealed class OnlineRadio
{
    internal static readonly string[] Stations =
    {
        "RADIO_21_DLC_XM17", // Blonded Los Santos 97.8 FM
        "RADIO_22_DLC_BATTLE_MIX1_RADIO", // Los Santos Underground Radio
        "RADIO_23_DLC_XM19_RADIO", // iFruit Radio
        "RADIO_27_DLC_PRHEI4", // Still Slipping Los Santos
        "RADIO_34_DLC_HEI4_KULT", // Kult FM
        "RADIO_35_DLC_HEI4_MLR", // The Music Locker
        "RADIO_37_MOTOMAMI", // MOTOMAMI Lost Santos
    };

    private readonly Action<string> _log;
    private bool _shown;

    internal OnlineRadio(Action<string> log) => _log = log;

    internal void Tick()
    {
        if (_shown) return;
        _shown = true;
        int before = OnlineRadioNatives.UnlockedCount();
        foreach (string station in Stations) OnlineRadioNatives.Show(station);
        _log("Coop Control: Online radio stations shown: " + before + " -> " + OnlineRadioNatives.UnlockedCount());
    }

    // A new connection shows them again; repeating is harmless.
    internal void Reset() => _shown = false;
}

internal static class OnlineRadioNatives
{
    internal static int UnlockedCount() => Function.Call<int>(Hash.GET_NUM_UNLOCKED_RADIO_STATIONS);

    // LOCK_RADIO_STATION(name, false) puts a station back on the radio wheel.
    internal static void Show(string station) => Function.Call(Hash.LOCK_RADIO_STATION, station, false);
}
