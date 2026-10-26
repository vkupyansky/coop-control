using System;
using System.Runtime.CompilerServices;
using OGCoop.Shared;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Story scripts know which hero the player is only by the player's body: they
// compare it with each hero's model in the game's character table, and any
// other body is "no hero". An Online character then found its own house
// locked, and Los Santos Customs and the shops saw no money (they spend the
// current hero's cash). While the player wears an Online body, the entry of
// the hero they came from holds that body instead, so every story script
// treats the character as that hero: house, money, shops, garages. The entry
// returns to the hero's model as soon as the player is that hero again.
//
// The table is GTA V Legacy 1.0.3889's Global_2349 (29 fields per character,
// field 0 the model; decompiled scripts 1.73-3889). Before any write the
// first three entries must be Michael, Franklin and Trevor, or one of them an
// Online body we put there; on another game build nothing is written.
// Game thread only.
internal sealed class HeroLink
{
    internal static readonly int[] Heroes =
    {
        225514697,   // player_zero, Michael
        -1692214353, // player_one, Franklin
        -1686040670, // player_two, Trevor
    };

    private readonly Action<string>? _log;
    private bool _unsupported;
    private int _linked = -1;

    internal HeroLink(Action<string>? log = null) => _log = log;

    // The hero whose entry holds an Online body: the player became an Online
    // character from that hero, possibly before this resource started.
    internal int LinkedHero()
    {
        int[]? table = ReadTable();
        if (table == null) return -1;
        return Array.FindIndex(table, CharacterModels.IsOnline);
    }

    // hero: the player's story hero (its model); body: the player's model now.
    internal void Sync(int heroModel, int body)
    {
        int[]? table = ReadTable();
        if (table == null) return;
        int hero = Array.IndexOf(Heroes, heroModel);
        if (hero < 0) hero = Array.FindIndex(table, CharacterModels.IsOnline);
        if (hero < 0) hero = 1;
        bool online = CharacterModels.IsOnline(body);
        for (int index = 0; index < Heroes.Length; index++)
        {
            int wanted = online && index == hero ? body : Heroes[index];
            if (table[index] != wanted) HeroLinkNatives.WriteModel(index, wanted);
        }
        int linked = online ? hero : -1;
        if (linked == _linked) return;
        _linked = linked;
        _log?.Invoke(linked < 0
            ? "OG Coop: story hero table restored."
            : "OG Coop: the Online character counts as story hero " + linked + " for the game.");
    }

    private int[]? ReadTable()
    {
        if (_unsupported) return null;
        var table = new int[Heroes.Length];
        try
        {
            for (int index = 0; index < Heroes.Length; index++) table[index] = HeroLinkNatives.ReadModel(index);
        }
        catch (Exception error) when (error is IndexOutOfRangeException or InvalidOperationException)
        {
            return Unsupported(error.GetType().Name);
        }
        for (int index = 0; index < Heroes.Length; index++)
            if (table[index] != Heroes[index] && !CharacterModels.IsOnline(table[index])) return Unsupported("unexpected entry " + index);
        return table;
    }

    private int[]? Unsupported(string reason)
    {
        _unsupported = true;
        _log?.Invoke("OG Coop: the game's story hero table is not the supported one (" + reason +
            "); an Online character is not linked to a story hero.");
        return null;
    }
}

// GTA boundary; isolated checks replace these calls.
internal static class HeroLinkNatives
{
    internal const int Table = 2349;
    internal const int Fields = 29;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int ReadModel(int hero) => GlobalVariable.Get(Table).GetArrayItem(hero, Fields).Read<int>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void WriteModel(int hero, int model) => GlobalVariable.Get(Table).GetArrayItem(hero, Fields).Write(model);
}
