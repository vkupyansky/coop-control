using System;
using CoopControl.Shared;

namespace CoopAdmin.Server;

// All resource state changes and host observations share this lock. Clock projection
// uses the host's actual rate and a monotonic sample, with no additional timer.
internal sealed class WorldStateAuthority
{
    private readonly object gate = new();
    private bool timeFrozen;
    private bool weatherFrozen;
    private bool ghostTrafficEnabled;
    private int seconds = -1;
    private int millisecondsPerMinute;
    private long sampledAt;
    private int weatherIndex = -1;
    private long revision;

    internal string Epoch { get; } = Guid.NewGuid().ToString("N");
    internal bool GhostTrafficEnabled { get { lock (gate) return ghostTrafficEnabled; } }

    internal WorldStateSnapshot Snapshot(long now)
    {
        lock (gate) return CreateSnapshot(now);
    }

    internal void ObserveHost(int hour, int minute, int second, int weather, int clockRate, long now, long expectedRevision = -1)
    {
        if (!WorldStateProtocol.IsClock(hour, minute, second) || !WorldStateProtocol.IsWeather(weather) ||
            clockRate < 1 || clockRate > 600000) return;
        lock (gate)
        {
            if (expectedRevision >= 0 && expectedRevision != revision) return;
            if (!timeFrozen)
            {
                seconds = hour * 3600 + minute * 60 + second;
                sampledAt = now;
                millisecondsPerMinute = clockRate;
            }
            if (!weatherFrozen) weatherIndex = weather;
        }
    }

    internal bool TryChange(string epoch, WorldStateChange kind, int value1, int value2, bool enabled,
        long now, out WorldStateSnapshot state)
    {
        lock (gate)
        {
            bool valid = epoch == Epoch;
            switch (kind)
            {
                case WorldStateChange.SetTime:
                    valid &= WorldStateProtocol.IsClock(value1, value2, 0) && !enabled;
                    break;
                case WorldStateChange.SetWeather:
                    valid &= WorldStateProtocol.IsWeather(value1) && value2 == 0 && !enabled;
                    break;
                case WorldStateChange.FreezeTime:
                    valid &= WorldStateProtocol.IsClock(value1, value2, 0);
                    break;
                case WorldStateChange.FreezeWeather:
                    valid &= (WorldStateProtocol.IsWeather(value1) || value1 == -1) && value2 == 0;
                    valid &= !enabled || weatherIndex >= 0 || value1 >= 0;
                    break;
                case WorldStateChange.GhostTraffic:
                    valid &= value1 == 0 && value2 == 0;
                    break;
                default:
                    valid = false;
                    break;
            }
            if (!valid)
            {
                state = CreateSnapshot(now);
                return false;
            }

            int currentSeconds = ProjectSeconds(now);
            switch (kind)
            {
                case WorldStateChange.SetTime:
                    seconds = value1 * 3600 + value2 * 60;
                    sampledAt = now;
                    break;
                case WorldStateChange.SetWeather:
                    weatherIndex = value1;
                    break;
                case WorldStateChange.FreezeTime:
                    // Prefer the host's shared clock. A freshly loaded resource may
                    // receive the menu request before its first host observation.
                    seconds = currentSeconds >= 0 ? currentSeconds : value1 * 3600 + value2 * 60;
                    sampledAt = now;
                    timeFrozen = enabled;
                    break;
                case WorldStateChange.FreezeWeather:
                    if (weatherIndex < 0 && value1 >= 0) weatherIndex = value1;
                    weatherFrozen = enabled;
                    break;
                case WorldStateChange.GhostTraffic:
                    ghostTrafficEnabled = enabled;
                    break;
            }
            revision = checked(revision + 1);
            state = CreateSnapshot(now);
            return true;
        }
    }

    internal bool ReleaseModes(long now, out WorldStateSnapshot state)
    {
        lock (gate)
        {
            bool changed = timeFrozen || weatherFrozen || ghostTrafficEnabled;
            seconds = ProjectSeconds(now);
            sampledAt = now;
            timeFrozen = false;
            weatherFrozen = false;
            ghostTrafficEnabled = false;
            if (changed) revision = checked(revision + 1);
            state = CreateSnapshot(now);
            return changed;
        }
    }

    // At resource start, before any player: the players' saved choice.
    internal void RestoreGhostTraffic(bool enabled)
    {
        lock (gate) ghostTrafficEnabled = enabled;
    }

    internal void Reset()
    {
        lock (gate)
        {
            timeFrozen = false;
            weatherFrozen = false;
            ghostTrafficEnabled = false;
            seconds = -1;
            weatherIndex = -1;
            millisecondsPerMinute = 0;
            sampledAt = 0;
            revision = 0;
        }
    }

    private int ProjectSeconds(long now)
    {
        if (seconds < 0 || timeFrozen || millisecondsPerMinute <= 0) return seconds;
        // Reduce before multiplying: monotonic uptime cannot overflow the projection.
        long elapsed = Math.Max(0, now - sampledAt);
        long dayMilliseconds = (long)millisecondsPerMinute * 1440;
        long added = elapsed % dayMilliseconds * 60 / millisecondsPerMinute;
        return (int)((seconds + added) % 86400);
    }

    private WorldStateSnapshot CreateSnapshot(long now)
    {
        int current = ProjectSeconds(now);
        return new WorldStateSnapshot(Epoch, revision, timeFrozen, weatherFrozen, ghostTrafficEnabled,
            current < 0 ? -1 : current / 3600, current < 0 ? 0 : current / 60 % 60,
            current < 0 ? 0 : current % 60, weatherIndex);
    }
}
