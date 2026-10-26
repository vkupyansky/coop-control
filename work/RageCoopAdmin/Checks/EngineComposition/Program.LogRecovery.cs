using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using RageCoop.Client.Scripting;
using RageCoop.Core;

internal static partial class Program
{
    private static void CheckLogRecovery()
    {
        MethodInfo resume = Resource.GetType("RageCoopAdmin.Client.RageCoopLog", true)!.GetMethod("Resume", Static)!;
        FieldInfo mainLogger = Engine.GetType("RageCoop.Client.Main", true)!.GetField("Logger", Static)!;
        object? previous = mainLogger.GetValue(null);
        string folder = Path.Combine(Path.GetTempPath(), "ogcoop-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "RageCoop.Client.log");
        Logger? resumed = null;
        try
        {
            var stopped = (Logger)typeof(Logger).GetConstructor(Instance, null, new[] { typeof(bool), typeof(bool) }, null)!.Invoke(new object[] { false, false });
            stopped.LogPath = path;
            stopped.Dispose();
            stopped.Info("written while RageCoop's log was stopped");
            mainLogger.SetValue(null, stopped);
            var resource = (ClientResource)FormatterServices.GetUninitializedObject(typeof(ClientResource));
            Assert((bool)resume.Invoke(null, new object[] { resource })!, "A stopped RageCoop log is resumed when the resource connects again.");
            resumed = (Logger)mainLogger.GetValue(null)!;
            Assert(!ReferenceEquals(resumed, stopped) && ReferenceEquals(resource.Logger, resumed) && resumed.LogPath == path,
                "The resumed log replaces RageCoop's and this resource's stopped one and writes to the same file.");
            resumed.Flush();
            string text = File.ReadAllText(path);
            Assert(text.Contains("written while RageCoop's log was stopped") && text.Contains("RageCoop log resumed"),
                "Lines collected while the log was stopped reach the file, followed by an explanation.");
            Assert(!(bool)resume.Invoke(null, new object[] { resource })!, "A running RageCoop log is left as it is.");
        }
        finally
        {
            resumed?.Dispose();
            mainLogger.SetValue(null, previous);
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
        Console.WriteLine("Stopped RageCoop log recovery passed on the real RageCoop logger.");
    }
}
