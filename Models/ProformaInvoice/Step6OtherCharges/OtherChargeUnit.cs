namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Unit of measurement for an Other Charge line.
    /// Drives which Grid Source options are shown in the dropdown.
    /// </summary>
    public enum OtherChargeUnit
    {
        Sqm,   // Square meters (area-based)
        Lm,    // Linear meters (perimeter-based)
        Qty    // Piece count (integer)
    }
}