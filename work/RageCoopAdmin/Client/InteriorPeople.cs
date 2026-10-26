using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using GTA;
using GTA.Math;
using GTA.Native;

namespace RageCoopAdmin.Client;

// человек из интерьера Online (InteriorPedData): место, модель, одежда, анимация или сценарий
internal sealed class InteriorPed
{
    internal InteriorPed(uint model, float x, float y, float z, float heading, string animations, string clip, string scenario, string look, string held, int flags)
    {
        Model = model;
        Position = new Vector3(x, y, z);
        Heading = heading;
        Animations = animations;
        Clip = clip;
        Scenario = scenario;
        Look = look;
        Held = held;
        Flags = flags;
    }

    internal uint Model { get; }
    internal Vector3 Position { get; }
    internal float Heading { get; }
    internal string Animations { get; }
    internal string Clip { get; }
    internal string Scenario { get; }
    // drawable/texture для частей 0-11, прочерк оставляет свою, дальше slot:drawable/texture для пропов
    internal string Look { get; }
    // model@bone@x,y,z@pitch,roll,yaw через точку с запятой, что в руках
    internal string Held { get; }
    // флаги анимации из скрипта этого человека, всегда с циклом
    internal int Flags { get; }
}

internal sealed class InteriorPedSet
{
    internal InteriorPedSet(string interior, InteriorPed[] people)
    {
        Interior = interior;
        People = people;
    }

    internal string Interior { get; }
    internal InteriorPed[] People { get; }
}

internal readonly struct PedPart
{
    internal PedPart(int slot, int drawable, int texture)
    {
        Slot = slot;
        Drawable = drawable;
        Texture = texture;
    }

    internal int Slot { get; }
    internal int Drawable { get; }
    internal int Texture { get; }
}

internal readonly struct HeldItem
{
    internal HeldItem(uint model, int bone, Vector3 offset, Vector3 rotation)
    {
        Model = model;
        Bone = bone;
        Offset = offset;
        Rotation = rotation;
    }

    internal uint Model { get; }
    internal int Bone { get; }
    internal Vector3 Offset { get; }
    internal Vector3 Rotation { get; }
}

internal static class InteriorPedLook
{
    internal const int Slots = 12;
    internal const int PropSlots = 9;

    internal static bool TryParse(string look, out PedPart[] clothes, out PedPart[] props)
    {
        clothes = Array.Empty<PedPart>();
        props = Array.Empty<PedPart>();
        if (look.Length == 0) return true;
        string[] parts = look.Split(' ');
        if (parts.Length < Slots) return false;
        var worn = new List<PedPart>();
        var extra = new List<PedPart>();
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            if (index < Slots)
            {
                if (part == "-") continue;
                if (!TryPair(part, out int drawable, out int texture)) return false;
                worn.Add(new PedPart(index, drawable, texture));
                continue;
            }

            int colon = part.IndexOf(':');
            if (colon <= 0 || !int.TryParse(part.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int slot) ||
                slot >= PropSlots || !TryPair(part.Substring(colon + 1), out int prop, out int tint)) return false;
            extra.Add(new PedPart(slot, prop, tint));
        }

        clothes = worn.ToArray();
        props = extra.ToArray();
        return true;
    }

    internal static bool TryParseHeld(string held, out HeldItem[] items)
    {
        items = Array.Empty<HeldItem>();
        if (held.Length == 0) return true;
        var found = new List<HeldItem>();
        foreach (string entry in held.Split(';'))
        {
            string[] fields = entry.Split('@');
            if (fields.Length != 4 || !uint.TryParse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint model) || model == 0 ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int bone) ||
                !TryVector(fields[2], out Vector3 offset) || !TryVector(fields[3], out Vector3 rotation)) return false;
            found.Add(new HeldItem(model, bone, offset, rotation));
        }

        items = found.ToArray();
        return true;
    }

    private static bool TryPair(string text, out int first, out int second)
    {
        second = 0;
        int slash = text.IndexOf('/');
        return int.TryParse(slash > 0 ? text.Substring(0, slash) : string.Empty, NumberStyles.None, CultureInfo.InvariantCulture, out first) &&
            int.TryParse(text.Substring(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out second) && first < 256 && second < 256;
    }

    private static bool TryVector(string text, out Vector3 value)
    {
        value = default;
        string[] parts = text.Split(',');
        if (parts.Length != 3) return false;
        var numbers = new float[3];
        for (int index = 0; index < 3; index++)
        {
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]) ||
                float.IsNaN(numbers[index]) || float.IsInfinity(numbers[index])) return false;
        }

        value = new Vector3(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}

