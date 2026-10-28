using System.Runtime.CompilerServices;
using GTA;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Raw hashes: the bundled SHVDN names several of these natives differently.
internal static class SpecialPlaceNatives
{
    private const string Island = "HeistIsland";
    // Hash of "h4_fake_islandx": the island's minimap, drawn like an interior.
    private const int IslandRadar = unchecked((int)0xC0A90510);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void RequestIpl(string name) => Function.Call((Hash)0x41B4893843BBDB74UL, name); // REQUEST_IPL
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void RemoveIpl(string name) => Function.Call((Hash)0xEE6C5AD3ECE0A82DUL, name); // REMOVE_IPL
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsIplActive(string name) => Function.Call<bool>((Hash)0x88A741E44A2B3495UL, name); // IS_IPL_ACTIVE

    // Online map parts (heist yacht, carriers) are registered only in the Online
    // map group. Switching back (ON_ENTER_SP) reloads story mode: the game froze
    // for seconds and RageCoop disconnected with "Abort", so it is never called.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void EnterOnlineMap() => Function.Call((Hash)0x0888C3502DBBEEF5UL); // ON_ENTER_MP

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetIslandEnabled(bool enabled) => Function.Call((Hash)0x9A9D1BA639675CF1UL, Island, enabled); // SET_ISLAND_ENABLED
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetIslandRoads(bool enabled) => Function.Call((Hash)0xF74B1FFA4A15FBEAUL, enabled ? 1 : 0); // SET_ALLOW_STREAM_HEIST_ISLAND_NODES
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetIslandWater(bool enabled) => Function.Call((Hash)0x7E3F55ED251B76D3UL, enabled ? 1 : 0); // LOAD_GLOBAL_WATER_FILE
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsIslandWater() => Function.Call<int>((Hash)0xF741BD853611592DUL) == 1; // GET_GLOBAL_WATER_FILE
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetIslandMap(bool enabled) => Function.Call((Hash)0x5E1460624D194A38UL, enabled); // SET_USE_ISLAND_MAP
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void DrawIslandRadarThisFrame()
    {
        if (Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, Game.Player.Character) != 0) return;
        Function.Call((Hash)0xE81B7D2A3DAB2D81UL); // SET_RADAR_AS_EXTERIOR_THIS_FRAME
        Function.Call((Hash)0x59E727A1C9D3E31AUL, IslandRadar, 4700f, -5145f, 0, 0); // SET_RADAR_AS_INTERIOR_THIS_FRAME
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsWaypointActive() => Function.Call<bool>((Hash)0x1DD1F58F493F1DA5UL); // IS_WAYPOINT_ACTIVE
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsPauseMenuActive() => Function.Call<bool>((Hash)0xB0034A223497FFCBUL); // IS_PAUSE_MENU_ACTIVE
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsRouteFound() => Function.Call<bool>((Hash)0x869DAACBBE9FA006UL); // GET_GPS_BLIP_ROUTE_FOUND
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int RouteLength() => Function.Call<int>((Hash)0xBBB45C3CF5C8AA85UL); // GET_GPS_BLIP_ROUTE_LENGTH

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetPrologueRoads(bool enabled) => Function.Call((Hash)0x228E5C6AD4D74BFDUL, enabled); // SET_ALLOW_STREAM_PROLOGUE_NODES
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetPrologueMap(bool enabled) => Function.Call((Hash)0x9133955F1A2DA957UL, enabled); // SET_MINIMAP_IN_PROLOGUE
}
