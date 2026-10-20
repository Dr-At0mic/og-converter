using System;
using Avalonia;
using Avalonia.Threading;
using OgConverter.Services;

namespace OgConverter.Platform.Linux
{
    /// <summary>
    /// Avalonia dispatcher timers once the app's UI is running (they tick on the UI thread); before that, and in the
    /// headless modes (<c>--test</c>, <c>--convert</c>), thread-pool timers.
    /// </summary>
    public sealed class AvaloniaUiDispatcher : IUiDispatcher
    {
        private readonly ThreadPoolUiDispatcher _headless = new();

        public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        {
            if (Application.Current == null) return _headless.CreateTimer(interval, tick);
            var timer = new DispatcherTimer { Interval = interval };
            timer.Tick += (_, _) => tick();
            return new Timer(timer);
        }

        private sealed class Timer : IUiTimer
        {
            private readonly DispatcherTimer _timer;

            public Timer(DispatcherTimer timer) => _timer = timer;

            public void Start() => _timer.Start();
            public void Stop() => _timer.Stop();
        }
    }
}
