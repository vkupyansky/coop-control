using System;
using System.Reflection;

namespace RageCoopAdmin.Client;

// SHVDN aborts a script whose frame ran longer than ScriptTimeoutThreshold (5 s by default).
// ON_ENTER_MP takes longer on a slow PC, and RageCoop disconnected with reason Abort.
// The threshold is checked after the frame, so it is raised before the call and restored later.
internal sealed class ScriptTimeoutLift
{
    internal const uint LiftedMilliseconds = 60_000;
    private readonly Action<string> _log;
    private object? _domain;
    private PropertyInfo? _threshold;
    private uint _restore;

    internal ScriptTimeoutLift(Action<string> log) => _log = log;

    internal void Lift()
    {
        if (_threshold != null) return;
        try
        {
            if (!Find(out object? domain, out PropertyInfo? threshold, out string missing))
            {
                _log("OG Coop: SHVDN script timeout left as is: " + missing + ".");
                return;
            }

            uint current = (uint)threshold!.GetValue(domain)!;
            if (current >= LiftedMilliseconds) return;
            threshold.SetValue(domain, LiftedMilliseconds);
            _domain = domain;
            _threshold = threshold;
            _restore = current;
            _log("OG Coop: SHVDN script timeout " + current + " -> " + LiftedMilliseconds + " ms for the map switch.");
        }
        catch (Exception error)
        {
            // The switch still runs without the raised threshold, and the reason goes to the log.
            _log("OG Coop: SHVDN script timeout left as is: " + error.GetBaseException().GetType().Name + ": " + error.GetBaseException().Message);
        }
    }

    internal void Restore()
    {
        PropertyInfo? threshold = _threshold;
        if (threshold == null) return;
        object? domain = _domain;
        _threshold = null;
        _domain = null;
        try
        {
            // A threshold someone else changed meanwhile is left alone.
            if ((uint)threshold.GetValue(domain)! != LiftedMilliseconds) return;
            threshold.SetValue(domain, _restore);
            _log("OG Coop: SHVDN script timeout back to " + _restore + " ms.");
        }
        catch (Exception error)
        {
            _log("OG Coop: SHVDN script timeout not restored: " + error.GetBaseException().GetType().Name + ": " + error.GetBaseException().Message);
        }
    }

    private static bool Find(out object? domain, out PropertyInfo? threshold, out string missing)
    {
        domain = null;
        threshold = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType("SHVDN.ScriptDomain", false);
            if (type == null) continue;
            domain = type.GetProperty("CurrentDomain", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            threshold = type.GetProperty("ScriptTimeoutThreshold", BindingFlags.Public | BindingFlags.Instance);
            missing = domain == null ? "no current script domain" :
                threshold == null || threshold.PropertyType != typeof(uint) || !threshold.CanWrite ? "no writable ScriptTimeoutThreshold" : "";
            return missing.Length == 0;
        }

        missing = "SHVDN.ScriptDomain not found";
        return false;
    }
}
