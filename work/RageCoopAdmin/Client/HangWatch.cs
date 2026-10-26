using System;
using System.Threading;

namespace RageCoopAdmin.Client;

// Freeze diagnostics for a game that stops for good: the freeze log
// (FrameHitchMonitor) reports a stop at the next frame, which then never comes.
// The window clock's timer thread checks that our frames still arrive; after
// ten seconds without one, with the window in front, it logs the step of our
// frame and the RageCoop ticks still running. RageCoop's logger writes the line
// to the file from its own thread while the game stands still.
internal sealed class HangWatch
{
    internal const long ReportMilliseconds = 10_000;
    private readonly object _gate = new object();
    private readonly Action<string> _log;
    private readonly Func<string> _running;
    private long _frames;
    private long _seen = -1;
    private long _stalled;
    private bool _reported;
    private volatile string _step = "start";

    internal HangWatch(Action<string> log, Func<string> running)
    {
        _log = log;
        _running = running;
    }

    // Game thread: a frame on the server, and the step it is in.
    internal void Frame() => Interlocked.Increment(ref _frames);
    internal void Step(string step) => _step = step;

    // Off the server no frames are counted until the next joining.
    internal void Reset()
    {
        Interlocked.Exchange(ref _frames, 0);
        _step = "start";
    }

    // Timer thread, every `elapsed` milliseconds.
    internal void Check(long elapsed, bool inFront)
    {
        lock (_gate)
        {
            long frames = Interlocked.Read(ref _frames);
            if (frames != _seen)
            {
                _seen = frames;
                _stalled = 0;
                _reported = false;
                return;
            }

            if (frames == 0 || _reported) return;
            if (inFront) _stalled += elapsed;
            if (_stalled < ReportMilliseconds) return;
            _reported = true;
            _log("OG Coop: game stopped: no frame for " + _stalled / 1000 + " s with the window in front; OG Coop step: " +
                _step + "; RageCoop ticks running: " + _running());
        }
    }
}