// люди Online в наших интерьерах, пока игрок внутри
// каждая игра ставит своих на те же места и не шлёт их по сети (LocalPeds)
internal sealed class InteriorPeople
{
    internal const int CreatePerFrame = 2;
    internal const long LeaveDelay = 2000;
    internal const long LoadLimit = 10000;
    internal const long CheckEvery = 1000;
    internal const float NearDistance = 200f;
    // тусовка под открытым небом видна, пока игрок ближе этого к её середине
    internal const float OutdoorDistance = 150f;
    // пляжная вечеринка Online на Кайо-Перико (am_mp_peds), есть, пока загружен остров
    internal const string CayoBeachParty = "cayo_beach_party";

    private readonly ShownSet[] _sets;
    private readonly Action<string>? _log;
    private readonly Func<string, bool> _outdoorReady;

    // набор с ключом не из каталога интерьеров стоит на улице, outdoorReady говорит, загружено ли там место
    internal InteriorPeople(IEnumerable<InteriorPedSet> sets, IEnumerable<OnlineInterior> interiors, Action<string>? log = null,
        Func<string, bool>? outdoorReady = null)
    {
        _log = log;
        _outdoorReady = outdoorReady ?? (_ => false);
        Dictionary<string, OnlineInterior> byKey = interiors.ToDictionary(interior => interior.Key, StringComparer.Ordinal);
        _sets = sets.Select(set => new ShownSet(set, byKey.TryGetValue(set.Interior, out OnlineInterior? interior) ? interior : null)).ToArray();
    }

    internal void Tick(long now)
    {
        int player = InteriorPeopleNatives.PlayerPed();
        if (player == 0) return;
        int inside = InteriorPeopleNatives.InteriorOf(player);
        Vector3 at = InteriorPeopleNatives.Position(player);
        int budget = CreatePerFrame;
        foreach (ShownSet set in _sets)
        {
            if (set.Interior == null ? IsNear(set, at) : inside != 0 && IsIn(set, inside, at, now))
            {
                set.LeftAt = 0;
                if (!set.Active)
                {
                    set.Active = true;
                    set.Reported = false;
                }

                Advance(set, now, ref budget);
            }
            else if (set.Active)
            {
                if (set.LeftAt == 0) set.LeftAt = now;
                else if (now - set.LeftAt >= LeaveDelay) Remove(set);
            }
        }
    }

    internal void Reset()
    {
        foreach (ShownSet set in _sets)
        {
            if (set.Active) Remove(set);
            set.Interiors.Clear();
            set.ResolvedAt = -CheckEvery;
        }
    }

    private bool IsNear(ShownSet set, Vector3 at) =>
        at.DistanceToSquared(set.Centre) <= OutdoorDistance * OutdoorDistance && _outdoorReady(set.Key);

    // номер интерьера игра даёт, когда комнаты загружены, у некоторых интерьеров их несколько
    private static bool IsIn(ShownSet set, int inside, Vector3 at, long now)
    {
        OnlineInterior interior = set.Interior!;
        if (!set.Interiors.Contains(inside))
        {
            if (at.DistanceToSquared(set.Centre) > NearDistance * NearDistance || now - set.ResolvedAt < CheckEvery) return false;
            set.ResolvedAt = now;
            int own = InteriorNatives.InteriorAt(interior.X, interior.Y, interior.Z);
            if (own != 0) set.Interiors.Add(own);
            foreach (ShownPerson person in set.People)
            {
                Vector3 place = person.Data.Position;
                int id = InteriorNatives.InteriorAt(place.X, place.Y, place.Z);
                if (id != 0) set.Interiors.Add(id);
            }

            if (!set.Interiors.Contains(inside)) return false;
        }

        return InteriorNatives.IsReady(inside);
    }

