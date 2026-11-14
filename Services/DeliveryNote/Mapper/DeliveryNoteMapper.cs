using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Models.MasterData;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProGlassAutomation.Services.DeliveryNote.Mapper
{
    /// <summary>
    /// Converts Job Order entities into Delivery Note entities.
    ///
    /// Three conversion paths:
    ///
    /// Path A — FromJobOrder(SavedJobOrder, snapshot, dnId, seq):
    ///   Creates a new Draft DN from a Job Order.
    ///
    /// Path B — FromBalance(SavedDeliveryNote, dnId, seq):
    ///   Creates a Dummy DN from the balance of a prior DN.
    ///
    /// Path C — Serialize/Deserialize snapshot (JSON round-trip).
    ///
    /// BUG-22: TotalArea now uses SqmCalculator.CalculatePanel()
    ///         instead of inline formula — single source of truth.
    /// BUG-23: FromBalance TotalOrderedQty uses MAX per unique item
    ///         not SUM — fixes Gotcha #40 double-count on splits.
    /// BUG-24: Empty ItemRows list guard added in FromJobOrder.
    /// </summary>
    public static class DeliveryNoteMapper
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        // ════════════════════════════════════════════════════════════════
        //  PATH A — Job Order → New Draft Delivery Note
        // ════════════════════════════════════════════════════════════════

        public static SavedDeliveryNote FromJobOrder(
            SavedJobOrder jobOrder,
            JobOrderSnapshot joSnapshot,
            string deliveryNoteId,
            int sequenceNumber)
        {
            if (jobOrder == null) throw new ArgumentNullException(nameof(jobOrder));
            if (joSnapshot == null) throw new ArgumentNullException(nameof(joSnapshot));

            var pi = joSnapshot.EmbeddedInvoice;
            var now = DateTime.Now;

            var entity = new SavedDeliveryNote
            {
                DeliveryNoteId = deliveryNoteId,
                JobOrderId = jobOrder.JobOrderId,
                ProformaId = jobOrder.SourceProformaId,
                SequenceNumber = sequenceNumber,
                CustomerName = jobOrder.CustomerName,
                ProjectName = jobOrder.ProjectName,
                Status = "Dispatching",
                DeliveryType = "Original",
                IsFoc = false,
                DriverName = "",
                VehicleNumber = "",
                ReceiverName = "",
                DeliveryDate = now,
                DeliveryTime = now.ToString("HH:mm"),
                Notes = "",
                TotalOrderedQty = 0,
                TotalDeliveredQty = 0,
                TotalBalanceQty = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            // ── Build items from JO spec groups ──────────────────────────
            var items = new List<DeliveryNoteItem>();

            // Extract glass composition from Step 4 pricing for weight calculation
            var step4Pricing = pi?.Step4Pricing;
            string glassTypeCode = step4Pricing?.SelectedGlassTypeCode ?? "";

            int globalSerialCounter = 1;  // Continuous numbering across all spec groups

            if (pi?.SpecGroups != null)
            {
                foreach (var group in pi.SpecGroups)
                {
                    // BUG-24: guard against null AND empty ItemRows
                    if (group.ItemRows == null || group.ItemRows.Count == 0)
                        continue;

                    // DEDUP: one DeliveryNoteItem per exact Step 5 row key
                    // (SerialNumber within the spec group). The PI snapshot
                    // can contain duplicated rows (e.g. a DGU entered as
                    // outer + inner with the same serial), which would
                    // otherwise surface as 9 rows → 18 in the Delivery Note.
                    // OrderedQty is replicated across such splits → take MAX;
                    // there is no delivered quantity yet on a fresh DN.
                    var rowsBySerial = group.ItemRows
                        .Where(r => !(string.IsNullOrWhiteSpace(r.GlassRef)
                                      && r.W1 == 0 && r.H1 == 0))
                        .Where(r => r.Qty > 0)
                        .GroupBy(r => r.SerialNumber);

                    foreach (var rowGroup in rowsBySerial)
                    {
                        var row = rowGroup.First();

                        // Calculate weight per piece using glass dimensions and composition
                        decimal weightPerPiece = step4Pricing != null 
                            ? CalculateWeightPerPiece(row.W1, row.H1, glassTypeCode, step4Pricing)
                            : 0m;

                        var item = new DeliveryNoteItem
                        {
                            DeliveryNoteId = deliveryNoteId,
                            JobOrderId = jobOrder.JobOrderId,
                            SpecGroupNumber = group.GroupNumber,
                            SpecGroupName = !string.IsNullOrWhiteSpace(
                                                         group.SelectedSpecName)
                                                         ? group.SelectedSpecName
                                                         : (group.GroupName
                                                            ?? $"Spec {group.GroupNumber}"),
                            SerialNumber = globalSerialCounter++,  // Continuous numbering
                            GlassRef = row.GlassRef ?? "",
                            W1 = row.W1,
                            H1 = row.H1,
                            W2 = row.W2,
                            H2 = row.H2,
                            OrderedQty = rowGroup.Max(r => r.Qty),
                            PreviouslyDeliveredQty = 0,
                            DeliveredQty = 0,
                            BalanceQty = rowGroup.Max(r => r.Qty),
                            ReworkQty = 0,
                            ReturnedQty = 0,
                            WeightPerPiece = weightPerPiece,
                            Notes = ""
                        };

                        items.Add(item);
                        entity.TotalOrderedQty += item.OrderedQty;
                        entity.TotalBalanceQty += item.BalanceQty;
                    }
                }
            }

            entity.Items = items;

            // ── Build snapshot ────────────────────────────────────────────
            var snapshot = BuildSnapshot(entity, joSnapshot);
            entity.FullSnapshotJson = JsonSerializer.Serialize(
                snapshot, _jsonOptions);

            return entity;
        }

        // ════════════════════════════════════════════════════════════════
        //  AUTHORITATIVE ORDERED-QTY MAP (self-heal source of truth)
        //
        //  OrderedQty is denormalized onto every DN line and copied
        //  master → DN → DN. If any historical build ever stored the
        //  *balance* into OrderedQty, that wrong number propagates forever
        //  because nothing re-derives it. This rebuilds the TRUE ordered
        //  quantity per line straight from the Job Order snapshot, keyed
        //  EXACTLY the way FromJobOrder assigns line identity
        //  (SpecGroupNumber + continuous global SerialNumber), so the keys
        //  line up with the stored DeliveryNoteItem rows. Callers use it to
        //  heal OrderedQty at load — the same pattern as HealItemPrevQty.
        // ════════════════════════════════════════════════════════════════

        public static Dictionary<string, int> BuildOrderedQtyMap(
            JobOrderSnapshot? joSnapshot)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var pi = joSnapshot?.EmbeddedInvoice;
            if (pi?.SpecGroups == null) return map;

            int globalSerialCounter = 1;  // MUST mirror FromJobOrder numbering
            foreach (var group in pi.SpecGroups)
            {
                if (group.ItemRows == null || group.ItemRows.Count == 0)
                    continue;

                var rowsBySerial = group.ItemRows
                    .Where(r => !(string.IsNullOrWhiteSpace(r.GlassRef)
                                  && r.W1 == 0 && r.H1 == 0))
                    .Where(r => r.Qty > 0)
                    .GroupBy(r => r.SerialNumber);

                foreach (var rowGroup in rowsBySerial)
                {
                    int serial = globalSerialCounter++;
                    int ordered = rowGroup.Max(r => r.Qty);
                    map[$"{group.GroupNumber}|{serial}"] = ordered;
                }
            }

            return map;
        }

        // ════════════════════════════════════════════════════════════════
        //  PATH A2 — Master remaining balance → New Dispatching DN
        //
        //  SerialNumber is copied from the Master line. Reassigning 1..N
        //  on remaining-only DNs made PREV lookup hit the WRONG line and
        //  the grid showed "all items fully delivered" while balance > 0.
        // ════════════════════════════════════════════════════════════════

        public static SavedDeliveryNote FromMasterBalance(
            SavedDeliveryNote masterDn,
            string deliveryNoteId,
            int sequenceNumber)
        {
            if (masterDn == null)
                throw new ArgumentNullException(nameof(masterDn));

            var now = DateTime.Now;

            var entity = new SavedDeliveryNote
            {
                DeliveryNoteId = deliveryNoteId,
                JobOrderId = masterDn.JobOrderId,
                ProformaId = masterDn.ProformaId,
                SequenceNumber = sequenceNumber,
                CustomerName = masterDn.CustomerName,
                ProjectName = masterDn.ProjectName,
                Status = "Dispatching",
                DeliveryType = "Original",
                IsMasterDn = false,
                IsFoc = false,
                DriverName = "",
                VehicleNumber = "",
                ReceiverName = "",
                DeliveryDate = now,
                DeliveryTime = now.ToString("HH:mm"),
                Notes = "",
                TotalOrderedQty = 0,
                TotalDeliveredQty = 0,
                TotalBalanceQty = 0,
                TotalPreviouslyDeliveredQty = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            var items = new List<DeliveryNoteItem>();

            foreach (var masterItem in (masterDn.Items ?? new List<DeliveryNoteItem>())
                .Where(i => i.ComputedBalanceQty > 0)
                .OrderBy(i => i.SpecGroupNumber)
                .ThenBy(i => i.SerialNumber))
            {
                int remaining = masterItem.ComputedBalanceQty;
                items.Add(new DeliveryNoteItem
                {
                    DeliveryNoteId = deliveryNoteId,
                    JobOrderId = masterDn.JobOrderId,
                    SpecGroupNumber = masterItem.SpecGroupNumber,
                    SpecGroupName = masterItem.SpecGroupName,
                    SerialNumber = masterItem.SerialNumber,
                    GlassRef = masterItem.GlassRef,
                    W1 = masterItem.W1,
                    H1 = masterItem.H1,
                    W2 = masterItem.W2,
                    H2 = masterItem.H2,
                    OrderedQty = masterItem.OrderedQty,
                    PreviouslyDeliveredQty = masterItem.PreviouslyDeliveredQty,
                    DeliveredQty = 0,
                    BalanceQty = remaining,
                    ReworkQty = 0,
                    ReturnedQty = 0,
                    WeightPerPiece = masterItem.WeightPerPiece,
                    Notes = ""
                });
            }

            entity.Items = items;
            entity.TotalOrderedQty = items.Sum(i => i.OrderedQty);
            entity.TotalPreviouslyDeliveredQty =
                items.Sum(i => i.PreviouslyDeliveredQty);
            entity.TotalDeliveredQty = 0;
            entity.TotalBalanceQty = items.Sum(i => i.BalanceQty);

            var masterSnapshot = DeserializeSnapshot(masterDn);
            RefreshSnapshot(entity, masterSnapshot?.JobOrderRef);
            return entity;
        }

        // ════════════════════════════════════════════════════════════════
        //  PATH B — Prior DN Balance → New Dummy Delivery Note
        // ════════════════════════════════════════════════════════════════

        public static SavedDeliveryNote FromBalance(
            SavedDeliveryNote priorDn,
            string newDeliveryNoteId,
            int newSequenceNumber)
        {
            if (priorDn == null)
                throw new ArgumentNullException(nameof(priorDn));

            var now = DateTime.Now;

            var entity = new SavedDeliveryNote
            {
                DeliveryNoteId = newDeliveryNoteId,
                JobOrderId = priorDn.JobOrderId,
                ProformaId = priorDn.ProformaId,
                SequenceNumber = newSequenceNumber,
                CustomerName = priorDn.CustomerName,
                ProjectName = priorDn.ProjectName,
                Status = "Dispatching",
                DeliveryType = "Dummy",
                IsFoc = false,
                DriverName = "",
                VehicleNumber = "",
                ReceiverName = "",
                DeliveryDate = now,
                DeliveryTime = now.ToString("HH:mm"),
                Notes = "",
                TotalOrderedQty = 0,
                TotalDeliveredQty = 0,
                TotalBalanceQty = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            // ── Carry forward only items with balance > 0 ─────────────────
            var items = new List<DeliveryNoteItem>();

            // BUG-23: Group by unique item key to get MAX OrderedQty
            // OrderedQty is REPLICATED across splits — use MAX not SUM
            var uniqueItems = priorDn.Items
                .Where(i => i.BalanceQty > 0)
                .GroupBy(i => new { i.SpecGroupNumber, i.SerialNumber })
                .ToList();

            foreach (var itemGroup in uniqueItems)
            {
                // MAX OrderedQty across splits (Gotcha #40 fix — BUG-23)
                var representative = itemGroup.First();
                int maxOrderedQty = itemGroup.Max(i => i.OrderedQty);
                int totalPreviouslyDeliv =
                    representative.PreviouslyDeliveredQty +
                    representative.DeliveredQty;

                var item = new DeliveryNoteItem
                {
                    DeliveryNoteId = newDeliveryNoteId,
                    JobOrderId = priorDn.JobOrderId,
                    SpecGroupNumber = representative.SpecGroupNumber,
                    SpecGroupName = representative.SpecGroupName,
                    SerialNumber = representative.SerialNumber,
                    GlassRef = representative.GlassRef,
                    W1 = representative.W1,
                    H1 = representative.H1,
                    W2 = representative.W2,
                    H2 = representative.H2,
                    OrderedQty = maxOrderedQty,
                    PreviouslyDeliveredQty = totalPreviouslyDeliv,
                    DeliveredQty = 0,
                    BalanceQty = representative.BalanceQty,
                    ReworkQty = 0,
                    ReturnedQty = 0,
                    WeightPerPiece = representative.WeightPerPiece,
                    Notes = ""
                };

                items.Add(item);
                entity.TotalOrderedQty += maxOrderedQty;
                entity.TotalBalanceQty += representative.BalanceQty;
            }

            entity.Items = items;

            // ── Deserialize prior snapshot to get JO ref context ──────────
            var priorSnapshot = DeserializeSnapshot(priorDn);
            var snapshot = BuildSnapshot(
                entity, null, priorSnapshot?.JobOrderRef);
            entity.FullSnapshotJson = JsonSerializer.Serialize(
                snapshot, _jsonOptions);

            return entity;
        }

        // ════════════════════════════════════════════════════════════════
        //  PATH C — Serialize / Deserialize
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Deserializes the JSON snapshot from a saved entity.
        /// Returns null if JSON is empty or malformed.
        /// BUG-25: Catch block preserved — malformed JSON returns null
        /// gracefully. Callers must handle null snapshot defensively.
        /// </summary>
        public static DeliveryNoteSnapshot? DeserializeSnapshot(
            SavedDeliveryNote entity)
        {
            if (entity == null
                || string.IsNullOrWhiteSpace(entity.FullSnapshotJson)
                || entity.FullSnapshotJson == "{}")
                return null;

            try
            {
                return JsonSerializer.Deserialize<DeliveryNoteSnapshot>(
                    entity.FullSnapshotJson, _jsonOptions);
            }
            catch (JsonException ex)
            {
                // Log for diagnostics — malformed snapshot causes empty detail view
                AppLogService.Debug(
                    $"[DeliveryNoteMapper] Snapshot deserialize failed " +
                    $"for DN '{entity.DeliveryNoteId}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Serializes the current entity state into FullSnapshotJson.
        /// Call before InsertAsync / UpdateAsync.
        /// </summary>
        public static void RefreshSnapshot(
            SavedDeliveryNote entity,
            DnJobOrderRefSnapshot? joRef = null)
        {
            var snapshot = BuildSnapshot(entity, null, joRef);
            entity.FullSnapshotJson = JsonSerializer.Serialize(
                snapshot, _jsonOptions);
        }

        // ════════════════════════════════════════════════════════════════
        //  PRIVATE — weight calculation helper
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculate weight per piece in kilograms from glass dimensions and composition.
        /// Uses GlassWeightCalculator with thickness data from Step 4 pricing.
        /// Returns 0 if data is insufficient or calculation fails.
        /// </summary>
        private static decimal CalculateWeightPerPiece(
            int widthMm,
            int heightMm,
            string glassTypeCode,
            Step4PricingSnapshot step4Pricing)
        {
            if (widthMm <= 0 || heightMm <= 0 || step4Pricing == null)
                return 0m;

            try
            {
                // Calculate area using SqmCalculator (applies 0.5 m² minimum)
                decimal areaM2 = SqmCalculator.CalculatePanel(widthMm, heightMm);
                if (areaM2 <= 0) return 0m;

                // Extract thickness based on glass type
                string type = (glassTypeCode ?? "").Trim().ToUpperInvariant();
                decimal totalThicknessMm = 0m;

                if (type == "SGU" && step4Pricing.Sgu != null)
                {
                    totalThicknessMm = GlassWeightCalculator.ExtractTotalThickness(
                        "SGU",
                        step4Pricing.Sgu.SelectedThickness,
                        "");
                }
                else if (type == "DGU" && step4Pricing.Dgu != null)
                {
                    totalThicknessMm = GlassWeightCalculator.ExtractTotalThickness(
                        "DGU",
                        step4Pricing.Dgu.OuterThickness,
                        step4Pricing.Dgu.InnerThickness);
                }
                else if (type == "LAM" && step4Pricing.Lam != null)
                {
                    totalThicknessMm = GlassWeightCalculator.ExtractTotalThickness(
                        "LAM",
                        step4Pricing.Lam.OuterThickness,
                        step4Pricing.Lam.InnerThickness);
                }
                else if (type == "DGU+LAM" && step4Pricing.DguLam != null)
                {
                    totalThicknessMm = GlassWeightCalculator.ExtractTotalThickness(
                        "DGU+LAM",
                        step4Pricing.DguLam.OuterThickness,
                        "",
                        step4Pricing.DguLam.LamOuterThickness,
                        step4Pricing.DguLam.LamInnerThickness);
                }

                if (totalThicknessMm <= 0) return 0m;

                // Calculate weight using GlassWeightCalculator
                return GlassWeightCalculator.CalculatePieceWeight(areaM2, totalThicknessMm);
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[DeliveryNoteMapper] Weight calculation failed for " +
                    $"W1={widthMm}, H1={heightMm}, Type={glassTypeCode}: {ex.Message}");
                return 0m;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  PRIVATE — snapshot builder
        // ════════════════════════════════════════════════════════════════

        private static DeliveryNoteSnapshot BuildSnapshot(
            SavedDeliveryNote entity,
            JobOrderSnapshot? joSnapshot = null,
            DnJobOrderRefSnapshot? existingRef = null)
        {
            var joRef = existingRef ?? new DnJobOrderRefSnapshot();

            if (joSnapshot != null)
            {
                var pi = joSnapshot.EmbeddedInvoice;

                joRef = new DnJobOrderRefSnapshot
                {
                    JobOrderId = joSnapshot.JobOrderId,
                    ProformaId = joSnapshot.SourceProformaId,
                    CustomerName = pi?.Client?.CustomerName ?? "",
                    ProjectName = !string.IsNullOrWhiteSpace(pi?.Project?.ProjectIdentifierName)
                        ? pi.Project.ProjectIdentifierName
                        : (pi?.Project?.ProjectReference ?? ""),
                    ProjectReference = pi?.Project?.ProjectReference ?? "",
                    CompanyName = pi?.Company?.CompanyName ?? "",
                    CompanyTrn = pi?.Company?.CompanyTrn ?? "",
                    CompanyLocation = pi?.Company?.CompanyLocation ?? "",

                    // v3 — company branding so the DN letterhead matches the PI.
                    LogoPath = pi?.Company?.LogoPath ?? "",
                    LogoBase64 = pi?.Company?.LogoBase64 ?? "",
                    LogoLayout = pi?.Company?.LogoLayout
                        ?? CompanyLogoLayout.None,
                    LogoWidthMm = pi?.Company?.LogoWidthMm ?? 22,
                    LogoHeightMm = pi?.Company?.LogoHeightMm ?? 26,

                    // BUG-22: use SqmCalculator instead of inline formula
                    SpecGroups = pi?.SpecGroups?.Select(g => new DnJoSpecGroupSnapshot
                    {
                        GroupNumber = g.GroupNumber,
                        GroupName = !string.IsNullOrWhiteSpace(g.SelectedSpecName)
                                          ? g.SelectedSpecName
                                          : (g.GroupName ?? $"Spec {g.GroupNumber}"),
                        GlassType = g.SelectedGlassType ?? "",
                        SpecName = g.SelectedSpecName ?? "",
                        TotalQty = g.ItemRows?.Sum(r => r.Qty) ?? 0,
                        TotalArea = g.ItemRows?.Sum(r =>
                            SqmCalculator.CalculatePanel(r.W1, r.H1) * r.Qty)
                            ?? 0m
                    }).ToList() ?? new()
                };
            }

            return new DeliveryNoteSnapshot
            {
                Header = new DnHeaderSnapshot
                {
                    DeliveryNoteId = entity.DeliveryNoteId,
                    JobOrderId = entity.JobOrderId,
                    ProformaId = entity.ProformaId,
                    SequenceNumber = entity.SequenceNumber,
                    Status = entity.Status,
                    DeliveryType = entity.DeliveryType,
                    IsFoc = entity.IsFoc,
                    DriverName = entity.DriverName,
                    VehicleNumber = entity.VehicleNumber,
                    ReceiverName = entity.ReceiverName,
                    DeliveryDate = entity.DeliveryDate,
                    DeliveryTime = entity.DeliveryTime,
                    Notes = entity.Notes
                },
                JobOrderRef = joRef,
                Items = entity.Items?.Select(i => new DnItemSnapshot
                {
                    SpecGroupNumber = i.SpecGroupNumber,
                    SpecGroupName = i.SpecGroupName,
                    SerialNumber = i.SerialNumber,
                    GlassRef = i.GlassRef,
                    W1 = i.W1,
                    H1 = i.H1,
                    W2 = i.W2,
                    H2 = i.H2,
                    OrderedQty = i.OrderedQty,
                    PreviouslyDeliveredQty = i.PreviouslyDeliveredQty,
                    DeliveredQty = i.DeliveredQty,
                    BalanceQty = i.BalanceQty,
                    ReworkQty = i.ReworkQty,
                    ReturnedQty = i.ReturnedQty,
                    WeightPerPiece = i.WeightPerPiece,
                    Notes = i.Notes
                }).ToList() ?? new(),
                Attachments = entity.Attachments?.Select(a => new DnAttachmentSnapshot
                {
                    AttachmentType = a.AttachmentType,
                    FileName = a.FileName,
                    FilePath = a.FilePath,
                    FileSize = a.FileSize,
                    Description = a.Description,
                    UploadedAt = a.UploadedAt
                }).ToList() ?? new()
            };
        }
    }
}