// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/MasterData/CompanyLogoLayout.cs
// PROJECT: ProGlassAutomation
// MODULE: Master Data — company branding
//
// How the saved company logo is placed on printed documents. Two industrial
// letterhead compositions (plus the text-only fallback):
//   None         — classic text letterhead (no logo).
//   LeftPortrait — a small portrait logo on the LEFT of the company identity,
//                  sized by LogoWidthMm × LogoHeightMm.
//   TopPanel     — the logo integrated into the top masthead panel of the
//                  letterhead (the full-width industrial band).
// The value is persisted as INTEGER in the Companies table.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Models.MasterData
{
    public enum CompanyLogoLayout
    {
        /// <summary>No logo — classic text-only letterhead.</summary>
        None = 0,

        /// <summary>Small portrait logo at the left of the company identity.</summary>
        LeftPortrait = 1,

        /// <summary>Logo integrated into the top industrial masthead panel.</summary>
        TopPanel = 2
    }
}