    private void Advance(ShownSet set, long now, ref int budget)
    {
        bool check = now - set.CheckedAt >= CheckEvery;
        if (check) set.CheckedAt = now;
        bool waiting = false;
        foreach (ShownPerson person in set.People)
        {
            if (person.Failed) continue;
            if (person.Ped != 0)
            {
                if (check) Keep(person);
                continue;
            }

            if (person.RequestedAt == 0) person.RequestedAt = now;
            if (!Ready(set, person, now))
            {
                if (now - person.RequestedAt > LoadLimit) Fail(set, person, "the model did not load");
                else waiting = true;
                continue;
            }

            if (budget == 0)
            {
                waiting = true;
                continue;
            }

            budget--;
            Place(set, person);
        }

        if (!waiting && !set.Reported) Report(set);
    }

    private static bool Ready(ShownSet set, ShownPerson person, long now)
    {
        bool ready = InteriorPeopleNatives.LoadModel(person.Data.Model);
        set.Models.Add(person.Data.Model);
        if (person.Data.Animations.Length > 0 && !person.NoAnimation)
        {
            if (!InteriorPeopleNatives.AnimationsExist(person.Data.Animations)) person.NoAnimation = true;
            else if (!InteriorPeopleNatives.LoadAnimations(person.Data.Animations))
            {
                set.Animations.Add(person.Data.Animations);
                if (now - person.RequestedAt <= LoadLimit) ready = false;
                else person.NoAnimation = true;
            }
            else set.Animations.Add(person.Data.Animations);
        }

        foreach (HeldItem item in person.Items)
        {
            set.Models.Add(item.Model);
            if (!InteriorPeopleNatives.LoadModel(item.Model) && now - person.RequestedAt <= LoadLimit) ready = false;
        }

        return ready;
    }

    private void Place(ShownSet set, ShownPerson person)
    {
        InteriorPed data = person.Data;
        int ped = InteriorPeopleNatives.CreatePed(data.Model, data.Position, data.Heading);
        if (ped == 0)
        {
            Fail(set, person, "the game did not create it");
            return;
        }

        LocalPeds.Add(ped);
        person.Ped = ped;
        InteriorPeopleNatives.Dress(ped, person.Clothes, person.Props);
        Animate(person);
        foreach (HeldItem item in person.Items)
        {
            int thing = InteriorPeopleNatives.Hold(ped, item);
            if (thing != 0) person.Things.Add(thing);
        }
    }

    private static void Animate(ShownPerson person)
    {
        InteriorPed data = person.Data;
        if (data.Scenario.Length > 0) InteriorPeopleNatives.StartScenario(person.Ped, data.Scenario);
        else if (!person.NoAnimation && data.Animations.Length > 0)
            InteriorPeopleNatives.PlayAnimation(person.Ped, data.Animations, data.Clip, data.Position, data.Heading, person.Phase, data.Flags);
    }

    // игра иногда убирает людей, тогда ставим заново, остановившуюся анимацию запускаем снова
    private static void Keep(ShownPerson person)
    {
        if (!InteriorPeopleNatives.Exists(person.Ped))
        {
            LocalPeds.Remove(person.Ped);
            DeleteThings(person);
            person.Ped = 0;
            person.RequestedAt = 0;
            return;
        }

        InteriorPed data = person.Data;
        bool busy = data.Scenario.Length > 0 ? InteriorPeopleNatives.UsesScenario(person.Ped) :
            person.NoAnimation || data.Animations.Length == 0 || InteriorPeopleNatives.IsPlaying(person.Ped, data.Animations, data.Clip);
        if (!busy) Animate(person);
    }

