using System;
using System.Collections.Generic;
using System.Reflection;
using OGCoop.Shared;
using GTA;
using HarmonyLib;
using RageCoop.Client;
using Player = RageCoop.Client.Player;

namespace RageCoopAdmin.Client;

// RageCoop dresses another player's copy from bytes: GTA Online items above
// 255 come out wrong, and the face and accessories are not sent at all. Each
// time RageCoop dresses a copy (when it is made and when the clothes change),
// the character the server sent for that player is put on in full.
internal static class CharacterReplicas
{
    private const string PatchId = "friendslegacy.ragecoop.characters";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Harmony? _harmony;
    private static Func<string, CharacterLook?>? _lookOf;
    private static Action<string>? _warn;
    private static bool _warned;

    internal static MethodInfo? FindTarget() => typeof(SyncedPed).GetMethod("SetClothes", Instance, null, Type.EmptyTypes, null);

    internal static bool Install(Func<string, CharacterLook?> lookOf, Action<string> warn)
    {
        if (_harmony != null) return true;
        MethodInfo? target = FindTarget();
        if (target?.ReturnType != typeof(void)) return false;
        _lookOf = lookOf;
        _warn = warn;
        _warned = false;
        var harmony = new Harmony(PatchId);
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(CharacterReplicas), nameof(AfterSetClothes)));
        _harmony = harmony;
        return true;
    }

    internal static void Uninstall()
    {
        MethodInfo? target = FindTarget();
        if (_harmony != null && target != null) _harmony.Unpatch(target, HarmonyPatchType.Postfix, PatchId);
        _harmony = null;
        _lookOf = null;
        _warn = null;
    }

    // A character that arrives later goes on that player's copy at once.
    internal static void Dress(string username)
    {
        if (!PlayerSnapshots.TryGet(out Dictionary<int, Player> players)) return;
        foreach (Player player in players.Values)
        {
            if (!string.Equals(player.Username, username, StringComparison.OrdinalIgnoreCase)) continue;
            SyncedPed? copy = RuntimeEntityLookup.GetPlayerPed(player.ID);
            if (copy != null) Dress(copy, player);
        }
    }

    private static void AfterSetClothes(SyncedPed __instance)
    {
        try
        {
            if (!__instance.IsPlayer || __instance.IsLocal || !PlayerSnapshots.TryGet(out Dictionary<int, Player> players) ||
                !players.TryGetValue(__instance.OwnerID, out Player? owner)) return;
            Dress(__instance, owner);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            // RageCoop's own clothes stay; the failure is reported once per resource start.
            if (_warned) return;
            _warned = true;
            _warn?.Invoke("OG Coop: a player's character could not be put on their copy: " + error.GetType().Name);
        }
    }

    private static void Dress(SyncedPed copy, Player owner)
    {
        if (copy.IsLocal || _lookOf?.Invoke(owner.Username) is not { IsOnline: true } look) return;
        Ped? body = copy.MainPed;
        if (body == null || !body.Exists() || body.Model.Hash != look.Model) return;
        CharacterNatives.Apply(body.Handle, look);
    }
}
