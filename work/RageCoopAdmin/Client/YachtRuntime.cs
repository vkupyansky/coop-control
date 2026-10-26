using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OGCoop.Shared;
using GTA;
using GTA.Math;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Puts the yachts of the players on the server on this game's map. A berth's
// hull and interior are map parts; GTA streams the hull in only near it, and
// then the upper decks, rails, lights, door, flags and the captain on the
// bridge go on it. When the hull streams out they are taken off and come back
// with it. Game thread only.
internal sealed class YachtRuntime
{
    internal const string HullModel = "apa_mp_apa_yacht";
    internal const string CaptainModel = "mp_m_boatstaff_01";
    internal const string CaptainAnimations = "ANIM@AMB@YACHT@CAPTAIN@";
    // The hull's centre from the yacht script's berth point, in the berth's
    // own axes (the same for all 36 berths), and how far to look for it.
    internal static readonly Vector2 HullFromPoint = new Vector2(19.47f, -2.01f);
    internal const float HullZ = 5.43f;
    internal const float HullSearch = 60f;
    // Where the objects go on the hull (HKH191's Working Galaxy Super Yacht).
    internal static readonly Vector3 DeckOffset = new Vector3(0f, 0f, 14.5f);
    internal static readonly Vector3 DoorOffset = new Vector3(-36.8f, -2.7f, 0.6f);
    internal static readonly Vector3 JacuzziOffset = new Vector3(-51f, -2f, 0.3f);
    internal static readonly Vector3[] FlagOffsets = { new Vector3(-3f, -0.45f, 9.8f), new Vector3(-3f, -3.55f, 9.8f), new Vector3(-56.5f, -2f, 0.5f) };
    // The captain at the bridge's controls, facing the bow; players arrive on
    // the aft deck.
    internal static readonly Vector3 CaptainOffset = new Vector3(12.4f, -2.03f, 6.5f);
    internal static readonly Vector3 DeckArrival = new Vector3(-30f, -2f, 1f);

