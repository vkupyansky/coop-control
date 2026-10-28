using System;

namespace RageCoopAdmin.Client;

// Story mode's blip_controller adds the game's own map icons (shops, Los
// Santos Customs, activities) only while fewer than 150 icons exist and
// otherwise waits. With all our places and interiors on the map it stopped
// adding them. Our icons get what is left under that limit, nearest first. On
// the open pause map all are shown (the game is paused, it adds nothing then).
internal static class MapIconBudget
{
    internal const int GameLimit = 150;
    internal const int Spare = 10;

    // How many of our icons may exist next to everyone else's.
    internal static int Allowed(int othersActive, bool pauseMap, int wanted) =>
        pauseMap ? wanted : Math.Max(0, Math.Min(wanted, GameLimit - Spare - Math.Max(0, othersActive)));
}
