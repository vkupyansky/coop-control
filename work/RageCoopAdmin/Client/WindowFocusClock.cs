using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RageCoopAdmin.Client;

// Freeze diagnostics: GTA stops its frames while its window is in the background
// (another app in front, the window hidden), and such a gap looked like a
// two-minute freeze in the log. A timer thread counts how long the game's window
// was not in front, so the freeze log tells time away from a real freeze.
internal static class WindowFocusClock
{
    internal const int StepMilliseconds = 250;
    private static readonly uint Game = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    private static Timer? _timer;
    private static HangWatch? _hangWatch;
    private static long _inactive;

    // The same timer checks that the game's frames still arrive.
    internal static void Start(HangWatch hangWatch)
    {
        if (_timer != null) return;
        _hangWatch = hangWatch;
        _timer = new Timer(_ => Step(), null, StepMilliseconds, StepMilliseconds);
    }

    internal static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _hangWatch = null;
        Take();
    }

    // Milliseconds the window was not in front since the previous call.
    internal static long Take() => Interlocked.Exchange(ref _inactive, 0);

    private static void Step()
    {
        bool inFront = IsGameInFront();
        if (!inFront) Interlocked.Add(ref _inactive, StepMilliseconds);
        _hangWatch?.Check(StepMilliseconds, inFront);
    }

    private static bool IsGameInFront()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint owner);
        return owner == Game;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
