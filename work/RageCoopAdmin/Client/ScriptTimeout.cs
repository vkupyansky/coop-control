using System;
using System.Reflection;

namespace RageCoopAdmin.Client;

// SHVDN снимает скрипт, если его кадр шёл дольше ScriptTimeoutThreshold (по умолчанию 5 с).
// ON_ENTER_MP на медленном ПК идёт дольше, и RageCoop отключался с причиной Abort.
// Порог сверяется после кадра, поэтому его поднимают до вызова и возвращают в следующих кадрах
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
            // без поднятого порога переключение всё равно идёт, причина остаётся в журнале
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
            // порог, который кто-то поменял за это время, не трогаем
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
