namespace OGCoop.Shared;

internal enum WorldStateChange
{
    Snapshot = 0,
    SetTime = 1,
    SetWeather = 2,
    FreezeTime = 3,
    FreezeWeather = 4,
    GhostTraffic = 5
}

internal sealed class WorldStateSnapshot
{
    internal WorldStateSnapshot(string epoch, long revision, bool timeFrozen, bool weatherFrozen,
        bool ghostTrafficEnabled, int hour, int minute, int second, int weatherIndex)
    {
        Epoch = epoch;
        Revision = revision;
        TimeFrozen = timeFrozen;
        WeatherFrozen = weatherFrozen;
        GhostTrafficEnabled = ghostTrafficEnabled;
        Hour = hour;
        Minute = minute;
        Second = second;
        WeatherIndex = weatherIndex;
    }

    internal string Epoch { get; }
    internal long Revision { get; }
    internal bool TimeFrozen { get; }
    internal bool WeatherFrozen { get; }
    internal bool GhostTrafficEnabled { get; }
    internal int Hour { get; }
    internal int Minute { get; }
    internal int Second { get; }
    internal int WeatherIndex { get; }
}

internal static class WorldStateProtocol
{
    internal const int Request = 20774142;
    internal const int Change = 20774143;
    internal const int State = 20774144;
    internal const int Observe = 20774147;

    internal static bool IsEpoch(string value)
    {
        if (value.Length != 32) return false;
        foreach (char c in value)
            if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
        return true;
    }

    internal static bool IsClock(int hour, int minute, int second) =>
        hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59 && second >= 0 && second <= 59;

    internal static bool IsWeather(int index) => index >= 0 && index < WorldOptions.WeatherCodes.Length;

    internal static bool IsChange(int kind) => kind >= (int)WorldStateChange.SetTime && kind <= (int)WorldStateChange.GhostTraffic;

    internal static object[] Encode(WorldStateSnapshot state, int session, int requestId = 0,
        bool accepted = true, WorldStateChange change = WorldStateChange.Snapshot) =>
        new object[] { session, state.Epoch, state.Revision, requestId, accepted, (int)change,
            state.TimeFrozen, state.WeatherFrozen, state.GhostTrafficEnabled,
            state.Hour, state.Minute, state.Second, state.WeatherIndex };

    internal static bool TryRead(object[]? args, out int session, out int requestId, out bool accepted,
        out WorldStateChange change, out WorldStateSnapshot? state)
    {
        session = 0;
        requestId = 0;
        accepted = false;
        change = WorldStateChange.Snapshot;
        state = null;
        if (args == null || args.Length != 13 || args[0] is not int token || token < 0 ||
            args[1] is not string epoch || !IsEpoch(epoch) ||
            args[2] is not long revision || revision < 0 || args[3] is not int id || id < 0 ||
            args[4] is not bool ok || args[5] is not int kind || kind < 0 || kind > 5 ||
            args[6] is not bool time || args[7] is not bool weather || args[8] is not bool ghost ||
            args[9] is not int hour || args[10] is not int minute || args[11] is not int second ||
            args[12] is not int weatherIndex ||
            !(IsClock(hour, minute, second) || (!time && hour == -1 && minute == 0 && second == 0)) ||
            !(IsWeather(weatherIndex) || (!weather && weatherIndex == -1)) ||
            (token == 0 && id != 0) || (id > 0 && kind == 0)) return false;
        session = token;
        requestId = id;
        accepted = ok;
        change = (WorldStateChange)kind;
        state = new WorldStateSnapshot(epoch, revision, time, weather, ghost, hour, minute, second, weatherIndex);
        return true;
    }

    internal static int WeatherIndexForHash(int hash)
    {
        for (int index = 0; index < WorldOptions.WeatherCodes.Length; index++)
            if (WeatherHash(WorldOptions.WeatherCodes[index]) == hash) return index;
        return -1;
    }

    internal static int WeatherHash(string code)
    {
        uint hash = 0;
        foreach (char c in code)
        {
            hash += (byte)char.ToLowerInvariant(c);
            hash += hash << 10;
            hash ^= hash >> 6;
        }
        hash += hash << 3;
        hash ^= hash >> 11;
        hash += hash << 15;
        return unchecked((int)hash);
    }
}