    private readonly Dictionary<string, ShownYacht> _shown = new Dictionary<string, ShownYacht>(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _log;
    private int _revision = int.MinValue;

    internal YachtRuntime(Action<string>? log = null) => _log = log;

    internal bool HasState => _shown.Count > 0;

    // A yacht moved to another berth: owner, old berth, new berth.
    internal event Action<string, int, int>? Moved;

    internal void Tick(YachtLooks looks)
    {
        if (looks.Revision != _revision)
        {
            _revision = looks.Revision;
            Sync(looks);
        }
        foreach (ShownYacht yacht in _shown.Values) Advance(yacht);
    }

    internal void Reset()
    {
        foreach (ShownYacht yacht in _shown.Values) Remove(yacht, keepMap: false);
        _shown.Clear();
        _revision = int.MinValue;
    }

    // Where a player goes on the owner's yacht: its aft deck once the hull is
    // in, otherwise the berth's point; null without that yacht.
    internal Vector3? Arrival(string owner)
    {
        if (!_shown.TryGetValue(owner, out ShownYacht? yacht)) return null;
        if (yacht.Hull != 0 && YachtNatives.Exists(yacht.Hull)) return YachtNatives.Offset(yacht.Hull, DeckArrival);
        return new Vector3(yacht.Berth.X, yacht.Berth.Y, YachtBerth.Z);
    }

    internal bool HullIn(string owner) => _shown.TryGetValue(owner, out ShownYacht? yacht) && yacht.Hull != 0;

    internal int Captain(string owner) => _shown.TryGetValue(owner, out ShownYacht? yacht) ? yacht.Captain : 0;

    // Where the hull stands, from the berth's point and heading.
    internal static Vector3 HullPosition(YachtBerth berth)
    {
        double angle = berth.Heading * Math.PI / 180.0;
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        return new Vector3(berth.X + HullFromPoint.X * cos - HullFromPoint.Y * sin, berth.Y + HullFromPoint.X * sin + HullFromPoint.Y * cos, HullZ);
    }

    private void Sync(YachtLooks looks)
    {
        var wanted = new Dictionary<string, YachtLook>(StringComparer.OrdinalIgnoreCase);
        foreach ((string owner, YachtLook look) in looks.All()) wanted[owner] = look;
        foreach (string owner in _shown.Keys.ToArray())
        {
            ShownYacht yacht = _shown[owner];
            bool stays = wanted.TryGetValue(owner, out YachtLook? look);
            if (stays && YachtCodec.Same(look, yacht.Look)) continue;
            bool sameBerth = stays && look!.Berth == yacht.Look.Berth;
            Remove(yacht, keepMap: sameBerth);
            _shown.Remove(owner);
            if (stays && !sameBerth) Moved?.Invoke(owner, yacht.Look.Berth, look!.Berth);
        }

        foreach (KeyValuePair<string, YachtLook> item in wanted)
        {
            if (_shown.ContainsKey(item.Key)) continue;
            var yacht = new ShownYacht(item.Key, YachtCodec.Copy(item.Value));
            YachtNatives.RequestIpl(yacht.Berth.Hull);
            YachtNatives.RequestIpl(yacht.Berth.Interior);
            _shown[item.Key] = yacht;
        }
    }

    private void Advance(ShownYacht yacht)
    {
        if (yacht.Hull != 0 && !YachtNatives.Exists(yacht.Hull))
        {
            // The hull streamed out: what stood on it goes too.
            DeleteObjects(yacht);
            yacht.Hull = 0;
        }

        if (yacht.Hull == 0)
        {
            Vector3 at = HullPosition(yacht.Berth);
            yacht.Hull = YachtNatives.FindObject(at, HullSearch, HullModel);
            if (yacht.Hull == 0) return;
        }

        if (!yacht.Built)
        {
            if (!yacht.Models.All(YachtNatives.LoadModel)) return;
            Build(yacht);
        }

        if (yacht.Captain != 0 && !yacht.Idle) yacht.Idle = YachtNatives.PlayCaptainIdle(yacht.Captain);
    }

    private void Build(ShownYacht yacht)
    {
        yacht.Built = true;
        int hull = yacht.Hull;
        YachtLook look = yacht.Look;
        YachtNatives.Tint(hull, look.Color);
        foreach ((string model, YachtPart kind) in Yachts.Parts(look))
        {
            int item = YachtNatives.CreateObject(model, YachtNatives.Offset(hull, Vector3.Zero));
            if (item == 0) continue;
            yacht.Objects.Add(item);
            (Vector3 offset, float turn) = kind switch
            {
                YachtPart.Door => (DoorOffset, 90f),
                YachtPart.Jacuzzi => (JacuzziOffset, 0f),
                _ => (DeckOffset, 0f),
            };
            YachtNatives.Attach(item, hull, offset, turn);
            if (kind is YachtPart.Colored or YachtPart.Door) YachtNatives.Tint(item, look.Color);
            else if (kind == YachtPart.Rails) YachtNatives.Tint(item, 0);
        }

        if (look.Flag > 0)
        {
            string flag = Yachts.Flags[look.Flag - 1].Model;
            foreach (Vector3 offset in FlagOffsets)
            {
                int item = YachtNatives.CreateObject(flag, YachtNatives.Offset(hull, offset));
                if (item == 0) continue;
                YachtNatives.Lean(item, -50f, YachtNatives.Heading(hull));
                yacht.Objects.Add(item);
            }
        }

        // The captain faces the bow (the hull's +X).
        // капитан у каждой игры свой, по сети не уходит
        yacht.Captain = YachtNatives.CreateCaptain(YachtNatives.Offset(hull, CaptainOffset), YachtNatives.Heading(hull) - 90f);
        LocalPeds.Add(yacht.Captain);
        yacht.Idle = false;
        _log?.Invoke("OG Coop: yacht of " + (yacht.Owner.Length == 0 ? "this player" : yacht.Owner) + " at berth " + yacht.Look.Berth +
            ": " + yacht.Objects.Count + " objects" + (yacht.Captain != 0 ? ", captain" : string.Empty) + ".");
    }

    private void DeleteObjects(ShownYacht yacht)
    {
        foreach (int item in yacht.Objects) YachtNatives.DeleteObject(item);
        yacht.Objects.Clear();
        DeleteCaptain(yacht);
        yacht.Idle = false;
        yacht.Built = false;
    }

    private static void Remove(ShownYacht yacht, bool keepMap)
    {
        foreach (int item in yacht.Objects) YachtNatives.DeleteObject(item);
        yacht.Objects.Clear();
        DeleteCaptain(yacht);
        foreach (string model in yacht.Models) YachtNatives.ReleaseModel(model);
        if (keepMap) return;
        YachtNatives.RemoveIpl(yacht.Berth.Hull);
        YachtNatives.RemoveIpl(yacht.Berth.Interior);
    }

    private static void DeleteCaptain(ShownYacht yacht)
    {
        if (yacht.Captain == 0) return;
        LocalPeds.Remove(yacht.Captain);
        YachtNatives.DeletePed(yacht.Captain);
        yacht.Captain = 0;
    }

    private sealed class ShownYacht
    {
        internal ShownYacht(string owner, YachtLook look)
        {
            Owner = owner;
            Look = look;
            Berth = Yachts.Berths[look.Berth];
            var models = Yachts.Parts(look).Select(part => part.Model).ToList();
            if (look.Flag > 0) models.Add(Yachts.Flags[look.Flag - 1].Model);
            models.Add(CaptainModel);
            Models = models.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal string Owner { get; }
        internal YachtLook Look { get; }
        internal YachtBerth Berth { get; }
        internal string[] Models { get; }
        internal int Hull { get; set; }
        internal bool Built { get; set; }
        internal List<int> Objects { get; } = new List<int>();
        internal int Captain { get; set; }
        internal bool Idle { get; set; }
    }
}

// GTA boundary of the yachts; isolated checks replace these calls.
internal static class YachtNatives
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void RequestIpl(string name) => Function.Call((Hash)0x41B4893843BBDB74UL, name); // REQUEST_IPL
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void RemoveIpl(string name) => Function.Call((Hash)0xEE6C5AD3ECE0A82DUL, name); // REMOVE_IPL

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int FindObject(Vector3 at, float radius, string model) =>
        Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE, at.X, at.Y, at.Z, radius, Joaat(model), false, false, false);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool Exists(int entity) => entity != 0 && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, entity);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Vector3 Offset(int entity, Vector3 offset) =>
        Function.Call<Vector3>(Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, entity, offset.X, offset.Y, offset.Z);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static float Heading(int entity) => Function.Call<float>(Hash.GET_ENTITY_HEADING, entity);

    // Models load over a few frames; true once in memory.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool LoadModel(string model)
    {
        int hash = Joaat(model);
        if (Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash)) return true;
        if (Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, hash)) Function.Call(Hash.REQUEST_MODEL, hash);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ReleaseModel(string model) => Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, Joaat(model));

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int CreateObject(string model, Vector3 at)
    {
        int hash = Joaat(model);
        if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash)) return 0;
        int item = Function.Call<int>(Hash.CREATE_OBJECT_NO_OFFSET, hash, at.X, at.Y, at.Z, false, false, false);
        if (item != 0) Function.Call(Hash.FREEZE_ENTITY_POSITION, item, true);
        return item;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Attach(int item, int hull, Vector3 offset, float turn) =>
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, item, hull, 0, offset.X, offset.Y, offset.Z, 0f, 0f, turn, false, false, true, false, 2, true);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Tint(int item, int tint) => Function.Call((Hash)0x971DA0055324D033UL, item, tint); // SET_OBJECT_TINT_INDEX

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Lean(int item, float pitch, float heading) => Function.Call(Hash.SET_ENTITY_ROTATION, item, 0f, pitch, heading, 2, true);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void DeleteObject(int item) => Delete(item);

    private static void Delete(int entity)
    {
        Entity? found = Entity.FromHandle(entity);
        if (found != null && found.Exists()) found.Delete();
    }

    // The yacht script's captain: in uniform with a cap, still and unharmed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int CreateCaptain(Vector3 at, float heading)
    {
        int hash = Joaat(YachtRuntime.CaptainModel);
        if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash)) return 0;
        int ped = Function.Call<int>(Hash.CREATE_PED, 4, hash, at.X, at.Y, at.Z, heading, false, false);
        if (ped == 0) return 0;
        Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, ped, false);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, true);
        Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, ped);
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped, false);
        Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped, false);
        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped, 4, 0, 1, 0);
        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped, 3, 0, 0, 0);
        Function.Call(Hash.SET_PED_PROP_INDEX, ped, 0, 0, 0, false, 1);
        Function.Call(Hash.REQUEST_ANIM_DICT, YachtRuntime.CaptainAnimations);
        return ped;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool PlayCaptainIdle(int ped)
    {
        if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, YachtRuntime.CaptainAnimations)) return false;
        Function.Call(Hash.TASK_PLAY_ANIM, ped, YachtRuntime.CaptainAnimations, "idle", 1000f, -2f, -1, 1, 0f, false, false, false);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void DeletePed(int ped) => Delete(ped);

    private static int Joaat(string value) => unchecked((int)StringHash.AtStringHash(value));
}
