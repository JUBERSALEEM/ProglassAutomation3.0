// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Shared/SectionActivityViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Shared — per-section Activity tab
//
// Live activity feed for ONE section. Used by the Activity tab inside
// each module's tab strip (Delivery Note / Job Order / Tax Invoice),
// so every section shows its own user-action trail as it happens.
//
// • Loads recent entries from AuditService on activation.
// • Subscribes to AuditService.ActivityLogged → new rows appear live
//   (marshalled to the UI thread by the service).
// • Capped at 200 rows in memory; older rows stay in audit.db.
// ═══════════════════════════════════════════════════════════════════════
using ProGlassAutomation.Models.Audit;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Services.Audit;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ProGlassAutomation.Commands;

namespace ProGlassAutomation.ViewModels.Shared
{
    public class SectionActivityViewModel : ViewModelBase, IDisposable
    {
        private readonly string? _section;
        private const int MaxRows = 200;

        public ObservableCollection<ActivityRowViewModel> Rows { get; } = new();

        public string Section => _section ?? "All sections";

        private bool _isEmpty = true;
        public bool IsEmpty
        {
            get => _isEmpty;
            private set { _isEmpty = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }

        /// <summary>Shows the GitHub-style before/after diff for a row.</summary>
        public ICommand ShowChangesCommand { get; }

        /// <summary>Shows the complete who/when/what/why audit report.</summary>
        public ICommand ShowAuditReportCommand { get; }

        public SectionActivityViewModel(string? section)
        {
            _section = section;

            RefreshCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(() => LoadAsync(), "GodView LoadAsync"));

            ShowChangesCommand = new RelayCommand(param =>
            {
                if (param is ActivityRowViewModel row && row.HasChanges)
                {
                    Views.Shared.ChangesDialog.Show(
                        System.Windows.Application.Current?.MainWindow,
                        $"{row.Section} — {row.Action} — {row.Username}",
                        AuditService_ChangesFromRow(row));
                }
            });

            ShowAuditReportCommand = new RelayCommand(param =>
            {
                if (param is ActivityRowViewModel row)
                {
                    Views.Shared.AuditReportDialog.Show(
                        System.Windows.Application.Current?.MainWindow,
                        row,
                        AuditService_ChangesFromRow(row));
                }
            });

            // Live updates — the service raises this on the UI thread.
            AuditService.ActivityLogged += OnActivityLogged;
        }

        /// <summary>Reloads the latest entries for this section.</summary>
        public async Task LoadAsync()
        {
            var entries = await AuditService.GetRecentAsync(
                MaxRows, string.IsNullOrEmpty(_section) ? null : _section);

            // GetRecentAsync resumes on a thread-pool thread. Bound
            // ObservableCollection mutations must run on the dispatcher
            // (ERR-A65374 crashed demo recording on the Activity tab).
            UiDispatch.Run(() =>
            {
                Rows.Clear();
                foreach (var e in entries)
                    Rows.Add(ToRow(e));
                IsEmpty = Rows.Count == 0;
            });
        }

        private void OnActivityLogged(object? sender, ActivityEventArgs e)
        {
            if (!string.IsNullOrEmpty(_section) &&
                e.Entry.Section != _section)
                return;

            Rows.Insert(0, ToRow(e.Entry));

            while (Rows.Count > MaxRows)
                Rows.RemoveAt(Rows.Count - 1);

            IsEmpty = Rows.Count == 0;
        }

        private static ActivityRowViewModel ToRow(ActivityLogEntry e)
        {
            return new ActivityRowViewModel
            {
                Id = e.Id,
                TimestampUtc = e.TimestampUtc,
                TimeDisplay = e.TimestampUtc.ToLocalTime()
                    .ToString("dd MMM yyyy  HH:mm:ss"),
                UtcTimeDisplay = e.TimestampUtc
                    .ToString("dd MMM yyyy  HH:mm:ss 'UTC'"),
                Username = e.Username,
                UserDisplayName = e.UserDisplayName,
                UserRole = e.UserRole,
                Action = e.Action,
                Details = e.Details,
                Section = e.Section,
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                SessionId = e.SessionId,
                OperationId = e.OperationId,
                MachineName = e.MachineName,
                AppVersion = e.AppVersion,
                LocalTimeZone = e.LocalTimeZone,
                CurrentViewerUsername = AuditService.CurrentUsername,
                ChangesJson = e.ChangesJson ?? "",
                Reason = e.Reason ?? ""
            };
        }

        private static List<Services.Audit.AuditChange>
            AuditService_ChangesFromRow(ActivityRowViewModel row) =>
            Services.Audit.AuditChangeSerializer.FromJson(row.ChangesJson);

        // ══════════════════════════════════════════════════════════════
        //  DISPOSAL — audit finding M-1
        // ══════════════════════════════════════════════════════════════

        private bool _disposed;

        /// <summary>
        /// Detaches from the static <see cref="AuditService.ActivityLogged"/>
        /// event so this instance can be collected. Safe to call more than
        /// once, and safe to call on an instance that is never collected.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Unsubscribing from the static event removes the last root
            // holding this object alive.
            AuditService.ActivityLogged -= OnActivityLogged;

            GC.SuppressFinalize(this);
        }
    }
}
