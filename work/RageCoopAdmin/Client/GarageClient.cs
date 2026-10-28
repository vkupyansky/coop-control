using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OGCoop.Shared;

namespace RageCoopAdmin.Client;

// All members run on the game thread.
internal sealed class GarageClient
{
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
