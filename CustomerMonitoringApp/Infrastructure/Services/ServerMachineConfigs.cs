using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Timers;

namespace CustomerMonitoringApp.Infrastructure.Services
{
    /// <summary>
    /// Monitors server machine configuration, performs regular cleanup of memory resources,
    /// and provides logging for system health. Implements IDisposable for proper resource management.
    /// </summary>
    public class ServerMachineConfigs : IDisposable
    {
        // Private members for resource cleanup and diagnostics tracking
        private readonly System.Timers.Timer _cleanupTimer;
        private readonly List<PerformanceCounter> _performanceCounters;
        private readonly TimeSpan _timerInterval;

        /// <summary>
        /// Initializes the ServerMachineConfigs instance with a configurable cleanup timer.
        /// </summary>
        /// <param name="intervalMinutes">Interval in minutes for performing cleanup operations.</param>
        public ServerMachineConfigs(int intervalMinutes = 10)
        {
            // Set interval based on constructor parameter (default 10 minutes)
            _timerInterval = TimeSpan.FromMinutes(intervalMinutes);
            _cleanupTimer = new System.Timers.Timer(_timerInterval.TotalMilliseconds) { AutoReset = true };
            _cleanupTimer.Elapsed += OnCleanupEvent;
            _cleanupTimer.Start();

            // Initialize and configure performance counters
            _performanceCounters = InitializePerformanceCounters();

            Console.WriteLine("ServerMachineConfigs initialized with cleanup interval: " + _timerInterval);
        }

        /// <summary>
        /// Event triggered for each timer interval to perform system cleanup and log diagnostics.
        /// </summary>
        private void OnCleanupEvent(object sender, ElapsedEventArgs e)
        {
            try
            {
                Console.WriteLine("Running cleanup operation at " + DateTime.Now);

                // Memory Cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Diagnostic Logging
                LogDiagnostics();
            }
            catch (Exception ex)
            {
                // Improved error handling with timestamp and detailed message
                Console.WriteLine($"[ERROR] Cleanup failed at {DateTime.Now}: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs diagnostics related to CPU and memory usage.
        /// </summary>
        private void LogDiagnostics()
        {
            foreach (var counter in _performanceCounters)
            {
                try
                {
                    // Retrieve the current value and log it
                    var value = counter.NextValue();
                    Console.WriteLine($"[{counter.CounterName}]: {value:F2}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Unable to read {counter.CounterName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Initializes performance counters for monitoring CPU and memory.
        /// </summary>
        /// <returns>List of performance counters to monitor.</returns>
        private List<PerformanceCounter> InitializePerformanceCounters()
        {
            return new List<PerformanceCounter>
            {
                new PerformanceCounter("Processor", "% Processor Time", "_Total"),
                new PerformanceCounter("Memory", "Available MBytes")
            };
        }

        private bool _disposed = false; // To track if Dispose has already been called

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // Prevents the finalizer from being called if Dispose was already done
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // Dispose managed resources
                _cleanupTimer.Stop();
                _cleanupTimer.Dispose();

                foreach (var counter in _performanceCounters)
                {
                    counter.Dispose();
                }
            }

            // Dispose unmanaged resources here (if any)

            _disposed = true;
            Console.WriteLine("ServerMachineConfigs disposed.");
        }
    }
}
