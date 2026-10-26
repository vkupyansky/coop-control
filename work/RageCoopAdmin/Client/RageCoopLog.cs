using System;
using System.Reflection;
using RageCoop.Client;
using RageCoop.Client.Scripting;
using RageCoop.Core;

namespace RageCoopAdmin.Client;

// On disconnect RageCoop stops its log, renames the file to .last.log and asks
// SHVDN to reload by posting Insert. Under CrossOver that reload may not
// happen: the next connection then runs with a stopped log that writes nothing
// and rebuilds its whole capped buffer on every line. Each connection of this
// resource resumes it in the same file, keeping the lines collected meanwhile.
internal static class RageCoopLog
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type? Main = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.Main", false);
    private static readonly FieldInfo? MainLogger = Main?.GetField("Logger", Static);
    private static readonly FieldInfo? MainResources = Main?.GetField("Resources", Static);
    private static readonly FieldInfo? Stopping = typeof(Logger).GetField("Stopping", Instance);
    private static readonly FieldInfo? Buffer = typeof(Logger).GetField("Buffer", Instance);
    private static readonly ConstructorInfo? NewLogger = typeof(Logger).GetConstructor(Instance, null, new[] { typeof(bool), typeof(bool) }, null);
    private static readonly PropertyInfo? ResourceLogger = typeof(ClientResource).GetProperty(nameof(ClientResource.Logger), Instance);

    internal static bool IsAvailable => MainLogger?.FieldType == typeof(Logger) && Stopping?.FieldType == typeof(bool) &&
        Buffer?.FieldType == typeof(string) && NewLogger != null && ResourceLogger?.PropertyType == typeof(Logger) && ResourceLogger.GetSetMethod(true) != null;

    // Returns true when a stopped log was replaced.
    internal static bool Resume(ClientResource? resource)
    {
        if (!IsAvailable || MainLogger!.GetValue(null) is not Logger stopped || !(bool)Stopping!.GetValue(stopped)!) { return false; }
        // Not overwriting: RageCoop already moved the previous session to .last.log.
        var resumed = (Logger)NewLogger!.Invoke(new object[] { false, false });
        resumed.LogLevel = stopped.LogLevel;
        resumed.UseConsole = stopped.UseConsole;
        Buffer!.SetValue(resumed, (string?)Buffer.GetValue(stopped) ?? string.Empty);
        resumed.LogPath = stopped.LogPath;
        MainLogger.SetValue(null, resumed);
        // RageCoop's resource loader and this resource captured the stopped one.
        object? resources = MainResources?.GetValue(null);
        resources?.GetType().GetProperty("Logger", Instance)?.SetValue(resources, resumed);
        if (resource != null) { ResourceLogger!.SetValue(resource, resumed); }
        resumed.Info("OG Coop: RageCoop log resumed; SHVDN did not reload after the previous disconnect.");
        return true;
    }
}
