// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.1.ProformaInvoiceWorkspace.cs
// MODULE: step6 — the wizard engine: one object a screen binds to.
//
// It owns the draft, the current stage, the stage markers and every mutation
// that changes the document. Whatever changes, it calls the draft's ONE
// recalculation, then revalidates, so a screen never has to remember the order
// in which totals are derived.
//
// STAGES (ADV numbers in brackets)
//   1 Company · 2 Client · 3 Project  all retain separate navigation and one shared snapshot
//   2 Pricing [ADV 4]            glass build → rate per m²
//   3 Specification grid [ADV 5] measured panels and values
//   4 Other charges [ADV 6]      predefined, wastage, custom
//   5 Summary [ADV 7]            discount, VAT, grand total
//   6 Attachments [ADV 8]        the evidence chain
//   7 Profit & loss [ADV 9]      costs, profit, margin, target KPI
//
// No WPF type appears here, so the whole engine is exercisable without a
// window and the same rules run on every path.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Proforma;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>Drives stage 1–9 of one proforma invoice.</summary>
    public sealed class ProformaInvoiceWorkspace
    {
        private readonly IProformaInvoiceRepository _repository;
        private readonly InvoicePartyReader _parties;

        public ProformaInvoiceWorkspace()
            : this(null, null) { }

        public ProformaInvoiceWorkspace(
            IProformaInvoiceRepository? repository = null,
            InvoicePartyReader? parties = null)
        {
            _repository = repository ?? new ProformaInvoiceRepository();
            _parties = parties ?? new InvoicePartyReader();
        }

        public ProformaInvoiceDraft Draft { get; private set; } = new();

        public InvoiceStage CurrentStage { get; private set; } = InvoiceStage.Company;

        public InvoiceValidationResult Validation { get; private set; } = new();

        public IReadOnlyList<StageStatus> StageStatuses { get; private set; } = Array.Empty<StageStatus>();

        public IReadOnlyList<string> Messages { get; private set; } = Array.Empty<string>();

        public bool IsPersisted => Draft.IsPersisted;

        public bool CanEdit => Draft.IsEditable;

        // ── Lifecycle ───────────────────────────────────────────────────

        /// <summary>
        /// A new invoice with its header read from the chosen master records.
        /// Nothing is invented: no client, project or user is guessed.
        /// </summary>
        public async Task NewDraftAsync(
            int? clientId = null,
            int? projectId = null,
            int? preparedByUserId = null,
            int? inquiryId = null,
            CancellationToken ct = default)
        {
            InvoicePartyResult parties = await _parties.ComposeAsync(
                clientId, projectId, preparedByUserId, inquiryId, ct).ConfigureAwait(false);

            Draft = new ProformaInvoiceDraft
            {
                Status = ProformaInvoiceStatus.Draft,
                IssueDate = DateTime.Today,
                ValidUntil = DateTime.Today.AddDays(7),
                Parties = parties.Parties
            };
            Draft.EnsureStructure();
            CurrentStage = InvoiceStage.Company;
            Messages = parties.Warnings;
            RefreshDerived();
        }

        /// <summary>Loads a saved invoice and restores the stage it was left on.</summary>
        public async Task<bool> LoadAsync(int id, CancellationToken ct = default)
        {
            ProformaInvoiceDraft? draft = await _repository.GetAsync(id, ct).ConfigureAwait(false);
            if (draft == null)
            {
                Messages = new[]
                {
                    "The invoice could not be opened. Its stored document body is missing or unreadable; "
                    + "nothing was changed."
                };
                return false;
            }

            Draft = draft;
            Draft.EnsureStructure();
            CurrentStage = Draft.FurthestStageReached == 0
                ? InvoiceStage.Company : Draft.FurthestStageReached;
            Messages = Array.Empty<string>();
            RefreshDerived();
            return true;
        }

        /// <summary>Moves to a stage and remembers it as the furthest reached.</summary>
        public void GoToStage(InvoiceStage stage)
        {
            CurrentStage = stage;
            if ((int)stage > (int)Draft.FurthestStageReached)
                Draft.FurthestStageReached = stage;
        }

        // ── Stage 1: header ─────────────────────────────────────────────

        /// <summary>Reads the chosen client/project/user/inquiry into the header.</summary>
        public async Task RefreshPartiesAsync(
            int? clientId = null,
            int? projectId = null,
            int? preparedByUserId = null,
            int? inquiryId = null,
            CancellationToken ct = default)
        {
            if (!CanEdit)
            {
                Messages = new[] { "A " + Draft.Status + " invoice keeps the parties it was issued with." };
                return;
            }

            InvoicePartyResult parties = await _parties.ComposeAsync(
                clientId ?? (Draft.Parties.Client.ClientId > 0 ? Draft.Parties.Client.ClientId : null),
                projectId ?? (Draft.Parties.Project.ProjectId > 0 ? Draft.Parties.Project.ProjectId : null),
                preparedByUserId ?? Draft.Parties.PreparedBy.UserId,
                inquiryId ?? Draft.Parties.Inquiry.InquiryId,
                ct).ConfigureAwait(false);

            Draft.Parties = parties.Parties;
            Messages = parties.Warnings;
            RefreshDerived();
        }

        /// <summary>
        /// A one-off customer with no client row. The name is still required;
        /// this is the fallback the Inquiries module also allows.
        /// </summary>
        public void SetWalkInCustomer(string name, string attention = "", string phone = "",
            string email = "", string trn = "", string address = "")
        {
            Draft.Parties.Client = new ClientIdentity
            {
                ClientId = 0,
                ClientName = ProformaInvoiceRules.NormalizeText(name, ProformaInvoiceRules.ClientNameMaxLength),
                AttentionName = ProformaInvoiceRules.NormalizeText(attention, 200),
                Phone = ProformaInvoiceRules.NormalizeText(phone, 40),
                Email = ProformaInvoiceRules.NormalizeText(email, 120),
                Trn = ProformaInvoiceRules.NormalizeText(trn, 20),
                BillingAddress = ProformaInvoiceRules.NormalizeText(address, 300)
            };
            Draft.Parties.RefreshWarnings = new List<string>();
            RefreshDerived();
        }

        /// <summary>Issue and validity dates; the validity rule is checked on save.</summary>
        public void SetDates(DateTime issueDate, DateTime validUntil)
        {
            Draft.IssueDate = issueDate.Date;
            Draft.ValidUntil = validUntil.Date;
            RefreshDerived();
        }

        // ── Stage 2: pricing ────────────────────────────────────────────

        /// <summary>The live result of one pricing section (never cached).</summary>
        public PricingResult CalculateSection(int sectionNumber)
        {
            PricingSection? section = Draft.PricingSections
                .FirstOrDefault(item => item.SectionNumber == sectionNumber);
            return section == null
                ? new PricingResult
                {
                    Warnings = new List<string> { "Pricing section " + sectionNumber + " was not found." }
                }
                : PricingEstimator.Calculate(section);
        }

        /// <summary>Adds a grid group from a priced section. Returns its warnings.</summary>
        public PricingResult AddGroupFromSection(int sectionNumber)
        {
            if (!CanEdit)
                return Refused("A " + Draft.Status + " invoice cannot gain new specifications.");

            PricingResult result = Draft.AddGroupFromSection(sectionNumber);
            Messages = result.Warnings;
            RefreshDerived();
            return result;
        }

        // ── Stage 3: grid ───────────────────────────────────────────────

        public SpecRow AddRow(int groupNumber)
        {
            SpecGroup? group = Draft.Groups.FirstOrDefault(item => item.GroupNumber == groupNumber);
            if (group == null || !CanEdit) return new SpecRow();
            var row = new SpecRow { GlassRef = "W1", Qty = 1 };
            group.Rows.Add(row);
            group.Renumber();
            RefreshDerived();
            return row;
        }

        // ── Stage 4: charges ────────────────────────────────────────────

        /// <summary>Operator switch for a predefined charge, recorded as a choice.</summary>
        public void SetChargeActive(string templateKey, bool active)
        {
            PredefinedChargeRow? row = Draft.Charges.Predefined.FirstOrDefault(item =>
                string.Equals(item.TemplateKey, templateKey, StringComparison.OrdinalIgnoreCase));
            if (row == null || !CanEdit) return;
            row.SetManualActive(active);
            RefreshDerived();
        }

        /// <summary>Operator switch for a wastage row (also a manual choice).</summary>
        public void SetWastageActive(string colorName, bool active)
        {
            WastageChargeRow? row = Draft.Charges.Wastages.FirstOrDefault(item =>
                string.Equals(item.ColorName.Trim(), (colorName ?? "").Trim(),
                    StringComparison.OrdinalIgnoreCase));
            if (row == null || !CanEdit) return;
            row.IsActive = active;
            RefreshDerived();
        }

        public void AddCustomCharge(string name, decimal quantity, ChargeUnit unit, decimal rate)
        {
            if (!CanEdit) return;
            Draft.Charges.Custom.Add(new CustomChargeRow
            {
                IsActive = true,
                ChargeName = ProformaInvoiceRules.NormalizeText(name, ProformaInvoiceRules.ChargeNameMaxLength),
                Qty = quantity,
                Unit = unit,
                Rate = rate
            });
            RefreshDerived();
        }

        // ── Stage 5: summary ────────────────────────────────────────────

        public void SetDiscount(DiscountType type, decimal value)
        {
            if (!CanEdit) return;
            Draft.Summary.DiscountMethod = type;
            Draft.Summary.DiscountValue = value;
            RefreshDerived();
        }

        public void SetVat(bool applicable, decimal percent)
        {
            if (!CanEdit) return;
            Draft.Summary.IsVatApplicable = applicable;
            Draft.Summary.VatPercent = percent;
            RefreshDerived();
        }

        /// <summary>"Make the total 15,500" — true when the target was reachable.</summary>
        public bool SetTargetGrandTotal(decimal targetGrandTotal)
        {
            if (!CanEdit) return false;
            Draft.Recalculate();
            bool reached = Draft.Summary.SetTargetGrandTotal(targetGrandTotal);
            RefreshDerived();
            return reached;
        }

        // ── Stage 7: profit & loss ──────────────────────────────────────

        public void SetWastagePercent(decimal percent)
        {
            if (!CanEdit) return;
            Draft.ProfitLossSettings.WastagePercent =
                MoneyRounding.ClampPercent(percent, 0m, 95m);
            RefreshDerived();
        }

        // ── Inquiry import ──────────────────────────────────────────────

        public async Task<BoqImportResult> ImportInquiryScopeAsync(
            int? inquiryId = null, CancellationToken ct = default)
        {
            int id = inquiryId ?? Draft.Parties.Inquiry.InquiryId ?? 0;
            BoqImportResult result = await InquiryBoqConverter
                .ImportAsync(Draft, id, null, ct).ConfigureAwait(false);
            Messages = result.Messages;
            RefreshDerived();
            return result;
        }

        // ── Save and status ─────────────────────────────────────────────

        public async Task<ProformaInvoiceSaveResult> SaveAsync(CancellationToken ct = default)
        {
            int expected = Draft.RowVersion;
            ProformaInvoiceSaveResult result = await _repository
                .SaveAsync(Draft, expected, ct).ConfigureAwait(false);

            if (result.Succeeded && result.Invoice != null)
            {
                Draft = result.Invoice;
                Messages = result.Warnings;
                if (result.NumberWasAllocated)
                    Messages = result.Warnings
                        .Concat(new[] { "Numbered " + Draft.ProformaNumber + "." })
                        .ToList();
            }
            else
            {
                Messages = new[] { result.Message };
            }

            RefreshDerived(result.Warnings);
            return result;
        }

        public async Task<ProformaInvoiceStatusResult> ChangeStatusAsync(
            string targetStatus, CancellationToken ct = default)
        {
            ProformaInvoiceStatusResult result = await _repository.ChangeStatusAsync(
                Draft.Id, targetStatus, Draft.RowVersion, ct).ConfigureAwait(false);

            if (result.Succeeded)
            {
                // The row's version moved in the database. Reload rather than
                // guessing the number: a guessed version would make the next save
                // look like a conflict with someone else.
                ProformaInvoiceDraft? fresh = await _repository
                    .GetAsync(Draft.Id, ct).ConfigureAwait(false);
                if (fresh != null)
                {
                    Draft = fresh;
                }
                else
                {
                    Draft.Status = result.Status;
                }
                Messages = new[] { "Status is now " + result.Status + "." };
                RefreshDerived();
            }
            else
            {
                Messages = new[] { result.Message };
            }
            return result;
        }

        public async Task<IReadOnlyList<ProformaInvoiceListRow>> LoadRegisterAsync(
            ProformaInvoiceQuery query, CancellationToken ct = default) =>
            await _repository.QueryAsync(query, ct).ConfigureAwait(false);

        public Task<ProformaAttachmentAddResult> AttachAsync(
            string sourceFilePath, string attachmentType = "Document", string note = "",
            CancellationToken ct = default) =>
            _repository.AddAttachmentAsync(Draft.Id, sourceFilePath, attachmentType, note, ct);

        /// <summary>
        /// Recomputes everything, revalidates and refreshes the stage markers.
        /// Called after every mutation, so no caller can leave a stale total.
        /// </summary>
        public void RefreshDerived(IReadOnlyList<string>? extraMessages = null)
        {
            Draft.Recalculate();
            Validation = ProformaInvoiceRules.Validate(Draft);
            StageStatuses = ProformaInvoiceRules.EvaluateStages(Draft);
            if (extraMessages != null && extraMessages.Count > 0)
                Messages = extraMessages;
        }

        private static PricingResult Refused(string message)
        {
            var result = new PricingResult();
            result.Warnings.Add(message);
            return result;
        }
    }
}
