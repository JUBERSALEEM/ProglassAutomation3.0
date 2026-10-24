using System.Text.Json;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;

namespace ProGlassAutomation.ThemeTests;

/// <summary>Regression checks for the live PostgREST jsonb plan-row contract.</summary>
internal static class InquiryInboxJsonbChecks
{
    public static void Run()
    {
        Expect(
            "{\"assignment_snapshot\":[{\"role\":\"primary\",\"estimatorCode\":\"E-12\"}]}",
            "[{\"role\":\"primary\",\"estimatorCode\":\"E-12\"}]",
            "JSONB array");
        Expect(
            "{\"assignment_snapshot\":{\"role\":\"primary\"}}",
            "{\"role\":\"primary\"}",
            "JSONB object");
        Expect("{\"assignment_snapshot\":null}", null, "JSONB null");
        Expect(
            "{\"assignment_snapshot\":\"[{\\\"role\\\":\\\"primary\\\"}]\"}",
            "[{\"role\":\"primary\"}]",
            "legacy JSON string");
    }

    private static void Expect(string row, string? expected, string label)
    {
        var plan = JsonSerializer.Deserialize<InboxSubmissionPlan>(row)
            ?? throw new InvalidOperationException(label + " failed: DTO was null.");
        if (plan.AssignmentSnapshotRaw != expected)
            throw new InvalidOperationException(label + " failed: unexpected raw snapshot.");
    }
}
