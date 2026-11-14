// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Utilities/BulkObservableCollection.cs                                   ║
// ║  ProGlass Automation                                                      ║
// ║                                                                           ║
// ║  CHAT 20 — Performance collection for bulk paste + generate operations   ║
// ║                                                                           ║
// ║  ROOT CAUSE SOLVED (AUDIT FIX):                                          ║
// ║    Original AddRangeSilent called Items.Add() which routes through        ║
// ║    ObservableCollection<T>.InsertItem() — fires CollectionChanged per    ║
// ║    item. Completely defeated the purpose of silent addition.             ║
// ║                                                                           ║
// ║  CORRECT FIX:                                                            ║
// ║    Override InsertItem() with _suppressNotifications flag.               ║
// ║    When suppressed: calls Items.Insert() directly on the underlying      ║
// ║    List<T> — bypasses ObservableCollection's notification path.          ║
// ║    Zero CollectionChanged events during bulk add.                        ║
// ║    Single RaiseReset() notifies WPF after all items are added.           ║
// ║                                                                           ║
// ║  PERFORMANCE CONTRACT:                                                    ║
// ║    AddRangeSilent(5000 items) + RaiseReset() = 1 UI notification.        ║
// ║    vs original: 5000 individual CollectionChanged events.                ║
// ║                                                                           ║
// ║  THREAD SAFETY:                                                           ║
// ║    AddRangeSilent — must be called on UI thread (same as base class).    ║
// ║    RaiseReset     — safe from any thread, marshals to UI dispatcher.     ║
// ║    ClearSilent    — must be called on UI thread.                         ║
// ║                                                                           ║
// ║  IMPORTANT — CALLER RESPONSIBILITY:                                      ║
// ║    AddRangeSilent suppresses CollectionChanged for added items.          ║
// ║    This means CollectionChanged.NewItems is never populated for          ║
// ║    silently-added rows. Any PropertyChanged subscriptions that           ║
// ║    normally happen in CollectionChanged handlers MUST be done            ║
// ║    manually by the caller after AddRangeSilent returns.                  ║
// ║    See SpecGroup.AddRows() and PasteFromTextAsync() for examples.        ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace ProGlassAutomation.Utilities
{
    /// <summary>
    /// A drop-in replacement for <see cref="ObservableCollection{T}"/> that adds
    /// two performance-critical operations for bulk data scenarios:
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="AddRangeSilent"/> — adds many items without firing a
    ///       <see cref="INotifyCollectionChanged.CollectionChanged"/> event per item.
    ///       Override of <see cref="InsertItem"/> ensures truly zero notifications
    ///       during the bulk add operation.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="RaiseReset"/> — fires a single
    ///       <see cref="NotifyCollectionChangedAction.Reset"/> notification after
    ///       bulk operations complete, telling WPF to re-read the entire collection
    ///       in one efficient pass.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// <strong>How suppression works:</strong><br/>
    /// <see cref="AddRangeSilent"/> sets an internal <c>_suppressNotifications</c>
    /// flag before adding items. The overridden <see cref="InsertItem"/> checks
    /// this flag — when set, it calls <c>Items.Insert()</c> directly on the
    /// underlying <see cref="List{T}"/>, bypassing
    /// <see cref="ObservableCollection{T}"/>'s notification path entirely.
    /// </para>
    ///
    /// <para>
    /// <strong>Caller responsibility:</strong><br/>
    /// Because <see cref="CollectionChanged"/> is suppressed during
    /// <see cref="AddRangeSilent"/>, any <c>PropertyChanged</c> event
    /// subscriptions that your <c>CollectionChanged</c> handler normally
    /// sets up for new items MUST be done manually after
    /// <see cref="AddRangeSilent"/> returns. See <c>SpecGroup.AddRows()</c>
    /// for the correct pattern.
    /// </para>
    ///
    /// <para>
    /// <strong>Thread safety:</strong><br/>
    /// <see cref="AddRangeSilent"/> and <see cref="ClearSilent"/> must be called
    /// on the UI thread — same constraint as the base class.<br/>
    /// <see cref="RaiseReset"/> is safe from any thread and automatically
    /// marshals to the UI dispatcher when called from a background thread.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    public sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        // ══════════════════════════════════════════════════════════════════
        // SUPPRESS FLAG
        // When true, InsertItem bypasses ObservableCollection notifications.
        // Set only within AddRangeSilent — always cleared in finally block.
        // ══════════════════════════════════════════════════════════════════

        private bool _suppressNotifications = false;

        // ══════════════════════════════════════════════════════════════════
        // CONSTRUCTORS
        // Mirror ObservableCollection<T> constructors for drop-in use.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initialises an empty <see cref="BulkObservableCollection{T}"/>.
        /// </summary>
        public BulkObservableCollection() { }

        /// <summary>
        /// Initialises a <see cref="BulkObservableCollection{T}"/> containing
        /// elements copied from the specified collection.
        /// </summary>
        /// <param name="collection">
        /// The collection from which elements are copied.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="collection"/> is null.
        /// </exception>
        public BulkObservableCollection(IEnumerable<T> collection)
            : base(collection) { }

        // ══════════════════════════════════════════════════════════════════
        // INSERTITEM OVERRIDE — core of the suppression mechanism
        //
        // Called by ObservableCollection<T> for every Add/Insert operation.
        // When _suppressNotifications is true:
        //   → Items.Insert() goes directly to List<T>.Insert()
        //   → Does NOT call this override again (not recursive)
        //   → Zero CollectionChanged, Zero PropertyChanged fired
        // When _suppressNotifications is false:
        //   → base.InsertItem() — standard ObservableCollection path
        //   → Fires CollectionChanged(Add) as normal
        // ══════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        protected override void InsertItem(int index, T item)
        {
            if (_suppressNotifications)
            {
                // Items is the protected IList<T> from Collection<T>.
                // Items.Insert calls List<T>.Insert directly —
                // does NOT re-enter this InsertItem override.
                // Zero notifications fired.
                Items.Insert(index, item);
                return;
            }

            // Normal path — fires CollectionChanged(Add) + PropertyChanged
            base.InsertItem(index, item);
        }

        // ══════════════════════════════════════════════════════════════════
        // BULK OPERATIONS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds all items in <paramref name="items"/> to the collection
        /// WITHOUT firing <see cref="INotifyCollectionChanged.CollectionChanged"/>
        /// for each individual item.
        ///
        /// <para>
        /// After this call, you MUST:
        /// <list type="number">
        ///   <item>
        ///     <description>
        ///       Manually subscribe any <c>PropertyChanged</c> handlers for
        ///       the newly added items (because <c>CollectionChanged.NewItems</c>
        ///       is never populated during silent add).
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <description>
        ///       Call <see cref="RaiseReset"/> to notify WPF of the bulk change.
        ///     </description>
        ///   </item>
        /// </list>
        /// </para>
        ///
        /// <para>
        /// <strong>Thread safety:</strong> Must be called on the UI thread.
        /// </para>
        /// </summary>
        /// <param name="items">Items to add. Must not be null.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="items"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when called from within a CollectionChanged event handler
        /// (reentrancy protection inherited from ObservableCollection).
        /// </exception>
        public void AddRangeSilent(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            // Prevent reentrancy — inherited from ObservableCollection<T>.
            // Throws if called from within a CollectionChanged handler.
            CheckReentrancy();

            _suppressNotifications = true;
            try
            {
                foreach (var item in items)
                {
                    // Add() → InsertItem() → suppressed → Items.Insert()
                    // Zero notifications per item.
                    Add(item);
                }
            }
            finally
            {
                // ALWAYS restore — even if an exception occurs mid-add.
                // Leaves collection in consistent state.
                _suppressNotifications = false;
            }
        }

        /// <summary>
        /// Fires a single <see cref="NotifyCollectionChangedAction.Reset"/>
        /// notification, instructing all bound WPF controls to discard their
        /// current item containers and re-read the collection from scratch.
        ///
        /// <para>
        /// Call this after <see cref="AddRangeSilent"/> or after bulk field
        /// mutations (e.g., <c>SetAllSilent</c>) to synchronise the UI in
        /// one efficient pass instead of per-item updates.
        /// </para>
        ///
        /// <para>
        /// <strong>Performance:</strong> Triggers one layout pass regardless
        /// of collection size. With virtualization active, WPF creates
        /// containers only for visible items after Reset.
        /// </para>
        ///
        /// <para>
        /// <strong>Thread safety:</strong> Safe from any thread. Automatically
        /// marshals to the UI dispatcher synchronously when called from a
        /// background thread.
        /// </para>
        /// </summary>
        public void RaiseReset()
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                // Already on UI thread — fire directly and synchronously.
                RaiseResetCore();
            }
            else
            {
                // Background thread — marshal to UI thread synchronously.
                // Synchronous (Invoke not InvokeAsync) so the caller knows
                // the UI has been updated before continuing. This matters
                // for AutoAddRowAndFocus which needs the new container
                // to exist before it tries to focus it.
                dispatcher.Invoke(RaiseResetCore, DispatcherPriority.DataBind);
            }
        }

        /// <summary>
        /// Clears all items silently and fires a single Reset notification.
        ///
        /// <para>
        /// More efficient than <see cref="ObservableCollection{T}.Clear"/>
        /// for large collections because it bypasses per-item Remove
        /// notifications from <c>ClearItems()</c>.
        /// </para>
        ///
        /// <para>
        /// <strong>Thread safety:</strong> Must be called on the UI thread.
        /// </para>
        /// </summary>
        public void ClearSilent()
        {
            CheckReentrancy();

            // Items.Clear() calls List<T>.Clear() directly —
            // bypasses ObservableCollection.ClearItems() which would
            // fire per-item Remove notifications.
            Items.Clear();

            // Single Reset notifies WPF to discard all containers.
            RaiseResetCore();
        }

        // ══════════════════════════════════════════════════════════════════
        // PRIVATE CORE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Internal Reset implementation. Must be called on the UI thread.
        ///
        /// Fires in correct order:
        ///   1. PropertyChanged("Count")   — bound Count displays update
        ///   2. PropertyChanged("Item[]")  — bound indexer displays update
        ///   3. CollectionChanged(Reset)   — ListView re-virtualizes
        ///
        /// This order matches what ObservableCollection<T> uses internally.
        /// </summary>
        private void RaiseResetCore()
        {
            // Notify property changes first — required by WPF binding engine
            // so Count-bound TextBlocks update before the ListView redraws.
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));

            // Single Reset — WPF discards all item containers and
            // re-virtualizes. Only currently-visible items get new containers.
            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Reset));
        }
    }
}