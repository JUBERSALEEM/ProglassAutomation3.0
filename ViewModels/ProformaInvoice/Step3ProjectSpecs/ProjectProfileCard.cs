// ═══════════════════════════════════════════════════════════════════════
// FILE: ProjectProfileCard.cs.cs
// PROJECT: ProGlassAutomation
// MODULE: Project Specs
//
// ProjectProfileCard.cs split out (Phase 11/12 God-VM split) — behaviour unchanged.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.MasterData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step3ProjectSpecs
{
    public class ProjectProfileCard : ViewModelBase
    {
        public int Id { get; set; }
        public string ProjectIdentifierName { get; set; } = "";
        public string ProjectContractNo { get; set; } = "";
        public string ProjectReference { get; set; } = "";   // CHAT 15: NEW
        public string SiteDeliveryLocation { get; set; } = "";
        public string LpoNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public int UsageCount { get; set; }
        public string LastUsed { get; set; } = "";

        /// <summary>Usage badge label.</summary>
        public string UsageBadgeText =>
            UsageCount > 0 ? $"Used {UsageCount}×" : "New";

        /// <summary>
        /// List title is Project Name. Reference is shown on the subtitle row.
        /// </summary>
        public string DisplayProjectName =>
            !string.IsNullOrWhiteSpace(ProjectIdentifierName) ? ProjectIdentifierName
            : !string.IsNullOrWhiteSpace(ProjectReference) ? ProjectReference
            : ProjectContractNo;

        /// <summary>Customer context shown on card.</summary>
        public string CustomerPreview =>
            string.IsNullOrWhiteSpace(CustomerName)
                ? "No client linked"
                : CustomerName;

        /// <summary>
        /// Contract number — with clear "Project Name/No:" prefix.
        /// Truncated to 30 chars for card display.
        /// </summary>
        public string ContractPreview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ProjectContractNo))
                    return "No project no.";
                var v = ProjectContractNo.Length > 30
                    ? ProjectContractNo.Substring(0, 30) + "…"
                    : ProjectContractNo;
                return v;
            }
        }

        /// <summary>
        /// CHAT 15: NEW — Project reference preview with clear label.
        /// Descriptive text (e.g. "HOUSE FOR IMAD HABIB").
        /// Truncated to 40 chars.
        /// </summary>
        public string ReferencePreview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ProjectReference))
                    return "No reference";
                return ProjectReference.Length > 40
                    ? ProjectReference.Substring(0, 40) + "…"
                    : ProjectReference;
            }
        }
    }
}
