using System.Collections.Generic;
using System.Linq;
using GTA;

namespace RageCoopAdmin.Client;

// NPC, которых каждая игра ставит сама на одни и те же места (люди в интерьерах, капитаны яхт)
// RageCoop не должен их отправлять, иначе у всех будут двойники, его список NPC проходит через Without
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

    // в peds все NPC игры, пропавший номер сразу забываем, пока игра не отдала его другому NPC
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
