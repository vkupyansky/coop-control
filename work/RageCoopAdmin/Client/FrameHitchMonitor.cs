using System;
using System.Globalization;

namespace RageCoopAdmin.Client;

// Freeze diagnostics for the client log. Scripts run once per game frame, so a
// long gap between two script frames means the game itself stood still: it is
// logged at once with what was going on. Time with the game's window in the
// background is not a freeze (GTA stops its frames then) and is logged apart.
// A line per minute gives the baseline to compare against: frame rate, slow
// frames and the game's pending loads. Pure logic: the caller supplies the time,
// the pending-load count, the time the window was inactive and the context.
internal sealed class FrameHitchMonitor
{
    internal const long FreezeMilliseconds = 400;
    internal const long SlowFrameMilliseconds = 100;
    internal const long SummaryMilliseconds = 60000;

    private readonly Action<string> _log;
    private long _lastFrame = -1;
    private long _windowStart;
    private int _frames;
    private int _slowFrames;
    private int _freezes;
    private long _worstFrame;
    private long _loadingSum;
    private int _loadingMax;
    private long _away;

    internal FrameHitchMonitor(Action<string> log) => _log = log;

    internal void Frame(long now, int loading, long inactive, Func<string> context)
    {
        if (_lastFrame < 0 || now < _lastFrame)
        {
            Start(now);
            return;
        }

        long gap = now - _lastFrame;
        long frame = Math.Max(0, gap - Math.Max(0, inactive));
        _lastFrame = now;
        _away += gap - frame;
        _frames++;
        loading = Math.Max(0, loading);
        _loadingSum += loading;
        _loadingMax = Math.Max(_loadingMax, loading);
        _worstFrame = Math.Max(_worstFrame, frame);
        if (frame >= SlowFrameMilliseconds) _slowFrames++;
        if (frame >= FreezeMilliseconds)
        {
            _freezes++;
            _log("OG Coop: freeze " + frame + " ms" + (frame < gap ? " (plus " + (gap - frame) + " ms with the window inactive)" : "") +
                "; pending loads " + loading + "; " + context());
        }
        else if (gap >= FreezeMilliseconds)
        {
            _log("OG Coop: game window inactive " + gap + " ms, not a freeze.");
        }

        // Played time only: time with the window in the background is left out.
        long window = now - _windowStart - _away;
        if (window < SummaryMilliseconds) return;
        _log("OG Coop: last " + window / 1000 + " s: " +
            (_frames * 1000.0 / window).ToString("0.0", CultureInfo.InvariantCulture) + " fps; slow frames " + _slowFrames +
            "; freezes " + _freezes + "; worst frame " + _worstFrame + " ms; pending loads avg " + _loadingSum / _frames +
            ", max " + _loadingMax + (_away > 0 ? "; window inactive " + _away / 1000 + " s more" : "") + "; " + context());
        Start(now);
    }

    // Leaving the server or a long stop: the next frame starts a new measurement
    // instead of reporting the time away as a freeze.
    internal void Reset() => _lastFrame = -1;

    private void Start(long now)
    {
        _lastFrame = now;
        _windowStart = now;
        _frames = 0;
        _slowFrames = 0;
        _freezes = 0;
        _worstFrame = 0;
        _loadingSum = 0;
        _loadingMax = 0;
        _away = 0;
    }
}
