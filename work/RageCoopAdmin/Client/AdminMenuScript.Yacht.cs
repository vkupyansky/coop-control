using System;
using System.Linq;
using OGCoop.Shared;
using GTA;
using GTA.Input;
using GTA.Math;
using GTA.UI;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// The player's own GTA Online yacht: Teleports → Моя яхта gets one on a free
// berth, moves it (the captain takes it there) and changes how it looks; the
// server keeps it per RageCoop name and everyone on the server sees it. Next
// to the captain on the bridge E opens the berths.
public sealed partial class AdminMenuScript
{
    private const long YachtSaveSpacing = 1_100;
    private const float CaptainReach = 2.5f;
    private const float AboardDistance = 120f;
    private YachtLooks? _yachts;
    private YachtRuntime? _yachtRuntime;
    private NativeMenu? _yachtMenu;
    private NativeItem? _yachtGet;
    private NativeItem? _yachtGo;
    private NativeListItem<string>? _yachtBerth;
    private NativeListItem<string>? _yachtModel;
    private NativeListItem<string>? _yachtColor;
    private NativeListItem<string>? _yachtRails;
    private NativeListItem<string>? _yachtLights;
    private NativeListItem<string>? _yachtFlag;
    private bool _yachtUpdating;
    // The yacht sent to the server and the next one waiting for its answer.
    private YachtLook? _yachtSaving;
    private YachtLook? _yachtNext;
    private long _yachtSentAt;
    private int _yachtMenuRevision = int.MinValue;
    private static readonly int[] YachtBerthChoices = Enumerable.Range(0, YachtCodec.BerthCount).Where(YachtCodec.IsBerth).ToArray();

    private void RegisterYachtEvents()
    {
        _eventSubscriptions.Register(YachtProtocol.State, e => QueueYachtEvent(e, YachtProtocol.State));
        _eventSubscriptions.Register(YachtProtocol.Saved, e => QueueYachtEvent(e, YachtProtocol.Saved));
        _eventSubscriptions.Register(YachtProtocol.Look, e => QueueYachtEvent(e, YachtProtocol.Look));
    }

    private void InitializeYachts()
    {
        _yachts = new YachtLooks((hash, args) => API.SendCustomEvent(hash, args));
        _yachtRuntime = new YachtRuntime(message => Logger.Info(message));
        _yachtRuntime.Moved += OnYachtMoved;
        _yachts.Request();
    }

    private void QueueYachtEvent(CustomEventReceivedArgs e, int kind)
    {
        if (_stopping || e.Args == null) return;
        object[] args = (object[])e.Args.Clone();
        API.QueueAction(() =>
        {
            if (!_active || _stopping || _yachts == null) return;
            if (kind == YachtProtocol.State) _yachts.ApplyState(args);
            else if (kind == YachtProtocol.Look) _yachts.ApplyLook(args);
            else if (args is { Length: 1 } && args[0] is bool saved) FinishYachtSave(saved);
        });
    }

    private void BuildYachtMenu(NativeMenu teleports)
    {
        if (_pool == null) return;
        _yachtMenu = CreateMenu("OG Coop", "Моя яхта");
        UiMenu.Description(_yachtMenu, () => UiText.Get("Своя яхта GTA Online: стоянка и вид. Её видят все на сервере."));
        teleports.AddSubMenu(_yachtMenu);
        _pool.Add(_yachtMenu);
        _yachtMenu.Shown += (_, _) => RefreshYachtMenu();

        _yachtGet = UiMenu.Item(() => UiText.Get("Получить яхту"), () => UiText.Get("Яхта Galaxy Super Yacht на свободной стоянке."));
        _yachtGet.Activated += (_, _) => GetYacht();
        _yachtMenu.Add(_yachtGet);
        _yachtGo = UiMenu.Item(() => UiText.Get("На яхту"), () => UiText.Get("Перенестись на палубу своей яхты."));
        _yachtGo.Activated += (_, _) => TeleportToYacht(string.Empty);
        _yachtMenu.Add(_yachtGo);
        _yachtBerth = UiMenu.List(() => UiText.Get("Стоянка"), () => UiText.Get("Выберите место ←/→ и нажмите Enter: капитан перегонит яхту туда."),
            () => YachtBerthChoices.Select(berth => YachtBerthName(berth)).ToArray());
        _yachtBerth.Activated += (_, _) => MoveYacht(YachtBerthChoices[_yachtBerth.SelectedIndex]);
        _yachtMenu.Add(_yachtBerth);
        _yachtModel = AddYachtRow(() => UiText.Get("Яхта"), () => UiText.Get("Orion, Pisces или Aquaris: разные верхние палубы."),
            () => Yachts.Models, (look, index) => look.Model = index);
        _yachtColor = AddYachtRow(() => UiText.Get("Цвет"), () => UiText.Get("Окраска корпуса и палуб, как в Online."),
            () => Yachts.Colors, (look, index) => look.Color = index);
        _yachtRails = AddYachtRow(() => UiText.Get("Перила"), () => UiText.Get("Хромированные или золотые."),
            () => new[] { UiText.Get("Хромированные"), UiText.Get("Золотые") }, (look, index) => look.GoldRails = index == 1);
        _yachtLights = AddYachtRow(() => UiText.Get("Подсветка"), () => UiText.Get("Огни на палубах."),
            () => new[] { UiText.Get("Нет") }.Concat(Yachts.Lights.SelectMany(style => Yachts.LightColors.Select(colour => style + ", " + UiText.Get(colour)))).ToArray(),
            (look, index) => look.Lighting = index);
        _yachtFlag = AddYachtRow(() => UiText.Get("Флаг"), () => UiText.Get("Флаг на мачте и корме."),
            () => new[] { UiText.Get("Нет") }.Concat(Yachts.Flags.Select(flag => UiText.Get(flag.Name))).ToArray(), (look, index) => look.Flag = index);
    }

