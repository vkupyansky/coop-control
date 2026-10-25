using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GTA;
using GTA.UI;
using LemonUI.Menus;
using RageCoop.Client.Scripting;
using CoopControl.Shared;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private readonly List<PeerSnapshot> _peers = new List<PeerSnapshot>();
    private readonly Dictionary<int, NativeItem> _peerRows = new Dictionary<int, NativeItem>();
    private NativeMenu? _peersMenu;
    private NativeItem? _peerEmptyItem;
    private long _lastPeerSnapshot;

    private void BuildPlayersMenu()
    {
        if (_pool == null || _mainMenu == null) return;
        _peersMenu = CreateMenu("Coop Control", "Напарники");
        UiMenu.Description(_peersMenu, () => UiText.Get("Выбрать игрока и переместиться к нему."));
        _mainMenu.AddSubMenu(_peersMenu);
        _peersMenu.Shown += (_, _) =>
        {
            if (!API.IsOnServer) return;
            UpdatePeerSnapshots();
            RefreshPeerRows();
        };
        _pool.Add(_peersMenu);
    }

    private void RequestPeerTeleport(int peerId)
    {
        if (!API.IsOnServer) return;
        if (!PlayerSnapshots.TryGet(out Dictionary<int, RageCoop.Client.Player> players))
        {
            Notification.PostTicker(UiText.Get("~o~Список игроков обновляется. Повторите действие."), false);
            return;
        }
        if (!players.ContainsKey(peerId))
        {
            API.LocalChatMessage(UiText.Get("Coop Control"), UiText.Get("Игрок уже отключился. Выберите другого напарника."));
            return;
        }
        RequestPassengerTeleport(peerId);
    }

    private void ProcessPlayersMenu(long now)
    {
        if (_peersMenu?.Visible != true) { return; }
        if ((now - _lastPeerSnapshot) / (double)Stopwatch.Frequency >= 0.5)
        {
            _lastPeerSnapshot = now;
            UpdatePeerSnapshots();
            RefreshPeerRows();
        }
    }

    private void UpdatePeerSnapshots()
    {
        if (!PlayerSnapshots.TryGet(out Dictionary<int, RageCoop.Client.Player> players)) return;
        Ped local = Game.Player.Character;
        if (local == null || !local.Exists()) return;
        _peers.Clear();
        GTA.Math.Vector3 localPosition = local.Position;
        // API.Players creates a dictionary copy. Read it once per snapshot,
        // and only read a streamed ped after checking it really is this player.
        foreach (KeyValuePair<int, RageCoop.Client.Player> entry in players)
        {
            RageCoop.Client.Player peer = entry.Value;
            if (entry.Key == API.LocalPlayerID) continue;
            float distance = peer.Position.DistanceTo(localPosition);
            string state = "Далеко";
            Ped? ped = RuntimeEntityLookup.GetPlayerPed(peer.ID)?.MainPed;
            if (ped != null && ped.Exists())
                state = ped.IsDead ? "Возрождается" : ped.IsInVehicle() ? "В транспорте" : "Пешком";
            else if (distance < 150f) state = "Загружается";
            float latency = peer.Ping;
            int ping = !Finite.Is(latency) || latency < 0 ? -1 : (int)Math.Min(latency * 1000f, 9999f);
            _peers.Add(new PeerSnapshot(peer.ID, CleanPlayerName(peer.Username), distance, state, ping));
        }
        _peers.Sort((first, second) =>
        {
            int compare = first.Distance.CompareTo(second.Distance);
            return compare != 0 ? compare : first.Id.CompareTo(second.Id);
        });
    }

    private void RefreshPeerRows()
    {
        if (_peersMenu == null) return;
        bool rosterChanged = _peerRows.Count != _peers.Count || _peers.Any(peer => !_peerRows.ContainsKey(peer.Id));
        if (rosterChanged || (_peerEmptyItem == null && _peers.Count == 0))
        {
            int? selectedId = _peerRows.Where(pair => ReferenceEquals(pair.Value, _peersMenu.SelectedItem))
                .Select(pair => (int?)pair.Key).FirstOrDefault();
            int selectedIndex = _peersMenu.SelectedIndex;
            _peersMenu.Clear();
            _peerRows.Clear();
            _peerEmptyItem = null;
            foreach (PeerSnapshot peer in _peers)
            {
                int id = peer.Id;
                NativeItem item = UiMenu.Item(() => peer.Name);
                item.Activated += (_, _) => RequestPeerTeleport(id);
                _peerRows.Add(id, item);
                _peersMenu.Add(item);
            }
            if (_peers.Count == 0)
            {
                _peerEmptyItem = UiMenu.Item(() => UiText.Get("Вы пока одни"), () => UiText.Get("Напарники появятся здесь после подключения."), configure: item => { item.Enabled = false; });
                _peersMenu.Add(_peerEmptyItem);
            }
            if (selectedId.HasValue && _peerRows.TryGetValue(selectedId.Value, out NativeItem? selected))
                _peersMenu.SelectedItem = selected;
            else if (_peersMenu.Items.Count > 0)
                _peersMenu.SelectedIndex = Math.Max(0, Math.Min(selectedIndex, _peersMenu.Items.Count - 1));
        }
        // Do not reorder rows as players move while someone is choosing a target.
        foreach (PeerSnapshot peer in _peers)
        {
            NativeItem item = _peerRows[peer.Id];
            UiMenu.Title(item, () => UiText.Format("К {0}", string.IsNullOrEmpty(peer.Name) ? UiText.Get("Игрок") : peer.Name));
            UiMenu.AltTitle(item, () => FormatPeerDistance(peer.Distance));
            UiMenu.Description(item, () => UiText.Get("На свободное пассажирское место или рядом. За рулём своей машины — вместе с ней. ") +
                UiText.Get(peer.State) + ". " + (peer.Ping >= 0 ? UiText.Format("Задержка: {0} мс.", peer.Ping) : UiText.Get("Связь устанавливается.")));
        }
    }

    private static string CleanPlayerName(string? name)
    {
        string clean = new string((name ?? string.Empty).Where(character => !char.IsControl(character) && character != '~').Take(20).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? string.Empty : clean;
    }

    private static string FormatPeerDistance(float distance) =>
        !Finite.Is(distance) ? "—" : distance >= 1000f
            ? UiText.Format("{0:0.0} км", distance / 1000f)
            : UiText.Format("{0} м", Math.Max(0, (int)distance));

    private void CleanupPlayersUi()
    {
        _peers.Clear();
        _lastPeerSnapshot = 0;
    }

    private sealed class PeerSnapshot
    {
        internal PeerSnapshot(int id, string name, float distance, string state, int ping)
        { Id = id; Name = name; Distance = distance; State = state; Ping = ping; }
        internal int Id { get; }
        internal string Name { get; }
        internal float Distance { get; }
        internal string State { get; }
        internal int Ping { get; }
    }
}
