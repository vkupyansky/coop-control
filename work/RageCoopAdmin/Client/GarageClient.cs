using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopControl.Shared;

namespace RageCoopAdmin.Client;

// The server keeps this player's garage; the menu shows its latest copy and
// sends every change there. All members run on the game thread.
internal sealed class GarageClient
{
    internal const string LegacyPresetsFile = "vehicle-presets.xml";
    internal const string LegacyFavoritesFile = "vehicle-favorites.xml";
    private const int MaximumPending = 64;
    private readonly Action<int, object[]> _send;
    private readonly Dictionary<int, Action<GarageResult>> _pending = new Dictionary<int, Action<GarageResult>>();
    private int _nextToken;

    internal GarageClient(Action<int, object[]> send) => _send = send;

    internal GarageData? Current { get; private set; }
    internal event Action? Changed;

    internal void Request() => _send(GarageProtocol.Request, Array.Empty<object>());

    internal bool ApplyState(object[] args)
    {
        if (args is not { Length: 1 } || args[0] is not string xml) return false;
        GarageData garage;
        try { garage = GarageCodec.Decode(xml); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { return false; }
        Current = garage;
        Changed?.Invoke();
        return true;
    }

    internal bool ApplyResult(object[] args)
    {
        if (args is not { Length: 2 } || args[0] is not int token || args[1] is not int code ||
            !Enum.IsDefined(typeof(GarageResult), code) || !_pending.TryGetValue(token, out Action<GarageResult>? done)) return false;
        _pending.Remove(token);
        done((GarageResult)code);
        return true;
    }

    internal void SavePreset(string name, VehicleAppearance appearance, Action<GarageResult> done) =>
        Send(done, GarageProtocol.SavePreset, name, VehicleAppearanceCodec.Encode(appearance));

    internal void DeletePreset(string name, Action<GarageResult> done) => Send(done, GarageProtocol.DeletePreset, name);

    internal void SetFavorite(int model, bool favorite, Action<GarageResult> done) =>
        Send(done, favorite ? GarageProtocol.AddFavorite : GarageProtocol.RemoveFavorite, model);

    // The garage this computer kept before moves to the server once; the local
    // files are removed only after everything in them is stored there.
    internal bool ImportLocal(string dataFolder, Action<bool> finished)
    {
        string presets = Path.Combine(dataFolder, LegacyPresetsFile), favorites = Path.Combine(dataFolder, LegacyFavoritesFile);
        bool hasPresets = File.Exists(presets), hasFavorites = File.Exists(favorites);
        if (!hasPresets && !hasFavorites) return false;
        GarageData local;
        using (StreamReader? presetReader = hasPresets ? new StreamReader(presets) : null)
        using (StreamReader? favoriteReader = hasFavorites ? new StreamReader(favorites) : null)
            local = GarageCodec.ReadLegacy(presetReader, favoriteReader);
        Send(result =>
        {
            bool moved = result == GarageResult.Saved;
            try
            {
                if (moved && hasPresets) File.Delete(presets);
                if (moved && hasFavorites) File.Delete(favorites);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Stored on the server already; a later connection merges the same names again harmlessly.
                moved = false;
            }
            finished(moved);
        }, GarageProtocol.Import, GarageCodec.Encode(local));
        return true;
    }

    internal void Clear()
    {
        Current = null;
        _pending.Clear();
    }

    private void Send(Action<GarageResult> done, int operation, params object[] values)
    {
        // A lost reply must not retain callbacks for the whole session.
        if (_pending.Count >= MaximumPending) _pending.Remove(_pending.Keys.Min());
        int token = unchecked(++_nextToken);
        _pending[token] = done;
        _send(GarageProtocol.Change, new object[] { token, operation }.Concat(values).ToArray());
    }
}
