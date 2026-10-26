using System.Runtime.CompilerServices;
using OGCoop.Shared;
using GTA.Native;

namespace RageCoopAdmin.Client;

// GTA Online's winter snow on the ground. The XMAS weather alone showed no snow
// in the user's story-mode game (build 3889); the ground snow pass (build 3095+),
// tracks of wheels and feet and snow footstep sounds are forced here. They are switched
// on here while the game's weather is snowy (XMAS «Снег на земле», SNOW,
// BLIZZARD, SNOWLIGHT), and off when it changes or the player leaves the server.
// Checked once a second. Game thread only.
internal sealed class SnowTracks
{
    internal const long IntervalMilliseconds = 1_000;
    internal static readonly int Christmas = WorldStateProtocol.WeatherHash("XMAS");
    internal static readonly int[] Snowy =
    {
        Christmas, WorldStateProtocol.WeatherHash("SNOW"), WorldStateProtocol.WeatherHash("BLIZZARD"), WorldStateProtocol.WeatherHash("SNOWLIGHT"),
    };

    private long _next;
    private bool _on;
    private bool _soundsLoaded;

    internal bool IsOn => _on;

    internal void Tick(long now)
    {
        if (_on && !_soundsLoaded) _soundsLoaded = SnowNatives.LoadSounds();
        if (now < _next) return;
        _next = now + IntervalMilliseconds;
        bool snowing = System.Array.IndexOf(Snowy, SnowNatives.PreviousWeather()) >= 0 ||
            System.Array.IndexOf(Snowy, SnowNatives.NextWeather()) >= 0;
        if (snowing == _on) return;
        _on = snowing;
        SnowNatives.SetTracks(snowing);
        if (snowing)
        {
            _soundsLoaded = SnowNatives.LoadSounds();
        }
        else
        {
            SnowNatives.ReleaseSounds();
            _soundsLoaded = false;
        }
    }

    internal void Reset()
    {
        _next = 0;
        if (!_on) return;
        _on = false;
        _soundsLoaded = false;
        SnowNatives.SetTracks(false);
        SnowNatives.ReleaseSounds();
    }
}

// GTA boundary; isolated checks replace these calls.
internal static class SnowNatives
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int PreviousWeather() => Function.Call<int>(Hash.GET_PREV_WEATHER_TYPE_HASH_NAME);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int NextWeather() => Function.Call<int>(Hash.GET_NEXT_WEATHER_TYPE_HASH_NAME);

    // Native names from alloc8or's native DB; the ground pass exists since build 3095
    // (the supported Legacy build is 3889).
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetTracks(bool on)
    {
        Function.Call((Hash)0x6E9EF3A33C8899F8UL, on); // _FORCE_GROUND_SNOW_PASS: snow lies on the ground
        Function.Call((Hash)0x4CC7F0FEA5283FE0UL, on); // USE_SNOW_WHEEL_VFX_WHEN_UNSHELTERED: wheel tracks
        Function.Call((Hash)0xAEEDAD1420C65CC0UL, on); // USE_SNOW_FOOT_VFX_WHEN_UNSHELTERED: footprints
    }

    // Footsteps on snow and ice; the request is repeated until both banks are in.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool LoadSounds() =>
        Function.Call<bool>(Hash.REQUEST_SCRIPT_AUDIO_BANK, "SNOW_FOOTSTEPS", false, -1) &
        Function.Call<bool>(Hash.REQUEST_SCRIPT_AUDIO_BANK, "ICE_FOOTSTEPS", false, -1);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ReleaseSounds()
    {
        Function.Call(Hash.RELEASE_NAMED_SCRIPT_AUDIO_BANK, "SNOW_FOOTSTEPS");
        Function.Call(Hash.RELEASE_NAMED_SCRIPT_AUDIO_BANK, "ICE_FOOTSTEPS");
    }
}