    private void Fail(ShownSet set, ShownPerson person, string reason)
    {
        person.Failed = true;
        _log?.Invoke("OG Coop: interior person in " + set.Key + " skipped (" + person.Data.Model.ToString("X8", CultureInfo.InvariantCulture) + "): " + reason + ".");
    }

    private void Report(ShownSet set)
    {
        set.Reported = true;
        int placed = set.People.Count(person => person.Ped != 0);
        int still = set.People.Count(person => person.Ped != 0 && person.NoAnimation && person.Data.Scenario.Length == 0);
        _log?.Invoke("OG Coop: interior people in " + set.Key + ": " + placed + " of " + set.People.Length +
            (still > 0 ? ", " + still + " without animation (" + string.Join(", ", set.People.Where(person => person.NoAnimation)
                .Select(person => person.Data.Animations).Distinct().Take(3)) + ")" : string.Empty) + ".");
    }

    private void Remove(ShownSet set)
    {
        foreach (ShownPerson person in set.People)
        {
            if (person.Ped != 0)
            {
                LocalPeds.Remove(person.Ped);
                InteriorPeopleNatives.Delete(person.Ped);
            }

            DeleteThings(person);
            person.Ped = 0;
            person.RequestedAt = 0;
            person.Failed = false;
            person.NoAnimation = false;
        }

        foreach (uint model in set.Models) InteriorPeopleNatives.ReleaseModel(model);
        foreach (string animations in set.Animations) InteriorPeopleNatives.ReleaseAnimations(animations);
        set.Models.Clear();
        set.Animations.Clear();
        set.Active = false;
        set.LeftAt = 0;
        _log?.Invoke("OG Coop: interior people in " + set.Key + " removed.");
    }

    private static void DeleteThings(ShownPerson person)
    {
        foreach (int thing in person.Things) InteriorPeopleNatives.Delete(thing);
        person.Things.Clear();
    }

    private sealed class ShownSet
    {
        internal ShownSet(InteriorPedSet set, OnlineInterior? interior)
        {
            Key = set.Interior;
            Interior = interior;
            People = set.People.Select((data, index) => new ShownPerson(data, index)).ToArray();
            Centre = People.Length == 0 ? (interior == null ? Vector3.Zero : new Vector3(interior.X, interior.Y, interior.Z)) :
                new Vector3(People.Average(person => person.Data.Position.X), People.Average(person => person.Data.Position.Y),
                    People.Average(person => person.Data.Position.Z));
        }

        internal string Key { get; }
        // null у тусовки под открытым небом
        internal OnlineInterior? Interior { get; }
        internal ShownPerson[] People { get; }
        internal Vector3 Centre { get; }
        internal HashSet<int> Interiors { get; } = new HashSet<int>();
        internal HashSet<uint> Models { get; } = new HashSet<uint>();
        internal HashSet<string> Animations { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal bool Active { get; set; }
        internal bool Reported { get; set; }
        internal long LeftAt { get; set; }
        internal long ResolvedAt { get; set; } = -CheckEvery;
        internal long CheckedAt { get; set; }
    }

    private sealed class ShownPerson
    {
        internal ShownPerson(InteriorPed data, int index)
        {
            Data = data;
            // кривые данные ловят проверки, здесь теряется только одежда или предмет
            if (!InteriorPedLook.TryParse(data.Look, out PedPart[] clothes, out PedPart[] props))
            {
                clothes = Array.Empty<PedPart>();
                props = Array.Empty<PedPart>();
            }

            Clothes = clothes;
            Props = props;
            Items = InteriorPedLook.TryParseHeld(data.Held, out HeldItem[] items) ? items : Array.Empty<HeldItem>();
            // разный старт цикла, чтобы соседи не двигались синхронно
            Phase = index * 0.37f % 0.5f;
        }

