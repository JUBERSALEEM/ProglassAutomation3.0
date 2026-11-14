// ═══════════════════════════════════════════════════════════════════════
// FILE: Utilities/SubscriptionManager.cs
// PROJECT: ProGlassAutomation
//
// Small disposable subscription container for ViewModels that must watch
// several INotifyPropertyChanged / INotifyCollectionChanged sources across
// rebinds (e.g. Step 9 watches Step 4 panels, Step 5 groups/rows and Step 6
// charge rows). Every subscription returns a token that can be disposed
// individually, and Clear() detaches everything at once — so re-binding can
// never leave duplicate or orphaned handlers behind.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ProGlassAutomation.Utilities
{
    public sealed class SubscriptionManager : IDisposable
    {
        private readonly List<Action> _unsubscribers = new();

        public IDisposable OnPropertyChanged(
            INotifyPropertyChanged? source,
            PropertyChangedEventHandler handler)
        {
            return Subscribe(
                source == null || handler == null
                    ? null
                    : () => source.PropertyChanged -= handler,
                source == null || handler == null
                    ? null
                    : () => source.PropertyChanged += handler);
        }

        public IDisposable OnCollectionChanged(
            INotifyCollectionChanged? source,
            NotifyCollectionChangedEventHandler handler)
        {
            return Subscribe(
                source == null || handler == null
                    ? null
                    : () => source.CollectionChanged -= handler,
                source == null || handler == null
                    ? null
                    : () => source.CollectionChanged += handler);
        }

        private IDisposable Subscribe(Action? unsubscribe, Action? subscribe)
        {
            subscribe?.Invoke();

            if (unsubscribe == null)
                return new Token(() => { });

            var token = new Token(unsubscribe);
            _unsubscribers.Add(token.Dispose);
            return token;
        }

        /// <summary>Detaches every registered handler. Safe to call repeatedly.</summary>
        public void Clear()
        {
            foreach (var unsubscribe in _unsubscribers)
            {
                try { unsubscribe(); }
                catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("foreach suppressed");
            }
            }

            _unsubscribers.Clear();
        }

        public void Dispose()
        {
            Clear();
        }

        private sealed class Token : IDisposable
        {
            private Action? _unsubscribe;

            public Token(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                var action = _unsubscribe;
                _unsubscribe = null;
                if (action != null)
                {
                    try { action(); }
                    catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("if suppressed");
            }
                }
            }
        }
    }
}