    private NativeListItem<string> AddYachtRow(Func<string> title, Func<string> description, Func<string[]> options, Action<YachtLook, int> write)
    {
        NativeListItem<string> item = UiMenu.List(title, description, options);
        item.ItemChanged += (_, e) =>
        {
            if (_yachtUpdating || _yachts?.Own == null) return;
            YachtLook look = YachtCodec.Copy(_yachtNext ?? _yachtSaving ?? _yachts.Own);
            write(look, e.Index);
            SaveYacht(look);
        };
        _yachtMenu!.Add(item);
        return item;
    }

    private static string YachtBerthName(int berth) => UiText.Get(Yachts.Berths[berth].Area) + " " +
        Yachts.Berths[berth].Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The rows show the yacht the server keeps (or the change on its way).
    private void RefreshYachtMenu()
    {
        if (_yachts == null || _yachtMenu == null) return;
        YachtLook? look = _yachtNext ?? _yachtSaving ?? _yachts.Own;
        _yachtUpdating = true;
        try
        {
            if (_yachtGet != null) _yachtGet.Enabled = _yachts.OwnKnown && _yachts.Own == null && _yachtSaving == null;
            foreach (NativeItem? item in new NativeItem?[] { _yachtGo, _yachtBerth, _yachtModel, _yachtColor, _yachtRails, _yachtLights, _yachtFlag })
                if (item != null) item.Enabled = look != null;
            if (look == null) return;
            _yachtBerth!.SelectedIndex = Math.Max(0, Array.IndexOf(YachtBerthChoices, look.Berth));
            _yachtModel!.SelectedIndex = look.Model;
            _yachtColor!.SelectedIndex = look.Color;
            _yachtRails!.SelectedIndex = look.GoldRails ? 1 : 0;
            _yachtLights!.SelectedIndex = look.Lighting;
            _yachtFlag!.SelectedIndex = look.Flag;
        }
        finally { _yachtUpdating = false; }
    }

    private void GetYacht()
    {
        if (_yachts is not { OwnKnown: true, Own: null } || _yachtSaving != null) return;
        int berth = YachtCodec.FreeBerth(0, _yachts.TakenByOther);
        if (berth < 0)
        {
            Notification.PostTicker(UiText.Get("~o~Яхта:~s~ все стоянки заняты."), false);
            return;
        }
        SaveYacht(new YachtLook { Berth = berth });
    }

    private void MoveYacht(int berth)
    {
        if (_yachts?.Own == null) return;
        YachtLook current = _yachtNext ?? _yachtSaving ?? _yachts.Own;
        if (current.Berth == berth) return;
        if (_yachts.TakenByOther(berth))
        {
            Notification.PostTicker(UiText.Get("~o~Яхта:~s~ здесь стоит яхта напарника. Выберите другую стоянку."), false);
            return;
        }
        YachtLook look = YachtCodec.Copy(current);
        look.Berth = berth;
        SaveYacht(look);
        Notification.PostTicker(UiText.Format("~b~Капитан:~s~ перегоняю яхту к стоянке «{0}».", YachtBerthName(berth)), false);
    }

