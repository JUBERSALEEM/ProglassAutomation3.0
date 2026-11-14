using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Services.DeliveryNote;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.TaxInvoice.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Documents
{
    /// <summary>
    /// Per-document qty / invoice progress used by list DisplayStatus.
    /// Ordered comes from the Master DN (or max unique-item ordered on real
    /// DNs — never the sum of remaining-DN OrderedQty). Delivered and
    /// invoiced counts use confirmed DNs and Issued tax invoices only.
    /// </summary>
    public sealed class DocumentProgress
    {
        public static readonly DocumentProgress Empty = new();

        public int Ordered { get; init; }
        public int ConfirmedDelivered { get; init; }
        public int IssuedQty { get; init; }
        public int DraftTiCount { get; init; }
        public int IssuedTiCount { get; init; }
        public int ConfirmedDnCount { get; init; }
        public int ConfirmedDnsCoveredByIssued { get; init; }
        public bool HasJobOrder { get; init; }
    }

    public sealed class DocumentProgressSnapshot
    {
        public static readonly DocumentProgressSnapshot Empty = new(
            new Dictionary<string, DocumentProgress>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, DocumentProgress>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        private readonly Dictionary<string, DocumentProgress> _byProforma;
        private readonly Dictionary<string, DocumentProgress> _byJobOrder;
        private readonly HashSet<string> _issuedCoveredDnIds;

        private DocumentProgressSnapshot(
            Dictionary<string, DocumentProgress> byProforma,
            Dictionary<string, DocumentProgress> byJobOrder,
            HashSet<string> issuedCoveredDnIds)
        {
            _byProforma = byProforma;
            _byJobOrder = byJobOrder;
            _issuedCoveredDnIds = issuedCoveredDnIds;
        }

        public IReadOnlyDictionary<string, DocumentProgress> ByJobOrder => _byJobOrder;

        public DocumentProgress ForProforma(string? proformaId) =>
            string.IsNullOrWhiteSpace(proformaId)
                ? DocumentProgress.Empty
                : _byProforma.TryGetValue(proformaId, out var p) ? p : DocumentProgress.Empty;

        public DocumentProgress ForJobOrder(string? jobOrderId) =>
            string.IsNullOrWhiteSpace(jobOrderId)
                ? DocumentProgress.Empty
                : _byJobOrder.TryGetValue(jobOrderId, out var p) ? p : DocumentProgress.Empty;

        public bool CoversDeliveryNote(string? deliveryNoteId) =>
            !string.IsNullOrWhiteSpace(deliveryNoteId)
            && _issuedCoveredDnIds.Contains(deliveryNoteId);

        public static async Task<DocumentProgressSnapshot> LoadAsync(
            IEnumerable<(string JobOrderId, string SourceProformaId)>? jobs = null)
        {
            var jobPairs = jobs?.ToList()
                ?? new List<(string JobOrderId, string SourceProformaId)>();

            // PERF-11 (2026-08-30): the four reads below were previously
            // strictly sequential — four full-table SQLite queries, each
            // waiting for the previous one — on EVERY list page load.
            // They are independent reads on separate repositories (each
            // opens its own DbContext), so they now run concurrently and
            // cut page-open latency from 4×query to ~1×query. Each task
            // keeps its own try/catch so one failing table can never
            // poison the other three.
            Task<List<(string JobOrderId, string SourceProformaId)>>? jobsTask = null;
            if (jobs == null && App.Jobs != null)
            {
                jobsTask = Task.Run(async () =>
                {
                    try
                    {
                        var rows = await App.Jobs.GetListRowsAsync();
                        return (rows ?? new List<JobOrderListRow>())
                            .Select(r => (r.JobOrderId ?? "", r.SourceProformaId ?? ""))
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Warning(
                            "Document progress: job list unavailable: " + ex.Message);
                        return new List<(string, string)>();
                    }
                });
            }

            Task<List<DeliveryNoteListItem>>? mastersTask = null;
            if (App.DeliveryNotes != null)
            {
                mastersTask = Task.Run(async () =>
                {
                    try
                    {
                        return await App.DeliveryNotes.GetMasterDnsAsync()
                            ?? new List<DeliveryNoteListItem>();
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Warning(
                            "Document progress: master DNs unavailable: " + ex.Message);
                        return new List<DeliveryNoteListItem>();
                    }
                });
            }

            Task<List<DeliveryNoteListItem>>? dnsTask = null;
            if (App.DeliveryNotes != null)
            {
                dnsTask = Task.Run(async () =>
                {
                    try
                    {
                        return await App.DeliveryNotes.GetAllAsync()
                            ?? new List<DeliveryNoteListItem>();
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Warning(
                            "Document progress: delivery notes unavailable: " + ex.Message);
                        return new List<DeliveryNoteListItem>();
                    }
                });
            }

            Task<List<TaxInvoiceListItem>>? tisTask = null;
            if (App.TaxInvoices != null)
            {
                tisTask = Task.Run(async () =>
                {
                    try
                    {
                        return await App.TaxInvoices.GetAllAsync()
                            ?? new List<TaxInvoiceListItem>();
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Warning(
                            "Document progress: tax invoices unavailable: " + ex.Message);
                        return new List<TaxInvoiceListItem>();
                    }
                });
            }

            if (jobsTask != null)
                jobPairs = await jobsTask;

            var masters = mastersTask != null ? await mastersTask : new List<DeliveryNoteListItem>();
            var dns = dnsTask != null ? await dnsTask : new List<DeliveryNoteListItem>();
            var tis = tisTask != null ? await tisTask : new List<TaxInvoiceListItem>();

            return From(jobPairs, masters, dns, tis);
        }

        public static DocumentProgressSnapshot From(
            IEnumerable<(string JobOrderId, string SourceProformaId)>? jobs,
            IEnumerable<DeliveryNoteListItem>? masters,
            IEnumerable<DeliveryNoteListItem>? realDns,
            IEnumerable<TaxInvoiceListItem>? invoices)
        {
            var joAcc = new Dictionary<string, Acc>(StringComparer.OrdinalIgnoreCase);
            var piAcc = new Dictionary<string, Acc>(StringComparer.OrdinalIgnoreCase);
            var joToPi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var piConfirmedIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            Acc Jo(string? id)
            {
                if (string.IsNullOrWhiteSpace(id)) return new Acc();
                if (!joAcc.TryGetValue(id, out var a))
                {
                    a = new Acc();
                    joAcc[id] = a;
                }
                return a;
            }

            Acc Pi(string? id)
            {
                if (string.IsNullOrWhiteSpace(id)) return new Acc();
                if (!piAcc.TryGetValue(id, out var a))
                {
                    a = new Acc();
                    piAcc[id] = a;
                }
                return a;
            }

            void Link(string? jo, string? pi)
            {
                if (string.IsNullOrWhiteSpace(jo) || string.IsNullOrWhiteSpace(pi))
                    return;
                if (!joToPi.ContainsKey(jo))
                    joToPi[jo] = pi;
            }

            foreach (var (jobOrderId, sourcePi) in jobs ?? Array.Empty<(string, string)>())
            {
                if (string.IsNullOrWhiteSpace(jobOrderId)) continue;
                Jo(jobOrderId).HasJob = true;
                if (!string.IsNullOrWhiteSpace(sourcePi))
                {
                    Pi(sourcePi).HasJob = true;
                    Link(jobOrderId, sourcePi);
                }
            }

            foreach (var m in masters ?? Array.Empty<DeliveryNoteListItem>())
            {
                if (m == null || string.IsNullOrWhiteSpace(m.JobOrderId)) continue;
                var acc = Jo(m.JobOrderId);
                acc.Ordered = Math.Max(0, m.TotalOrderedQty);
                acc.HasMasterOrdered = true;
                Link(m.JobOrderId, m.ProformaId);
                if (!string.IsNullOrWhiteSpace(m.ProformaId))
                    Pi(m.ProformaId).HasJob = acc.HasJob || Pi(m.ProformaId).HasJob;
            }

            foreach (var d in realDns ?? Array.Empty<DeliveryNoteListItem>())
            {
                if (d == null || d.IsMasterDn) continue;
                if (string.Equals(d.DeliveryType, "Dummy", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(d.JobOrderId)) continue;

                var acc = Jo(d.JobOrderId);
                if (!acc.HasMasterOrdered)
                    acc.Ordered = Math.Max(acc.Ordered, Math.Max(0, d.TotalOrderedQty));

                Link(d.JobOrderId, d.ProformaId);

                if (!DeliveryNoteWorkflow.IsConfirmedDelivery(d.Status))
                    continue;

                acc.Delivered += Math.Max(0, d.TotalDeliveredQty);
                if (!string.IsNullOrWhiteSpace(d.DeliveryNoteId)
                    && acc.ConfirmedIds.Add(d.DeliveryNoteId))
                    acc.ConfirmedDn++;

                if (!string.IsNullOrWhiteSpace(d.ProformaId)
                    && !string.IsNullOrWhiteSpace(d.DeliveryNoteId))
                {
                    if (!piConfirmedIds.TryGetValue(d.ProformaId, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        piConfirmedIds[d.ProformaId] = set;
                    }
                    set.Add(d.DeliveryNoteId);
                }
            }

            var issuedCovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var joConfirmedLookup = joAcc.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ConfirmedIds,
                StringComparer.OrdinalIgnoreCase);

            foreach (var t in invoices ?? Array.Empty<TaxInvoiceListItem>())
            {
                if (t == null || DocumentListStatus.IsCancelledInvoice(t.Status))
                    continue;

                bool issued = DocumentListStatus.IsIssuedInvoice(t.Status);
                bool draft = DocumentListStatus.IsDraftInvoice(t.Status);

                void Touch(Acc acc)
                {
                    if (issued)
                    {
                        acc.IssuedTi++;
                        acc.IssuedQty += Math.Max(0, t.TotalQty);
                    }
                    else if (draft)
                    {
                        acc.DraftTi++;
                    }
                }

                if (!string.IsNullOrWhiteSpace(t.JobOrderId))
                    Touch(Jo(t.JobOrderId));
                if (!string.IsNullOrWhiteSpace(t.ProformaId))
                    Touch(Pi(t.ProformaId));

                if (!issued) continue;

                var refs = TaxInvoiceDnReconciler.ParseDnRefs(t.DeliveryNoteReference);
                if (refs.Count > 0)
                {
                    foreach (var r in refs)
                    {
                        issuedCovered.Add(r);
                        if (joConfirmedLookup.TryGetValue(r, out var byJoId))
                        {
                            foreach (var id in byJoId)
                                issuedCovered.Add(id);
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(t.JobOrderId)
                         && joConfirmedLookup.TryGetValue(t.JobOrderId, out var joIds))
                {
                    foreach (var id in joIds)
                        issuedCovered.Add(id);
                }
                else if (!string.IsNullOrWhiteSpace(t.ProformaId)
                         && piConfirmedIds.TryGetValue(t.ProformaId, out var piIds))
                {
                    foreach (var id in piIds)
                        issuedCovered.Add(id);
                }
            }

            foreach (var kv in joAcc)
            {
                int covered = kv.Value.ConfirmedIds.Count(id => issuedCovered.Contains(id));
                kv.Value.Covered = covered;
            }

            foreach (var kv in joToPi)
            {
                if (!joAcc.TryGetValue(kv.Key, out var jo)) continue;
                var pi = Pi(kv.Value);
                pi.HasJob = true;
                pi.Ordered += jo.Ordered;
                pi.Delivered += jo.Delivered;
                pi.ConfirmedDn += jo.ConfirmedDn;
                pi.Covered += jo.Covered;
                // Invoice counts already landed on the PI via ProformaId.
            }

            foreach (var kv in piConfirmedIds)
            {
                var pi = Pi(kv.Key);
                if (pi.ConfirmedDn == 0)
                    pi.ConfirmedDn = kv.Value.Count;
                if (pi.Covered == 0)
                    pi.Covered = kv.Value.Count(id => issuedCovered.Contains(id));
            }

            return new DocumentProgressSnapshot(
                piAcc.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToProgress(),
                    StringComparer.OrdinalIgnoreCase),
                joAcc.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToProgress(),
                    StringComparer.OrdinalIgnoreCase),
                issuedCovered);
        }

        private sealed class Acc
        {
            public int Ordered;
            public int Delivered;
            public int IssuedQty;
            public int DraftTi;
            public int IssuedTi;
            public int ConfirmedDn;
            public int Covered;
            public bool HasJob;
            public bool HasMasterOrdered;
            public HashSet<string> ConfirmedIds { get; } = new(StringComparer.OrdinalIgnoreCase);

            public DocumentProgress ToProgress() => new()
            {
                Ordered = Ordered,
                ConfirmedDelivered = Delivered,
                IssuedQty = IssuedQty,
                DraftTiCount = DraftTi,
                IssuedTiCount = IssuedTi,
                ConfirmedDnCount = ConfirmedDn,
                ConfirmedDnsCoveredByIssued = Covered,
                HasJobOrder = HasJob
            };
        }
    }
}
