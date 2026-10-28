using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GTA.Math;
using GTA.Native;
using HarmonyLib;
using RageCoop.Client;

internal static partial class Program
{
    private static readonly List<(Hash Native, ulong[] Args)> NameTagNatives = new List<(Hash, ulong[])>();

    private static void CheckNameTagComposition()
    {
        Type guard = Resource.GetType("RageCoopAdmin.Client.EngineNameTagGuard", true)!;
        MethodInfo target = (MethodInfo)guard.GetMethod("FindTarget", Static)!.Invoke(null, null)!;
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(target);
        object?[] block = { original, -1, -1 };
        Assert((bool)guard.GetMethod("TryFindDrawBlock", Static)!.Invoke(null, block)!,
            "The installed RageCoop name tag has the supported head-to-pixels drawing block.");
        int start = (int)block[1]!, end = (int)block[2]!;
        var rewritten = ((IEnumerable<CodeInstruction>)guard.GetMethod("ReplaceDraw", Static)!.Invoke(null, new object[] { original })!).ToList();
        Assert(rewritten.Count == original.Count - (end - start + 1) + 2 &&
            Enumerable.Range(0, start).All(index => rewritten[index].opcode == original[index].opcode && Equals(rewritten[index].operand, original[index].operand)) &&
            rewritten[start].labels.SequenceEqual(original[start].labels),
            "Only the drawing block changes; RageCoop's visibility and 40 m range gate stays as installed.");
        Assert(!rewritten.Any(code => code.operand is MethodInfo method && (method.Name == "WorldToScreen" || method.DeclaringType?.Name == "ScaledText")) &&
            rewritten.Any(code => code.operand is MethodInfo method && method.Name == "DrawNameTag"),
            "The pixel conversion and its fixed 100 px lift are gone; the guard draws the name instead.");
        bool rejected = false;
        try { ((IEnumerable<CodeInstruction>)guard.GetMethod("ReplaceDraw", Static)!.Invoke(null, new object[] { new[] { new CodeInstruction(OpCodes.Ret) } })!).ToArray(); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
        Assert(rejected, "An unknown name tag body is left to RageCoop instead of receiving a guessed rewrite.");

        Assert((bool)guard.GetMethod("Install", Static)!.Invoke(null, null)! && (bool)guard.GetMethod("Install", Static)!.Invoke(null, null)! &&
            Harmony.GetPatchInfo(target)?.Transpilers.Count(patch => patch.owner == "friendslegacy.ragecoop.enginenametag") == 1,
            "The name tag guard installs once against the actual method.");
        guard.GetMethod("Uninstall", Static)!.Invoke(null, null);
        Assert(Harmony.GetPatchInfo(target)?.Owners.Contains("friendslegacy.ragecoop.enginenametag") != true, "Unload removes the name tag patch.");

        MethodInfo draw = guard.GetMethod("DrawLabel", Static)!;
        var harmony = new Harmony("friendslegacy.check.nametag-natives");
        harmony.Patch(draw, transpiler: new HarmonyMethod(typeof(Program), nameof(CaptureNameTagNatives)));
        try
        {
            NameTagNatives.Clear(); NameTagStrings.Clear();
            draw.Invoke(null, new object[] { new Vector3(10f, 20f, 31.5f), "Rider", false });
            Hash[] order = NameTagNatives.Select(call => call.Native).ToArray();
            Assert(order.First() == Hash.SET_DRAW_ORIGIN && order.Last() == Hash.CLEAR_DRAW_ORIGIN &&
                order.Contains(Hash.SET_TEXT_CENTRE) && order.Contains(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME),
                "The name is drawn centred inside a world draw origin, which is always cleared.");
            Assert(NameTagStrings.SequenceEqual(new[] { "STRING", "Rider" }), "The player's name is passed as plain text.");
            ulong[] origin = NameTagNatives[0].Args;
            Assert(BitConverter.ToSingle(BitConverter.GetBytes(origin[0]), 0) == 10f && BitConverter.ToSingle(BitConverter.GetBytes(origin[2]), 0) == 31.5f,
                "The origin is the world point above the head, independent of screen resolution and aspect ratio.");
            ulong[] text = NameTagNatives.Single(call => call.Native == Hash.END_TEXT_COMMAND_DISPLAY_TEXT).Args;
            Assert(BitConverter.ToSingle(BitConverter.GetBytes(text[0]), 0) == 0f,
                "The text is horizontally centred on the player rather than offset in pixels.");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        Console.WriteLine("Actual name tag guard passed: gate preserved, pixel conversion replaced by a world-anchored label; GTA drawing isolated.");
    }

    private static IEnumerable<CodeInstruction> CaptureNameTagNatives(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction source in instructions)
        {
            var code = new CodeInstruction(source);
            if (code.operand is MethodInfo method && method.DeclaringType == typeof(Function) && method.Name == nameof(Function.Call))
                code.operand = typeof(Program).GetMethod("RecordNameTagNative" + (method.GetParameters().Length - 1), Static)!;
            // Native strings need the SHVDN script domain, so they stay at the boundary.
            else if (code.operand is MethodInfo conversion && conversion.DeclaringType == typeof(InputArgument) &&
                conversion.Name == "op_Implicit" && conversion.GetParameters()[0].ParameterType == typeof(string))
                code.operand = typeof(Program).GetMethod(nameof(NameTagString), Static)!;
            yield return code;
        }
    }

    private static readonly List<string> NameTagStrings = new List<string>();
    private static InputArgument NameTagString(string value) { NameTagStrings.Add(value); return 0; }
    private static ulong Raw(InputArgument argument) => (ulong)typeof(InputArgument).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(argument)!;
    private static void RecordNameTag(Hash hash, params InputArgument[] args) => NameTagNatives.Add((hash, args.Select(Raw).ToArray()));
    private static void RecordNameTagNative0(Hash hash) => RecordNameTag(hash);
    private static void RecordNameTagNative1(Hash hash, InputArgument a) => RecordNameTag(hash, a);
    private static void RecordNameTagNative2(Hash hash, InputArgument a, InputArgument b) => RecordNameTag(hash, a, b);
    private static void RecordNameTagNative3(Hash hash, InputArgument a, InputArgument b, InputArgument c) => RecordNameTag(hash, a, b, c);
    private static void RecordNameTagNative4(Hash hash, InputArgument a, InputArgument b, InputArgument c, InputArgument d) => RecordNameTag(hash, a, b, c, d);
}