    // One change at a time: the server takes one save a second, so a newer
    // one waits for the answer and goes out after it.
    private void SaveYacht(YachtLook look)
    {
        if (_yachts == null) return;
        if (_yachtSaving != null)
        {
            _yachtNext = look;
            return;
        }
        try { _yachts.Save(look); }
        catch (System.IO.InvalidDataException) { return; }
        _yachtSaving = look;
        _yachtSentAt = NowMilliseconds();
        RefreshYachtMenu();
    }

    private void FinishYachtSave(bool saved)
    {
        YachtLook? sent = _yachtSaving;
        _yachtSaving = null;
        if (sent == null || _yachts == null) return;
        if (saved)
        {
            bool first = _yachts.Own == null;
            _yachts.Saved(sent);
            if (first) Notification.PostTicker(UiText.Format("~g~Яхта ваша.~s~ Стоянка «{0}»: Телепорты → Моя яхта → На яхту.", YachtBerthName(sent.Berth)), false);
        }
        else
        {
            _yachtNext = null;
            Notification.PostTicker(UiText.Get("~o~Яхта:~s~ сервер не сохранил изменение. Возможно, стоянку только что занял напарник."), false);
        }
        RefreshYachtMenu();
    }

    private void TeleportToYacht(string owner)
    {
        Vector3? at = _yachtRuntime?.Arrival(owner);
        if (at is not Vector3 point)
        {
            Notification.PostTicker(UiText.Get("~o~Яхта:~s~ её пока нет на карте."), false);
            return;
        }
        if (!CanTeleportToPlace(true, out _)) return;
        _pool?.HideAll();
        BeginTeleport(point.X, point.Y, point.Z, "яхте", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
        // On the hull the point stands just above the aft deck, under the upper decks.
        if (_pendingTeleport != null) _pendingTeleport.FloorKnown = _yachtRuntime!.HullIn(owner);
    }

    // Every frame on the server: the yachts on the map, the next queued save
    // and the captain's offer.
    private void TickYachts(long now)
    {
        if (_yachts == null || _yachtRuntime == null || !_specialPlaces.OnlineMapEnabled) return;
        _yachtRuntime.Tick(_yachts);
        if (_yachtSaving == null && _yachtNext != null && now - _yachtSentAt >= YachtSaveSpacing)
        {
            YachtLook next = _yachtNext;
            _yachtNext = null;
            SaveYacht(next);
        }
        if (_yachts.Revision != _yachtMenuRevision && _yachtMenu?.Visible == true) RefreshYachtMenu();
        _yachtMenuRevision = _yachts.Revision;
        TickCaptain();
    }

    private void TickCaptain()
    {
        int captain = _yachtRuntime?.Captain(string.Empty) ?? 0;
        if (captain == 0 || _pool?.AreAnyVisible == true || IsInputBlocked()) return;
        Ped player = Game.Player.Character;
        if (player.IsInVehicle() || player.IsDead) return;
        Entity? found = Entity.FromHandle(captain);
        if (found == null || !found.Exists() || found.Position.DistanceTo(player.Position) > CaptainReach) return;
        Screen.ShowHelpTextThisFrame(UiText.Get("~INPUT_CONTEXT~ Капитан: перегнать яхту на другую стоянку"));
        if (!Controls.IsControlJustPressed(ControlType.PlayerControl, ControlAction.Context) || _yachtMenu == null || _yachtBerth == null) return;
        _pool!.HideAll();
        _yachtMenu.Visible = true;
        _yachtMenu.SelectedIndex = _yachtMenu.Items.IndexOf(_yachtBerth);
    }

    // A yacht that moves takes along whoever is on it.
    private void OnYachtMoved(string owner, int from, int to)
    {
        Vector3? local = LocalPlayerPosition();
        if (local is not Vector3 at || at.DistanceTo2D(YachtRuntime.HullPosition(Yachts.Berths[from])) > AboardDistance) return;
        if (_pendingTeleport != null || _mapLoad != null) return;
        YachtBerth berth = Yachts.Berths[to];
        BeginTeleport(berth.X, berth.Y, YachtBerth.Z, "яхте", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
    }

    private void ResetYachts()
    {
        _yachtRuntime?.Reset();
        _yachtSaving = null;
        _yachtNext = null;
    }

    private void CleanupYachts()
    {
        ResetYachts();
        if (_yachtRuntime != null) _yachtRuntime.Moved -= OnYachtMoved;
        _yachts?.Clear();
    }
}
