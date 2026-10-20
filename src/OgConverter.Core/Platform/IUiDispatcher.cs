using System;
using System.Threading;

namespace OgConverter.Services
{
    /// <summary>A repeating timer, created stopped. Starting a running timer restarts its interval.</summary>
    public interface IUiTimer
    {
        void Start();
        void Stop();
    }

    /// <summary>Work that must happen on the UI thread.</summary>
    public interface IUiDispatcher
    {
        /// <summary>A stopped timer whose <paramref name="tick"/> runs on the UI thread of the caller.</summary>
        IUiTimer CreateTimer(TimeSpan interval, Action tick);
    }

    /// <summary>Default when there's no UI (scripts, tests): timers tick on the thread pool.</summary>
    public sealed class ThreadPoolUiDispatcher : IUiDispatcher
    {
        public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new PoolTimer(interval, tick);

        private sealed class PoolTimer : IUiTimer
        {
            private readonly TimeSpan _interval;
            private readonly Timer _timer;

            public PoolTimer(TimeSpan interval, Action tick)
            {
                _interval = interval;
                _timer = new Timer(_ => tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            public void Start() => _timer.Change(_interval, _interval);
            public void Stop() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
