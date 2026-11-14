using CsvHelper;
using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Utilities;
using System;
using System.Linq;

using System.Globalization;
namespace ProGlassAutomation.Services.DeliveryNote.Export
{
    /// <summary>
    /// CSV layout for a Delivery Note — flat item table.
    ///
    /// One row per glass item with all identifying context columns
    /// so the file is machine-parseable and self-contained.
    ///
    /// BUG-58: Added null guard on Items before OrderBy.
    /// BUG-60: DocumentTitle uses shared constant.
    ///
    /// Columns:
    ///   DN ID · JO ID · Customer · Project · Status · Type · FOC ·
    ///   Date · Driver · Vehicle · Receiver ·
    ///   Spec # · Spec Group · Row # · Glass Ref ·
    ///   W1 · H1 · W2 · H2 ·
    ///   Ordered · Prev Delivered · This DN · Balance
    /// </summary>
    public class DeliveryNoteCsvLayout : ICsvLayout
    {
        private readonly SavedDeliveryNote _entity;
        private readonly DeliveryNoteSnapshot _snapshot;
        private readonly DnJobOrderRefSnapshot _joRef;

        public DeliveryNoteCsvLayout(
            SavedDeliveryNote entity,
            DeliveryNoteSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _joRef = snapshot.JobOrderRef ?? new DnJobOrderRefSnapshot();
        }

        // ── IDocumentLayout ──────────────────────────────────────────────
        public string DocumentPrefix => "DN";
        public string DocumentId => _entity.DeliveryNoteId;
        public string DocumentTitle => DeliveryNoteExportConstants.DocumentTitle;
        public string CompanyName => ExportFileNamer.FirstFilled(
            _joRef.CustomerName, _entity.CustomerName);
        public string Reference => ExportFileNamer.FirstFilled(
            _joRef.ProjectName, _entity.ProjectName, _joRef.ProjectReference);
        public DateTime DocumentDate =>
            _entity.DeliveryDate != default
                ? _entity.DeliveryDate
                : _entity.CreatedAt;

        public void WriteRows(CsvWriter writer)
        {
            // ── Header row ────────────────────────────────────────────────
            writer.WriteField("DeliveryNoteId");
            writer.WriteField("JobOrderId");
            writer.WriteField("Customer");
            writer.WriteField("ProjectName");
            writer.WriteField("ProjectReference");
            writer.WriteField("Status");
            writer.WriteField("DeliveryType");
            writer.WriteField("IsFoc");
            writer.WriteField("DeliveryDate");
            writer.WriteField("DeliveryTime");
            writer.WriteField("DriverName");
            writer.WriteField("VehicleNumber");
            writer.WriteField("ReceiverName");
            writer.WriteField("SpecNumber");
            writer.WriteField("SpecGroup");
            writer.WriteField("RowNumber");
            writer.WriteField("GlassRef");
            writer.WriteField("W1");
            writer.WriteField("H1");
            writer.WriteField("W2");
            writer.WriteField("H2");
            writer.WriteField("SQM");
            writer.WriteField("Ordered");
            writer.WriteField("PrevDelivered");
            writer.WriteField("ThisDN");
            writer.WriteField("Balance");
            writer.WriteField("WeightPerPiece");
            writer.WriteField("TotalWeight");
            writer.NextRecord();

            // ── Shared context values ─────────────────────────────────────
            string dnId = _entity.DeliveryNoteId;
            string joId = _entity.JobOrderId;
            string customer = _joRef.CustomerName;
            string project = _joRef.ProjectName;
            string projectReference = _joRef.ProjectReference;
            string status = _entity.Status;
            string type = _entity.DeliveryType;
            string foc = _entity.IsFoc ? "Yes" : "No";
            string date = _entity.DeliveryDate.ToString("yyyy-MM-dd");
            string time = _entity.DeliveryTime;
            string driver = _entity.DriverName;
            string vehicle = _entity.VehicleNumber;
            string receiver = _entity.ReceiverName;

            // BUG-58: null guard on Items before OrderBy
            if (_entity.Items == null || _entity.Items.Count == 0)
                return;

            var items = _entity.Items
                .OrderBy(i => i.SpecGroupNumber)
                .ThenBy(i => i.SerialNumber)
                .ToList();

            foreach (var item in items)
            {
                writer.WriteField(dnId);
                writer.WriteField(joId);
                writer.WriteField(customer);
                writer.WriteField(project);
                writer.WriteField(projectReference);
                writer.WriteField(status);
                writer.WriteField(type);
                writer.WriteField(foc);
                writer.WriteField(date);
                writer.WriteField(time);
                writer.WriteField(driver);
                writer.WriteField(vehicle);
                writer.WriteField(receiver);
                writer.WriteField(item.SpecGroupNumber);
                writer.WriteField(item.SpecGroupName ?? "");
                writer.WriteField(item.SerialNumber);
                writer.WriteField(item.GlassRef ?? "");
                writer.WriteField(item.W1);
                writer.WriteField(item.H1);
                writer.WriteField(item.W2);
                writer.WriteField(item.H2);
                writer.WriteField(SqmCalculator.CalculatePanel(item.W1, item.H1).ToString("0.000", CultureInfo.InvariantCulture));
                writer.WriteField(item.OrderedQty);
                writer.WriteField(item.PreviouslyDeliveredQty);
                writer.WriteField(item.DeliveredQty);
                writer.WriteField(item.BalanceQty);
                writer.WriteField(item.WeightPerPiece);
                writer.WriteField(item.TotalWeight);
                writer.NextRecord();
            }
        }
    }
}