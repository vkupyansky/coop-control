using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Map icons of our places and interiors within MapIconBudget, nearest first.
public sealed partial class AdminMenuScript
{
    private const long MapIconMilliseconds = 1_000;
    private readonly Dictionary<string, Blip> _mapIcons = new Dictionary<string, Blip>();
    private UiLanguage? _mapIconLanguage;
    private bool _mapIconsForPauseMap;
    private long _lastMapIconSync;

    // Every frame: the full set appears as soon as the pause map opens.
    private void TickMapIcons(long now)
    {
        bool pauseMap = Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE);
        if (pauseMap == _mapIconsForPauseMap && now - _lastMapIconSync < MapIconMilliseconds) return;
        _mapIconsForPauseMap = pauseMap;
        _lastMapIconSync = now;
        SyncMapIcons(pauseMap);
    }

    private void SyncMapIcons(bool pauseMap)
    {
        bool rename = _mapIconLanguage != UiText.Language;
        _mapIconLanguage = UiText.Language;
        foreach (string gone in _mapIcons.Where(item => !item.Value.Exists()).Select(item => item.Key).ToArray()) _mapIcons.Remove(gone);

        Vector3? local = LocalPlayerPosition();
        MapIcon[] wanted = WantedMapIcons()
            .OrderBy(icon => local is Vector3 at ? Flat(at, icon.At) : 0f)
            .ToArray();
        int others = Function.Call<int>(Hash.GET_NUMBER_OF_ACTIVE_BLIPS) - _mapIcons.Count;
        var shown = new HashSet<string>(wanted.Take(MapIconBudget.Allowed(others, pauseMap, wanted.Length)).Select(icon => icon.Key));

        foreach (string key in _mapIcons.Keys.Where(key => !shown.Contains(key)).ToArray())
        {
            _mapIcons[key].Delete();
            _mapIcons.Remove(key);
        }

        foreach (MapIcon icon in wanted.Where(icon => shown.Contains(icon.Key)))
        {
            if (_mapIcons.TryGetValue(icon.Key, out Blip? blip))
            {
                if (rename) blip.Name = UiText.Get(icon.Name);
                continue;
            }

            blip = Blip.Create(icon.At);
            blip.Sprite = (BlipSprite)icon.Sprite;
            if (icon.Place) blip.Color = BlipColor.Orange;
            // Places point the way from afar; interiors show on the minimap only nearby.
            blip.IsShortRange = !icon.Place;
            blip.Name = UiText.Get(icon.Name);
            _mapIcons[icon.Key] = blip;
        }
    }

    // Loaded places (the island loads only near it, but its icon shows the
    // way) and the buildings with interiors this game has.
    private IEnumerable<MapIcon> WantedMapIcons()
    {
        foreach (SpecialPlace place in SpecialPlaces.All)
        {
            if (place.Icon != 0 && (place.LoadsNearby || _specialPlaces.IsActive(place.Id)))
                yield return new MapIcon("place:" + place.Id, new Vector3(place.X, place.Y, place.Z), place.Icon, place.Name, true);
        }

        if (!_specialPlaces.OnlineMapEnabled) yield break;
        foreach (InteriorBuilding building in OnlineInteriors.Buildings)
        {
            if (IsAvailable(building))
                yield return new MapIcon("interior:" + building.Key, new Vector3(building.X, building.Y, building.Z), building.Sprite, building.Name, false);
        }
    }

    private void ClearMapIcons()
    {
        foreach (Blip blip in _mapIcons.Values)
        {
            if (blip.Exists()) blip.Delete();
        }

        _mapIcons.Clear();
        _mapIconLanguage = null;
        _mapIconsForPauseMap = false;
        _lastMapIconSync = 0;
    }

    private static float Flat(Vector3 from, Vector3 to)
    {
        float dx = from.X - to.X, dy = from.Y - to.Y;
        return dx * dx + dy * dy;
    }

    private readonly struct MapIcon
    {
        internal MapIcon(string key, Vector3 at, int sprite, string name, bool place)
        {
            Key = key;
            At = at;
            Sprite = sprite;
            Name = name;
            Place = place;
        }

        internal string Key { get; }
        internal Vector3 At { get; }
        internal int Sprite { get; }
        internal string Name { get; }
        internal bool Place { get; }
    }
}
