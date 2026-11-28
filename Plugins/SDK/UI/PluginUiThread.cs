using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Marshals host service calls onto the WPF dispatcher thread.
    /// Host task and reminder services are backed by observable collections and must be
    /// touched on the UI thread. The helpers fall back to the current thread when no
    /// application or dispatcher is available (for example in tests).
    /// </summary>
    public static class PluginUiThread
    {
        /// <summary>
        /// Runs a synchronous action on the UI thread.
        /// </summary>
        public static void Run(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess() || dispatcher.HasShutdownStarted)
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }

        /// <summary>
        /// Runs a synchronous function on the UI thread and returns its result.
        /// </summary>
        public static T Get<T>(Func<T> func)
        {
            ArgumentNullException.ThrowIfNull(func);

            var dispatcher = Application.Current?.Dispatcher;
            return dispatcher == null || dispatcher.CheckAccess() || dispatcher.HasShutdownStarted
                ? func()
                : dispatcher.Invoke(func);
        }

        /// <summary>
        /// Runs an asynchronous operation on the UI thread and returns a task that
        /// completes when the operation finishes.
        /// </summary>
        public static Task RunAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess() || dispatcher.HasShutdownStarted)
            {
                return action();
            }

            return dispatcher.InvokeAsync(action).Task.Unwrap();
        }
    }
}
