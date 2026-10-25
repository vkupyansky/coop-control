using System;
using System.Collections.Generic;
using System.Linq;
using RageCoopAdmin.Client;

// Freeze log: the real monitor runs on a simulated clock; nothing touches GTA.
internal static class Program
{
    private static int Passes;
    private static int ContextCalls;

    private static int Main()
    {
        try
        {
            CheckFreezes();
            CheckSummary();
            CheckWindowAway();
            CheckRestart();
            CheckHangWatch();
            Console.WriteLine("Frame hitch checks passed: " + Passes + ". The real monitor runs on a simulated clock.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string Context()
    {
        ContextCalls++;
        return "ctx";
    }

    private static void CheckFreezes()
    {
        var logs = new List<string>();
        var monitor = new FrameHitchMonitor(logs.Add);
        ContextCalls = 0;
        long now = 1000;
        monitor.Frame(now, 3, 0, Context);
        for (int i = 0; i < 100; i++) monitor.Frame(now += 16, 3, 0, Context);
        Check(logs.Count == 0 && ContextCalls == 0, "smooth frames log nothing and do not build the context");
        monitor.Frame(now += FrameHitchMonitor.FreezeMilliseconds - 1, 3, 0, Context);
        Check(logs.Count == 0, "a frame just under the freeze threshold is not a freeze");
        monitor.Frame(now += FrameHitchMonitor.FreezeMilliseconds, 57, 0, Context);
        monitor.Frame(now += 4800, 210, 0, Context);
        Check(logs.Count == 2 && ContextCalls == 2, "each frame at or over the threshold is logged once with its context");
        Check(logs[1] == "Coop Control: freeze 4800 ms; pending loads 210; ctx", "a freeze line names its length, the pending loads and the context");
    }

    // The stopped-game watch runs on its timer's simulated steps.
    private static void CheckHangWatch()
    {
        var logs = new List<string>();
        var watch = new HangWatch(logs.Add, () => "Main resources");
        for (int i = 0; i < 100; i++) watch.Check(250, true);
        Check(logs.Count == 0, "before the first frame on the server nothing is reported");
        watch.Frame();
        watch.Step("map load");
        for (int i = 0; i < 39; i++) watch.Check(250, true);
        Check(logs.Count == 0, "a stop shorter than ten seconds is left to the freeze log");
        for (int i = 0; i < 40; i++) watch.Check(250, false);
        Check(logs.Count == 0, "time with the window in the background does not count");
        watch.Check(250, true);
        watch.Check(250, true);
        Check(logs.Count == 1 && logs[0] == "Coop Control: game stopped: no frame for 10 s with the window in front; Coop Control step: map load; RageCoop ticks running: Main resources",
            "a stop is reported once while it lasts, with our step and the running RageCoop ticks");
        watch.Frame();
        watch.Check(250, true);
        for (int i = 0; i < 40; i++) watch.Check(250, true);
        Check(logs.Count == 2, "after frames resume, a new stop is reported again");
        watch.Reset();
        for (int i = 0; i < 100; i++) watch.Check(250, true);
        Check(logs.Count == 2, "off the server no frames are expected and nothing is reported");
    }

    private static void CheckSummary()
    {
        var logs = new List<string>();
        var monitor = new FrameHitchMonitor(logs.Add);
        long now = 0;
        monitor.Frame(now, 0, 0, Context);
        // 59.5 s of 50 ms frames (20 fps), one 150 ms slow frame, one 1 s freeze, then the minute closes.
        for (int i = 0; i < 1170; i++) monitor.Frame(now += 50, i % 2 == 0 ? 10 : 30, 0, Context);
        monitor.Frame(now += 150, 20, 0, Context);
        monitor.Frame(now += 1000, 400, 0, Context);
        Check(logs.Count == 1 && logs[0].StartsWith("Coop Control: freeze 1000 ms", StringComparison.Ordinal), "the freeze is logged before the minute closes");
        while (now < FrameHitchMonitor.SummaryMilliseconds) monitor.Frame(now += 50, 10, 0, Context);
        Check(logs.Count == 2, "a summary line closes the minute");
        string summary = logs[1];
        Check(summary.StartsWith("Coop Control: last 60 s: ", StringComparison.Ordinal) && summary.Contains(" fps; slow frames 2; freezes 1; worst frame 1000 ms;") &&
            summary.Contains("pending loads avg ") && summary.Contains(", max 400; ctx"),
            "the summary counts slow frames (the freeze too), freezes, the worst frame and the pending-load peak: " + summary);
        string fps = summary.Substring("Coop Control: last 60 s: ".Length).Split(' ')[0];
        Check(fps.Contains('.') && !fps.Contains(',') && double.Parse(fps, System.Globalization.CultureInfo.InvariantCulture) is > 19.0 and < 21.0,
            "frame rate is written with a dot and matches the simulated 20 fps: " + fps);
        long next = now + FrameHitchMonitor.SummaryMilliseconds - 100;
        while (now < next) monitor.Frame(now += 16, 0, 0, Context);
        Check(logs.Count == 2, "the next summary waits for a full new minute");
        while (logs.Count == 2) monitor.Frame(now += 16, 0, 0, Context);
        Check(logs[2].Contains("slow frames 0; freezes 0;"), "each minute counts afresh");
    }

    private static void CheckWindowAway()
    {
        var logs = new List<string>();
        var monitor = new FrameHitchMonitor(logs.Add);
        long now = 0;
        monitor.Frame(now, 0, 0, Context);
        monitor.Frame(now += 120_000, 0, 119_750, Context);
        Check(logs.SequenceEqual(new[] { "Coop Control: game window inactive 120000 ms, not a freeze." }),
            "two minutes with the window in the background are logged as time away, not as a freeze");
        monitor.Frame(now += 9_000, 0, 1_000, Context);
        Check(logs.Count == 2 && logs[1] == "Coop Control: freeze 8000 ms (plus 1000 ms with the window inactive); pending loads 0; ctx",
            "a real freeze counts only the time the window was in front");
        while (logs.Count == 2) monitor.Frame(now += 16, 0, 0, Context);
        Check(logs[2].StartsWith("Coop Control: last 60 s: ", StringComparison.Ordinal) && logs[2].Contains("freezes 1;") &&
            logs[2].Contains("worst frame 8000 ms;") && logs[2].Contains("; window inactive 120 s more; "),
            "the minute covers played time only, counts only the real freeze and notes the time away");
    }

    private static void CheckRestart()
    {
        var logs = new List<string>();
        var monitor = new FrameHitchMonitor(logs.Add);
        long now = 0;
        monitor.Frame(now, 0, 0, Context);
        monitor.Frame(now += 16, 0, 0, Context);
        monitor.Reset();
        monitor.Frame(now += 120_000, 0, 0, Context);
        monitor.Frame(now += 16, 0, 0, Context);
        Check(logs.Count == 0, "time off the server after Reset is neither a freeze nor a summary");
        monitor.Frame(now - 5000, 0, 0, Context);
        monitor.Frame(now - 4984, 0, 0, Context);
        Check(logs.Count == 0, "a clock that goes back restarts the measurement instead of logging");
        Check(new[] { FrameHitchMonitor.SlowFrameMilliseconds, FrameHitchMonitor.FreezeMilliseconds, FrameHitchMonitor.SummaryMilliseconds }.SequenceEqual(new long[] { 100, 400, 60000 }),
            "thresholds: slow frame 100 ms, freeze 400 ms, summary every minute");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL " + message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
