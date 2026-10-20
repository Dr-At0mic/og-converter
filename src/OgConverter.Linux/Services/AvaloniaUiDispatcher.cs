using System;
using Avalonia.Threading;

namespace OgConverter.Services
{
    /// <summary><see cref="IUiDispatcher"/> on Avalonia's dispatcher, so Core timers (settings saves) tick on the UI thread.</summary>
    public sealed class AvaloniaUiDispatcher : IUiDispatcher
    {
        public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new Timer(interval, tick);

        private sealed class Timer : IUiTimer
        {
            private readonly DispatcherTimer _timer;

            public Timer(TimeSpan interval, Action tick)
            {
                _timer = new DispatcherTimer { Interval = interval };
                _timer.Tick += (_, _) => tick();
            }

            public void Start()
            {
                // Restart the interval, like the Windows timer
                _timer.Stop();
                _timer.Start();
            }

            public void Stop() => _timer.Stop();
        }
    }
}
