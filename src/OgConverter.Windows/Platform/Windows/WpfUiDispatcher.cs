using System;
using System.Windows.Threading;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>WPF dispatcher timers: they tick on the thread that created them (the UI thread).</summary>
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        {
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
