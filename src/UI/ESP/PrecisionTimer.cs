using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoneEftDmaRadar.UI.ESP
{
    /// <summary>
    /// High-precision timer for accurate frame timing using multimedia timer API.
    /// Provides sub-millisecond accuracy unlike standard System.Threading.Timer.
    /// </summary>
    internal sealed class PrecisionTimer : IDisposable
    {
        private readonly Action _callback;
        private readonly TimeSpan _interval;
        private readonly Thread _timerThread;
        private volatile bool _isRunning;
        private readonly Stopwatch _stopwatch = new();

        public event EventHandler Elapsed;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        public PrecisionTimer(TimeSpan interval)
        {
            _interval = interval;
            _isRunning = true;

            // Set Windows to 1ms timer resolution
            TimeBeginPeriod(1);

            _timerThread = new Thread(TimerLoop)
            {
                Name = "PrecisionTimer",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };

            _stopwatch.Start();
            _timerThread.Start();
        }

        private void TimerLoop()
        {
            long lastTicks = _stopwatch.ElapsedTicks;
            double ticksPerMs = Stopwatch.Frequency / 1000.0;
            long targetTicks = _interval == TimeSpan.Zero ? 0 : (long)(_interval.TotalMilliseconds * ticksPerMs);

            while (_isRunning)
            {
                if (_interval == TimeSpan.Zero)
                {
                    // No throttling - run as fast as possible
                    Elapsed?.Invoke(this, EventArgs.Empty);
                    continue;
                }

                long currentTicks = _stopwatch.ElapsedTicks;
                long elapsedTicks = currentTicks - lastTicks;

                if (elapsedTicks >= targetTicks)
                {
                    lastTicks = currentTicks;
                    Elapsed?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    // High-precision sleep
                    long remainingTicks = targetTicks - elapsedTicks;
                    double remainingMs = remainingTicks / ticksPerMs;

                    if (remainingMs > 2.0)
                    {
                        // Sleep for most of the remaining time
                        Thread.Sleep((int)(remainingMs - 1.0));
                    }
                    else if (remainingMs > 0.5)
                    {
                        // Spin-wait for precision
                        SpinWait.SpinUntil(() => _stopwatch.ElapsedTicks - lastTicks >= targetTicks);
                    }
                    // else: very short time, just continue loop
                }
            }
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                _isRunning = false;
                _timerThread?.Join(1000);
                TimeEndPeriod(1);
                _stopwatch.Stop();
            }
        }
    }
}
