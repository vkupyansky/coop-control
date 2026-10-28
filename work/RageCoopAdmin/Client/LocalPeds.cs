using System.Collections.Generic;
using System.Linq;
using GTA;

namespace RageCoopAdmin.Client;

// NPCs every game places itself at the same points (people in interiors, yacht captains).
// RageCoop must not send them or everyone gets doubles, so its NPC list goes through Without.
internal static class LocalPeds
{
    private static readonly HashSet<int> Handles = new HashSet<int>();
    private static readonly object Lock = new object();

    internal static void Add(int ped)
    {
        if (ped == 0) return;
        lock (Lock) Handles.Add(ped);
    }

    internal static void Remove(int ped)
    {
        lock (Lock) Handles.Remove(ped);
    }

    // peds holds all the game's NPCs. A vanished handle is forgotten at once, before the game gives it to another NPC.
    internal static Ped[] Without(Ped[] peds)
    {
        lock (Lock)
        {
            if (Handles.Count == 0) return peds;
            var present = new HashSet<int>(peds.Select(ped => ped.Handle));
            Handles.RemoveWhere(handle => !present.Contains(handle));
            return peds.Where(ped => !Handles.Contains(ped.Handle)).ToArray();
        }
    }
}
