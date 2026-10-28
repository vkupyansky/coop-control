using System;
using System.Runtime.CompilerServices;
using GTA.Native;

namespace RageCoopAdmin.Client;

// building_controller locks a story safehouse for everyone but its owner hero,
// so an Online character (or a partner playing another hero) was shut inside
// or outside. While on the server these doors stay unlocked for every player.
// The script sets a door only when its own decision changes, so it does not
// lock it again each frame. The doors are checked once a second. Door system
// names and owners are from building_controller (doors 38-53): Michael's
// house without its bathroom window, Franklin's two houses, Trevor's trailer
// and Floyd's flat. Game thread only.
internal sealed class SafehouseDoors
{
    internal const long IntervalMilliseconds = 1_000;

    internal static readonly int[] Doors =
    {
        -2097039789, -2127416656, -1986583853, 776026812, 698422331, 535076355, 474675599, -1978427516, // Michael
        613961892, -272570634, -1040675994, 1201219326, // Franklin: Forum Drive, Vinewood Hills
        1736361794, 1113956670, -1361617046, // Trevor: trailer, Floyd's flat
    };

    private readonly Action<string>? _log;
    private long _next;
    private bool _logged;

    internal SafehouseDoors(Action<string>? log = null) => _log = log;

    internal void Tick(long now)
    {
        if (now < _next) return;
        _next = now + IntervalMilliseconds;
        int opened = 0;
        foreach (int door in Doors)
        {
            if (!SafehouseDoorNatives.IsRegistered(door) || SafehouseDoorNatives.State(door) == SafehouseDoorNatives.Unlocked) continue;
            SafehouseDoorNatives.Unlock(door);
            opened++;
        }
        if (opened == 0 || _logged) return;
        _logged = true;
        _log?.Invoke("OG Coop: story safehouse doors unlocked for every player.");
    }

    internal void Reset()
    {
        _next = 0;
        _logged = false;
    }
}

internal static class SafehouseDoorNatives
{
    internal const int Unlocked = 0;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsRegistered(int door) => Function.Call<bool>((Hash)0xC153C43EA202C8C1UL, door); // IS_DOOR_REGISTERED_WITH_SYSTEM
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int State(int door) => Function.Call<int>((Hash)0x160AA1B32F6139B8UL, door); // DOOR_SYSTEM_GET_DOOR_STATE
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Unlock(int door) => Function.Call((Hash)0x6BAB9442830C7F53UL, door, Unlocked, false, true); // DOOR_SYSTEM_SET_DOOR_STATE
}
