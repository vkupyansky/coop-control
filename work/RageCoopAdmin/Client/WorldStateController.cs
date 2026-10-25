using System;
using System.Diagnostics;
using CoopControl.Shared;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// All methods run on the game thread. Network callbacks are queued by the menu's
// event handler so neither natives nor UI state escape the resource lifecycle.
internal sealed class WorldStateController
{
    private readonly Action<int, object[]> send;
    private bool active;
    private long lastFetch;
    private int fetches;
    private long lastObservation;
    private int nextRequest;
    private long pendingSince;
    private WorldStateChange pendingKind;
    private bool changedClock;
    private bool changedWeather;

    internal WorldStateController(Action<int, object[]> send) => this.send = send;
    internal WorldStateSnapshot? Snapshot { get; private set; }
    internal int SessionToken { get; private set; }
    internal int PendingRequestId { get; private set; }
    internal event Action<WorldStateSnapshot, int, bool, WorldStateChange>? Changed;
    internal event Action? PendingExpired;

    internal void Start()
    {
        Stop();
        SessionToken = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & int.MaxValue;
        if (SessionToken == 0) SessionToken = 1;
        try
        {
            WorldStateEngineGuard.Install(this);
            active = true;
            Fetch();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    internal void Stop()
    {
        active = false;
        try
        {
            WorldStateEngineGuard.Uninstall(this);
        }
        finally
        {
            try { ReleaseOverrides(); }
            finally
            {
                Snapshot = null;
                PendingRequestId = 0;
                SessionToken = 0;
                nextRequest = 0;
                fetches = 0;
                lastObservation = 0;
            }
        }
    }

    internal int Request(WorldStateChange change, int value1, int value2, bool enabled)
    {
        WorldStateSnapshot? state = Snapshot;
        if (!active || state == null || PendingRequestId != 0 || !API.IsOnServer || nextRequest == int.MaxValue) return 0;
        PendingRequestId = ++nextRequest;
        pendingKind = change;
        pendingSince = Stopwatch.GetTimestamp();
        send(WorldStateProtocol.Change,
            new object[] { SessionToken, PendingRequestId, state.Epoch, (int)change, value1, value2, enabled });
        return PendingRequestId;
    }

    internal bool Receive(object[]? args)
    {
        if (!active || !API.IsOnServer || !WorldStateProtocol.TryRead(args, out int session,
                out int requestId, out bool accepted, out WorldStateChange change, out WorldStateSnapshot? next) || next == null ||
            (session != 0 && session != SessionToken)) return false;
        WorldStateSnapshot? previous = Snapshot;
        if (previous == null)
        {
            // Initial identity must be proved by the target response to this exact
            // resource instance, never an unsolicited broadcast from an old one.
            if (session != SessionToken || requestId != 0) return false;
        }
        else if (next.Epoch != previous.Epoch) return false;

        bool acknowledgement = requestId > 0 && session == SessionToken &&
            requestId == PendingRequestId && change == pendingKind;
        bool apply = previous == null || next.Revision >= previous.Revision;
        if (!apply && !acknowledgement) return false;
        if (acknowledgement) PendingRequestId = 0;
        if (apply)
        {
            Snapshot = next;
            Apply(previous, next, change, requestId == 0 || accepted);
        }
        Changed?.Invoke(Snapshot!, acknowledgement ? requestId : 0, accepted, change);
        return true;
    }

    internal void Tick()
    {
        if (!active) return;
        if (!API.IsOnServer)
        {
            Stop();
            return;
        }
        long now = Stopwatch.GetTimestamp();
        if (Snapshot == null && fetches < 5 && Elapsed(now, lastFetch) >= 2) Fetch();
        if (PendingRequestId != 0 && Elapsed(now, pendingSince) >= 5)
        {
            PendingRequestId = 0;
            PendingExpired?.Invoke();
            Fetch();
        }
        WorldStateSnapshot? state = Snapshot;
        if (state == null) return;
        if (state.TimeFrozen)
        {
            SetClock(state);
            WorldStateNatives.PauseClock(true);
        }
        if (state.WeatherFrozen)
            WorldStateNatives.FreezeWeather(state.WeatherIndex);
        if (Elapsed(now, lastObservation) >= 1)
        {
            lastObservation = now;
            if (API.Players.TryGetValue(API.LocalPlayerID, out RageCoop.Client.Player local) && local.IsHost)
                send(WorldStateProtocol.Observe, WorldStateNatives.Observe(SessionToken, state.Epoch, state.Revision));
        }
    }

    private void Fetch()
    {
        lastFetch = Stopwatch.GetTimestamp();
        fetches++;
        send(WorldStateProtocol.Request, new object[] { SessionToken });
    }

    private void Apply(WorldStateSnapshot? previous, WorldStateSnapshot next, WorldStateChange change, bool applySelection)
    {
        if (next.TimeFrozen)
        {
            changedClock = true;
            SetClock(next);
            WorldStateNatives.PauseClock(true);
        }
        else
        {
            if (changedClock)
            {
                WorldStateNatives.PauseClock(false);
                changedClock = false;
            }
            if (next.Hour >= 0 && (previous == null || previous.TimeFrozen ||
                (change == WorldStateChange.SetTime && applySelection))) SetClock(next);
        }
        if (next.WeatherFrozen)
        {
            changedWeather = true;
            WorldStateNatives.FreezeWeather(next.WeatherIndex);
        }
        else
        {
            if (changedWeather)
            {
                WorldStateNatives.ClearWeather();
                changedWeather = false;
            }
            if (next.WeatherIndex >= 0 && (previous == null ||
                (change == WorldStateChange.SetWeather && applySelection) || next.WeatherIndex != previous.WeatherIndex))
            {
                // A single cycle change: never refresh NOW every host snapshot,
                // otherwise the weather can never resume its normal progression.
                WorldStateNatives.SelectWeather(next.WeatherIndex);
            }
        }
    }

    private static void SetClock(WorldStateSnapshot state) =>
        WorldStateNatives.SetClock(state.Hour, state.Minute, state.Second);

    private void ReleaseOverrides()
    {
        try
        {
            if (changedClock) WorldStateNatives.PauseClock(false);
        }
        finally
        {
            changedClock = false;
            try
            {
                if (changedWeather) WorldStateNatives.ClearWeather();
            }
            finally { changedWeather = false; }
        }
    }

    private static double Elapsed(long now, long previous) => (now - previous) / (double)Stopwatch.Frequency;
}