        internal InteriorPed Data { get; }
        internal PedPart[] Clothes { get; }
        internal PedPart[] Props { get; }
        internal HeldItem[] Items { get; }
        internal float Phase { get; }
        internal int Ped { get; set; }
        internal List<int> Things { get; } = new List<int>();
        internal long RequestedAt { get; set; }
        internal bool Failed { get; set; }
        internal bool NoAnimation { get; set; }
    }
}

// вызовы игры, проверки их подменяют
internal static class InteriorPeopleNatives
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int PlayerPed() => Function.Call<int>(Hash.PLAYER_PED_ID);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int InteriorOf(int entity) => Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, entity);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Vector3 Position(int entity) => Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, entity, false);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool Exists(int entity) => entity != 0 && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, entity);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool LoadModel(uint model)
    {
        int hash = unchecked((int)model);
        if (Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash)) return true;
        if (Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, hash)) Function.Call(Hash.REQUEST_MODEL, hash);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ReleaseModel(uint model) => Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, unchecked((int)model));

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool AnimationsExist(string animations) => Function.Call<bool>(Hash.DOES_ANIM_DICT_EXIST, animations);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool LoadAnimations(string animations)
    {
        if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, animations)) return true;
        Function.Call(Hash.REQUEST_ANIM_DICT, animations);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ReleaseAnimations(string animations) => Function.Call(Hash.REMOVE_ANIM_DICT, animations);

    // как в скриптах Online: стоит на месте, неуязвим, ни на что не реагирует, не цель
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int CreatePed(uint model, Vector3 at, float heading)
    {
        int hash = unchecked((int)model);
        if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash)) return 0;
        int ped = Function.Call<int>(Hash.CREATE_PED, 26, hash, at.X, at.Y, at.Z, heading, false, false);
        if (ped == 0) return 0;
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, ped, at.X, at.Y, at.Z, false, false, false);
        Function.Call(Hash.SET_ENTITY_HEADING, ped, heading);
        Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, ped, false);
        Function.Call(Hash.SET_PED_AS_ENEMY, ped, false);
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 185, true);
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 108, true);
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 106, true);
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 208, true);
        Function.Call(Hash.SET_PED_CAN_EVASIVE_DIVE, ped, false);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, ped, false);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped, false);
        Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped, false);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, true);
        return ped;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Dress(int ped, PedPart[] clothes, PedPart[] props)
    {
        if (clothes.Length == 0) Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, ped);
        foreach (PedPart part in clothes) Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped, part.Slot, part.Drawable, part.Texture, 0);
        foreach (PedPart prop in props) Function.Call(Hash.SET_PED_PROP_INDEX, ped, prop.Slot, prop.Drawable, prop.Texture, false, 1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void PlayAnimation(int ped, string animations, string clip, Vector3 at, float heading, float phase, int flags) =>
        Function.Call(Hash.TASK_PLAY_ANIM_ADVANCED, ped, animations, clip, at.X, at.Y, at.Z, 0f, 0f, heading, 8f, -8f, -1,
            flags, phase, 2, 0);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsPlaying(int ped, string animations, string clip) => Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ped, animations, clip, 3);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void StartScenario(int ped, string scenario) => Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped, scenario, 0, true);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool UsesScenario(int ped) => Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, ped);

    // бокал, телефон или сигарета на кости руки, как крепит скрипт
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Hold(int ped, HeldItem item)
    {
        int hash = unchecked((int)item.Model);
        if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash)) return 0;
        Vector3 at = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, ped, false);
        int thing = Function.Call<int>(Hash.CREATE_OBJECT, hash, at.X, at.Y, at.Z, false, false, false);
        if (thing == 0) return 0;
        int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, ped, item.Bone);
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, thing, ped, bone, item.Offset.X, item.Offset.Y, item.Offset.Z,
            item.Rotation.X, item.Rotation.Y, item.Rotation.Z, true, true, false, false, 2, true, false);
        return thing;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Delete(int entity)
    {
        Entity? found = Entity.FromHandle(entity);
        if (found != null && found.Exists()) found.Delete();
    }
}
