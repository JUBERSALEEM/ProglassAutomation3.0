// Cross-module signal: PI / JO / DN / TI lists recompute derived status
// when any related document is created or deleted.
using System;
using ProGlassAutomation.Services.Logging;
using System.Threading.Tasks;
using System.Windows;

namespace ProGlassAutomation.Services.Database
{
    public static class DocumentLifecycle
    {
        public static event Action? Changed;

        // Maps each subscribed reload delegate to the wrapper we actually
        // attached to Changed, so Unsubscribe can detach it again. Without
        // this, the anonymous wrapper in Subscribe was impossible to remove
        // (a leak) and Unsubscribe() could not exist (CS0117).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Func<Task>, Action>
            _subscriptions = new();

        public static void Raise()
        {
            try { Changed?.Invoke(); }
            catch { /* a listener must never undo a completed write */ }
        }

        public static void Subscribe(Func<Task> reload)
        {
            if (reload == null) return;

            Action handler = () =>
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    _ = Safe(reload);
                    return;
                }

                if (dispatcher.CheckAccess())
                    _ = Safe(reload);
                else
                    dispatcher.InvokeAsync(() => Safe(reload));
            };

            _subscriptions[reload] = handler;
            Changed += handler;
        }

        /// <summary>
        /// Detaches a previously subscribed reload delegate. Idempotent.
        /// </summary>
        public static void Unsubscribe(Func<Task> reload)
        {
            if (reload == null) return;

            if (_subscriptions.TryRemove(reload, out var handler))
                Changed -= handler;
        }

        private static async Task Safe(Func<Task> reload)
        {
            try { await reload(); }
            catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ex.Message}"); }
        }
    }
}
