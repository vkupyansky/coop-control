using System.Diagnostics;
using OGCoop.Shared;
using GTA;
using GTA.Native;
using GTA.UI;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private const long PlaceOfferMilliseconds = 60_000;
    private const long PlaceTickMilliseconds = 1_000;
    private const double PlaceTeleportSeconds = 20.0;
    private readonly SpecialPlaceRuntime _specialPlaces = new SpecialPlaceRuntime(SpecialPlaces.All, message => API.Logger.Info(message));
    private NativeMenu? _teleportsMenu;
    private NativeItem? _placeOfferItem;
    private int _placeOffer = -1;
    private long _placeOfferUntil;
    private long _lastPlaceTick;
    private GTA.Math.Vector3? _placeReturn;
    private MapLoad? _mapLoad;
    private ScriptTimeoutLift? _scriptTimeout;

    // Covering the switch to the Online map: the card fades in, the switch and a
    // scene load run behind it, then the card fades out over the reloaded world.
    // Map parts the switch replaces while they are loaded around the player
    // stay missing (holes in the ground, the world showing through): before the
    // switch the game's streaming focus moves to the open sea until the area
    // around the player has unloaded, as behind GTA Online's loading screen.
    private const long MapLoadFadeIn = 450;
    private const long MapFadeLimit = 5_000;
    private const long MapClearMinimum = 1500;
    private const long MapClearLimit = 6_000;
    private const long MapLoadMinimum = 1500;
    private const long MapLoadLimit = 15_000;
    private const long MapLoadFadeOut = 700;
    private static readonly GTA.Math.Vector3 OpenSea = new GTA.Math.Vector3(-5500f, -5500f, 0f);

    private void BuildSpecialPlacesMenu(NativeMenu teleports)
    {
        if (_pool == null) return;
        _teleportsMenu = teleports;
        NativeMenu places = CreateMenu("OG Coop", "Особые места");
        UiMenu.Description(places, () => UiText.Get("Скрытые места карты. Влево/вправо: только вы или вместе с напарником."));
        teleports.AddSubMenu(places);
        _pool.Add(places);
        NativeItem back = UiMenu.Item(() => UiText.Get("Вернуться назад"), () => UiText.Get("Туда, откуда вы перенеслись в особое место."));
        back.Activated += (_, _) => ReturnFromSpecialPlace();
        places.Add(back);
        foreach (SpecialPlace place in SpecialPlaces.All)
        {
            NativeListItem<string> item = UiMenu.List(() => UiText.Get(place.Name), () => UiText.Get(place.Description),
                () => new[] { UiText.Get("Только я"), UiText.Get("Вместе с напарником") });
            item.Activated += (_, _) => TeleportToSpecialPlace(place, item.SelectedIndex == 1);
            places.Add(item);
        }
    }

    private void TeleportToSpecialPlace(SpecialPlace place, bool withPartner)
    {
        if (!CanTeleportToPlace(place.OnFootOnly, out Ped player))
        {
            return;
        }

        _specialPlaces.Target(place.Id, NowMilliseconds());
        if (_specialPlaces.LoadedParts(place.Id) == 0)
        {
            _specialPlaces.CancelTarget(place.Id);
            Notification.PostTicker(UiText.Get("~r~Телепорт:~s~ этого места нет в вашей версии игры."), false);
            return;
        }

        if (withPartner)
        {
            API.SendCustomEvent(SpecialPlaceProtocol.Invite, place.Id);
        }

        // Hopping between places keeps the way back to where the trip started.
        GTA.Math.Vector3 here = player.Position;
        if (_placeReturn == null || !_specialPlaces.IsInsideSeparatePlace(here)) _placeReturn = here;
        BeginTeleport(place.X, place.Y, place.Z, "месту «" + place.Name + "»", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
    }

    private void ReturnFromSpecialPlace()
    {
        if (_placeReturn is not GTA.Math.Vector3 back)
        {
            Notification.PostTicker(UiText.Get("~o~Телепорт:~s~ вы ещё не переносились в особое место."), false);
            return;
        }

        if (!CanTeleportToPlace(false, out _))
        {
            return;
        }

        _placeReturn = null;
        BeginTeleport(back.X, back.Y, back.Z, "назад", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
    }

    private bool CanTeleportToPlace(bool onFootOnly, out Ped player)
    {
        player = Game.Player.Character;
        if (!API.IsOnServer)
        {
            return false;
        }

        if (player == null || !player.Exists() || player.IsDead)
        {
            Notification.PostTicker(UiText.Get("~o~Телепорт:~s~ сначала дождитесь возрождения."), false);
            return false;
        }

        if (_pendingTeleport != null || _pendingRecall != null)
        {
            Notification.PostTicker(UiText.Get("~o~Телепорт:~s~ дождитесь текущего перемещения."), false);
            return false;
        }

        if (_mapLoad != null || !_specialPlaces.MapReady)
        {
            Notification.PostTicker(UiText.Get("~o~Телепорт:~s~ дождитесь загрузки карты."), false);
            return false;
        }

        if (onFootOnly && player.IsInVehicle())
        {
            Notification.PostTicker(UiText.Get("~o~Телепорт:~s~ в это место можно пешком. Выйдите из транспорта."), false);
            return false;
        }

        return true;
    }

    private void OnSpecialPlaceOffer(CustomEventReceivedArgs e)
    {
        if (_stopping || !SpecialPlaceProtocol.TryReadOffer(e.Args, out int place, out string inviter))
        {
            return;
        }

        API.QueueAction(() =>
        {
            if (_active && !_stopping && API.IsOnServer) ShowPlaceOffer(place, inviter);
        });
    }

    private void ShowPlaceOffer(int id, string inviter)
    {
        SpecialPlace place = SpecialPlaces.All[id];
        ClearPlaceOffer();
        string name = inviter.Length == 0 ? UiText.Get("Напарник") : inviter;
        _placeOffer = id;
        _placeOfferUntil = NowMilliseconds() + PlaceOfferMilliseconds;
        _placeOfferItem = UiMenu.Item(() => UiText.Format("Принять: {0}", UiText.Get(place.Name)),
            () => UiText.Format("{0} зовёт вас сюда. Приглашение действует минуту.", name));
        _placeOfferItem.Activated += (_, _) => AcceptPlaceOffer();
        _teleportsMenu?.Add(0, _placeOfferItem);
        Notification.PostTicker(UiText.Format("~b~{0}~s~ зовёт: {1}. Принять: J → Телепорты.", name, UiText.Get(place.Name)), false);
    }

    private void AcceptPlaceOffer()
    {
        int id = _placeOffer;
        bool current = id >= 0 && NowMilliseconds() <= _placeOfferUntil;
        ClearPlaceOffer();
        if (current)
        {
            TeleportToSpecialPlace(SpecialPlaces.All[id], false);
        }
    }

    private void ClearPlaceOffer()
    {
        if (_placeOfferItem != null && _teleportsMenu?.Contains(_placeOfferItem) == true)
        {
            _teleportsMenu.Remove(_placeOfferItem);
        }

        _placeOfferItem = null;
        _placeOffer = -1;
    }

    private void TickSpecialPlaces()
    {
        long now = NowMilliseconds();
        _specialPlaces.Frame(now);
        _hangWatch.Step("map load");
        TickMapLoad(now);
        _hangWatch.Step("interiors");
        TickInteriors();
        _hangWatch.Step("special places");
        if (_placeOffer >= 0 && now > _placeOfferUntil)
        {
            ClearPlaceOffer();
        }

        _hangWatch.Step("map icons");
        TickMapIcons(now);
        _hangWatch.Step("special places");
        if (now - _lastPlaceTick < PlaceTickMilliseconds)
        {
            return;
        }

        _lastPlaceTick = now;
        _specialPlaces.Tick(now, LocalPlayerPosition());
    }

    private void TickMapLoad(long now)
    {
        if (_mapLoad == null)
        {
            if (!_specialPlaces.NeedsOnlineMap || _pendingTeleport != null || _pendingRecall != null) return;
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists() || player.IsDead) return;
            Vehicle? vehicle = player.CurrentVehicle;
            Entity held = vehicle != null && vehicle.Exists() && vehicle.Driver?.Handle == player.Handle ? vehicle : player;
            _mapLoad = new MapLoad(now, held, held.IsPositionFrozen);
            held.IsPositionFrozen = true;
            MapLoadingScreen.FadeOut((int)MapLoadFadeIn);
            LoadingPrompt.Show(UiText.Get("Загрузка карты"));
        }

        MapLoad load = _mapLoad;
        long elapsed = now - load.Started;
        int alpha = 255;
        if (load.ClosedAt != 0)
        {
            long closing = now - load.ClosedAt;
            if (closing >= MapLoadFadeOut)
            {
                _mapLoad = null;
                return;
            }

            alpha = (int)(255 * (1 - (double)closing / MapLoadFadeOut));
        }
        else
        {
            if (elapsed < MapLoadFadeIn) alpha = (int)(255 * (double)elapsed / MapLoadFadeIn);
            // ON_ENTER_MP stops the game for seconds, and meanwhile only GTA's
            // own fade covers the world: this drawing stops with the game. The
            // fade counts game time, which lags right after connecting, so the
            // switch waits until the screen is really dark. Another script
            // that fades the screen in is overridden.
            bool dark = MapLoadingScreen.IsFadedOut();
            if (!dark && elapsed >= MapLoadFadeIn && !MapLoadingScreen.IsFadingOut()) MapLoadingScreen.FadeOut((int)MapLoadFadeIn);
            if (load.ClearedAt == 0)
            {
                if (dark || elapsed >= MapFadeLimit)
                {
                    Function.Call(Hash.SET_FOCUS_POS_AND_VEL, OpenSea.X, OpenSea.Y, OpenSea.Z, 0f, 0f, 0f);
                    load.FocusAway = true;
                    load.ClearedAt = now;
                    load.DarkAfter = dark ? elapsed : -1;
                }
            }
            else if (load.SwitchedAt == 0)
            {
                // The area has unloaded once the collision around the player is gone.
                bool unloaded = !load.Held.Exists() || !Function.Call<bool>((Hash)0xE9676F61BC0B3321UL, load.Held.Handle); // HAS_COLLISION_LOADED_AROUND_ENTITY
                long clearing = now - load.ClearedAt;
                if ((clearing >= MapClearMinimum && unloaded && dark) || clearing >= MapClearLimit)
                {
                    (_scriptTimeout ??= new ScriptTimeoutLift(message => API.Logger.Info(message))).Lift();
                    _specialPlaces.EnterOnlineMap();
                    Function.Call(Hash.CLEAR_FOCUS);
                    load.FocusAway = false;
                    GTA.Math.Vector3 here = load.Held.Exists() ? load.Held.Position : Game.Player.Character.Position;
                    Function.Call((Hash)0xACCFB4ACF53551B0UL, here.X, here.Y, here.Z, 200f, 0); // NEW_LOAD_SCENE_START_SPHERE
                    load.SwitchedAt = NowMilliseconds();
                    API.Logger.Info("OG Coop: map switched: screen dark " +
                        (load.DarkAfter >= 0 ? "after " + load.DarkAfter + " ms" : "not confirmed") +
                        ", area cleared in " + clearing + " ms" + (unloaded ? "" : " (limit reached)") +
                        ", the switch took " + (load.SwitchedAt - now) + " ms" + (dark ? "" : " (screen not dark)") + ".");
                }
            }
            else
            {
                // Rockstar's own switch waits for the scene, the content
                // changes and the player's streaming before fading in.
                long loading = now - load.SwitchedAt;
                bool ready = Function.Call<bool>((Hash)0x01B8247A7A8B9AD1UL) && // IS_NEW_LOAD_SCENE_LOADED
                    !Function.Call<bool>((Hash)0x241FCA5B1AA14F75UL) && // ARE_ANY_CCS_PENDING
                    Function.Call<bool>((Hash)0x7350823473013C02UL, Game.Player.Character.Handle); // HAVE_ALL_STREAMING_REQUESTS_COMPLETED
                if ((loading >= MapLoadMinimum && ready) || loading >= MapLoadLimit)
                {
                    ReleaseMapLoad(load);
                    load.ClosedAt = now;
                    API.Logger.Info("OG Coop: map loaded " + loading + " ms after the switch" + (ready ? "" : " (limit reached)") + ".");
                }
            }
        }

        MapLoadingScreen.Draw(alpha, elapsed / 1000.0);
    }

    // Safe to repeat.
    private void ReleaseMapLoad(MapLoad load)
    {
        _scriptTimeout?.Restore();
        if (load.Released) return;
        load.Released = true;
        if (load.FocusAway) Function.Call(Hash.CLEAR_FOCUS);
        load.FocusAway = false;
        if (load.SwitchedAt != 0) Function.Call((Hash)0xC197616D221FF4A4UL); // NEW_LOAD_SCENE_STOP
        if (load.Held.Exists()) load.Held.IsPositionFrozen = load.HeldWasFrozen;
        MapLoadingScreen.FadeIn((int)MapLoadFadeOut);
        LoadingPrompt.Hide();
    }

    private void ResetSpecialPlaces()
    {
        if (_mapLoad != null)
        {
            ReleaseMapLoad(_mapLoad);
            _mapLoad = null;
        }

        ClearPlaceOffer();
        ClearMapIcons();
        ResetInteriors();
        _placeReturn = null;
        _lastPlaceTick = 0;
        // Disconnected or stopping: touch the game only when a place is loaded.
        if (_specialPlaces.HasState) _specialPlaces.Reset(LocalPlayerPosition());
    }

    private static GTA.Math.Vector3? LocalPlayerPosition()
    {
        Ped player = Game.Player.Character;
        return player != null && player.Exists() ? player.Position : null;
    }

    private static long NowMilliseconds() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    private sealed class MapLoad
    {
        internal MapLoad(long started, Entity held, bool heldWasFrozen)
        {
            Started = started;
            Held = held;
            HeldWasFrozen = heldWasFrozen;
        }

        internal long Started { get; }
        internal Entity Held { get; }
        internal bool HeldWasFrozen { get; }
        internal long ClearedAt { get; set; }
        internal long DarkAfter { get; set; }
        internal bool FocusAway { get; set; }
        internal long SwitchedAt { get; set; }
        internal long ClosedAt { get; set; }
        internal bool Released { get; set; }
    }
}
