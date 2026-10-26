using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OGCoop.Shared;
using GTA;
using GTA.Math;
using GTA.Native;
using HarmonyLib;
using RageCoop.Client;
using Player = RageCoop.Client.Player;

namespace RageCoopAdmin.Client;

// RageCoop converts the head to pixels from the game resolution, draws in
// LemonUI's own scaled space and lifts the name 100 px: on a non-16:9 screen
// the name drifts sideways, and at distance it floats high above the player.
// Keep its visibility rules; let the game anchor the text above the head.
internal static class EngineNameTagGuard
{
    private const string PatchId = "friendslegacy.ragecoop.enginenametag";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const float HeadClearance = 0.35f;
    private static Func<SyncedEntity, Player>? _readOwner;
    private static Harmony? _harmony;

    internal static MethodInfo? FindTarget() => typeof(SyncedPed).GetMethod("RenderNameTag", Instance, null, Type.EmptyTypes, null);

    internal static bool Install()
    {
        if (_harmony != null) return true;
        MethodInfo? target = FindTarget(), owner = typeof(SyncedEntity).GetProperty("Owner", Instance)?.GetGetMethod(true);
        if (target?.ReturnType != typeof(void) || owner?.ReturnType != typeof(Player) ||
            !TryFindDrawBlock(PatchProcessor.GetOriginalInstructions(target), out _, out _)) return false;
        _readOwner = (Func<SyncedEntity, Player>)Delegate.CreateDelegate(typeof(Func<SyncedEntity, Player>), owner);
        var harmony = new Harmony(PatchId);
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(EngineNameTagGuard), nameof(ReplaceDraw)));
        _harmony = harmony;
        return true;
    }

    internal static void Uninstall()
    {
        MethodInfo? target = FindTarget();
        if (_harmony != null && target != null) _harmony.Unpatch(target, HarmonyPatchType.Transpiler, PatchId);
        _harmony = null;
        _readOwner = null;
    }

    // The block runs from the head-bone read to ScaledText.Draw and is entered
    // only at its first instruction; the gate before it stays untouched.
    internal static bool TryFindDrawBlock(IList<CodeInstruction> codes, out int start, out int end)
    {
        start = end = -1;
        int[] bones = Indexes(codes, method => method.DeclaringType == typeof(Ped) && method.Name == "get_Bones");
        int[] project = Indexes(codes, method => method.DeclaringType?.FullName == "RageCoop.Client.Util" && method.Name == "WorldToScreen");
        int[] draw = Indexes(codes, method => method.DeclaringType?.FullName == "LemonUI.Elements.ScaledText" && method.Name == "Draw");
        if (bones.Length != 1 || project.Length != 1 || draw.Length != 1 || bones[0] < 2 ||
            codes[bones[0] - 2].opcode != OpCodes.Ldarg_0 || codes[bones[0] - 1].operand is not MethodInfo main ||
            main.DeclaringType != typeof(SyncedPed) || main.Name != "get_MainPed" ||
            !(bones[0] < project[0] && project[0] < draw[0])) return false;
        int first = bones[0] - 2, last = draw[0];
        var inside = new HashSet<Label>(codes.Skip(first + 1).Take(last - first).SelectMany(code => code.labels));
        for (int index = 0; index < codes.Count; index++)
        {
            bool within = index >= first && index <= last;
            if (codes[index].operand is Label jump && inside.Contains(jump) && !within) return false;
            if (within && codes[index].operand is Label exit && !inside.Contains(exit) &&
                !codes.Skip(last + 1).Any(code => code.labels.Contains(exit))) return false;
        }
        start = first;
        end = last;
        return true;
    }

    internal static IEnumerable<CodeInstruction> ReplaceDraw(IEnumerable<CodeInstruction> original)
    {
        List<CodeInstruction> codes = original.Select(code => new CodeInstruction(code)).ToList();
        if (!TryFindDrawBlock(codes, out int start, out int end)) throw new InvalidOperationException("Unsupported RageCoop name tag drawing.");
        var load = new CodeInstruction(OpCodes.Ldarg_0);
        load.labels.AddRange(codes[start].labels);
        load.blocks.AddRange(codes[start].blocks);
        codes.RemoveRange(start, end - start + 1);
        codes.InsertRange(start, new[] { load, new CodeInstruction(OpCodes.Call, typeof(EngineNameTagGuard).GetMethod(nameof(DrawNameTag), Static)!) });
        return codes;
    }

    private static void DrawNameTag(SyncedPed ped)
    {
        Player owner = _readOwner!(ped);
        Vector3 anchor = ped.MainPed.Bones[Bone.IKHead].Position + new Vector3(0f, 0f, HeadClearance);
        if (!Finite.Is(anchor)) return;
        DrawLabel(anchor, owner.Username ?? string.Empty, owner.HasDirectConnection);
    }

    // Same font, size, outline and colours as RageCoop; the text's bottom edge
    // sits at the anchor whatever the screen resolution or aspect ratio.
    internal static void DrawLabel(Vector3 anchor, string text, bool direct)
    {
        Function.Call(Hash.SET_DRAW_ORIGIN, anchor.X, anchor.Y, anchor.Z, 0);
        Function.Call(Hash.SET_TEXT_FONT, (int)GTA.UI.Font.ChaletLondon);
        Function.Call(Hash.SET_TEXT_SCALE, 0.4f, 0.4f);
        if (direct) Function.Call(Hash.SET_TEXT_COLOUR, 179, 229, 252, 255);
        else Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 255);
        Function.Call(Hash.SET_TEXT_OUTLINE);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0f, -0.03f, 0);
        Function.Call(Hash.CLEAR_DRAW_ORIGIN);
    }

    private static int[] Indexes(IList<CodeInstruction> codes, Func<MethodInfo, bool> match) =>
        Enumerable.Range(0, codes.Count).Where(index => codes[index].operand is MethodInfo method && match(method)).ToArray();
}
