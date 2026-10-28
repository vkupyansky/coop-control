using System;
using System.Collections.Generic;

namespace RageCoopAdmin.Client;

// The server drops a request that arrives before it marked the player ready, and the
// garage, character, yacht and home would then be missing for the whole session.
internal sealed class AnswerRetry
{
    internal const long RetryMilliseconds = 3000;
    internal const int MaximumAttempts = 5;
    private readonly List<Pending> _pending = new List<Pending>();

    internal int Count => _pending.Count;

    // The caller has already sent the first request.
    internal void Await(string name, Action send, Func<bool> answered, long now) =>
        _pending.Add(new Pending(name, send, answered, now));

    internal void Clear() => _pending.Clear();

    internal void Tick(long now, Action<string> gaveUp)
    {
        for (int index = _pending.Count - 1; index >= 0; index--)
        {
            Pending pending = _pending[index];
            if (pending.Answered())
            {
                _pending.RemoveAt(index);
                continue;
            }

            if (now - pending.SentAt < RetryMilliseconds) continue;
            if (pending.Attempts >= MaximumAttempts)
            {
                _pending.RemoveAt(index);
                gaveUp(pending.Name);
                continue;
            }

            pending.Send();
            pending.Attempts++;
            pending.SentAt = now;
        }
    }

    private sealed class Pending
    {
        internal Pending(string name, Action send, Func<bool> answered, long sentAt)
        {
            Name = name;
            Send = send;
            Answered = answered;
            SentAt = sentAt;
            Attempts = 1;
        }

        internal string Name { get; }
        internal Action Send { get; }
        internal Func<bool> Answered { get; }
        internal long SentAt { get; set; }
        internal int Attempts { get; set; }
    }
}
