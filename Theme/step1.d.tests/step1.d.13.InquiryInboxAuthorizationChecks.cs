using System.Net;
using System.Net.Http;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;

namespace ProGlassAutomation.ThemeTests;

/// <summary>Network-free checks for self-report display data and delivery gating.</summary>
internal static class InquiryInboxAuthorizationChecks
{
    public static void Run()
    {
        VerifyScenario(
            "{\"account_email\":\"staff@example.invalid\",\"roles\":[\"inquiry_reader\"],\"capabilities\":[],\"denial_reason\":\"role policy requires an inbox grant\"}",
            expectAllowed: false);
        VerifyScenario(
            "{\"account_email\":\"staff@example.invalid\",\"roles\":[\"inquiry_reader\"],\"capabilities\":[\"inbox.plan.bridge\"],\"denial_reason\":null}",
            expectAllowed: true);
    }

    private static void VerifyScenario(string reportJson, bool expectAllowed)
    {
        var handler = new AccessHandler(reportJson);
        InquiryInboxClient.SetHttpHandlerFactoryForTests(() => handler);
        var client = new InquiryInboxClient(new InquiryInboxSettings
        {
            SupabaseUrl = "https://unit.invalid",
            AnonKey = "public-test-key",
            BridgeEmail = "staff@example.invalid"
        });
        try
        {
            var signIn = client.SignInAsync().GetAwaiter().GetResult();
            if (!signIn.Success) throw new InvalidOperationException("Fake sign-in failed: " + signIn.Error);
            var access = client.GetMyInquiryAccessAsync().GetAwaiter().GetResult();
            if (access.AccountEmail != "staff@example.invalid" || access.RolesText != "inquiry_reader")
                throw new InvalidOperationException("Self-reported email or role was not preserved.");

            bool denied = false;
            try { client.PlanDeliveriesAsync(10, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InboxRemoteException error) when (error.Kind == InboxRemoteErrorKind.PermissionDenied)
            {
                denied = true;
                if (!error.Message.Contains("staff@example.invalid", StringComparison.Ordinal)
                    || !error.Message.Contains("inquiry_reader", StringComparison.Ordinal)
                    || !error.Message.Contains("inbox.plan.bridge", StringComparison.Ordinal))
                    throw new InvalidOperationException("Missing-capability error omitted the account, role, or capability.");
                if (!expectAllowed && !error.Message.Contains("role policy requires an inbox grant", StringComparison.Ordinal))
                    throw new InvalidOperationException("Non-null denial_reason was not preserved verbatim.");
            }

            if (denied == expectAllowed)
                throw new InvalidOperationException("Delivery capability gate returned the wrong result.");
            if (!expectAllowed && handler.PlanCalls != 0)
                throw new InvalidOperationException("A delivery RPC ran without inbox.plan.bridge.");
            if (expectAllowed && handler.PlanCalls != 1)
                throw new InvalidOperationException("Authorized account did not reach the plan RPC.");
            if (handler.Paths.Count < 2 || !handler.Paths[0].Contains("/auth/v1/token", StringComparison.Ordinal)
                || !handler.Paths[1].Contains("/rpc/get_my_inquiry_access_v1", StringComparison.Ordinal))
                throw new InvalidOperationException("The self-report was not fetched immediately after sign-in.");
        }
        finally
        {
            client.Dispose();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
        }
    }

    private sealed class AccessHandler : HttpMessageHandler
    {
        private readonly string _reportJson;
        public List<string> Paths { get; } = new();
        public int PlanCalls { get; private set; }

        public AccessHandler(string reportJson) => _reportJson = reportJson;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            Paths.Add(path);
            if (path.Contains("/auth/v1/token", StringComparison.Ordinal))
                return Json("{\"access_token\":\"test-token\",\"expires_in\":3600}");
            if (path.EndsWith("/rpc/get_my_inquiry_access_v1", StringComparison.Ordinal))
                return Json(_reportJson);
            if (path.EndsWith("/rpc/plan_customer_inquiry_deliveries_v2", StringComparison.Ordinal))
            {
                PlanCalls++;
                return Json("[]");
            }
            throw new InvalidOperationException("Unexpected fake HTTP path: " + path);
        }

        private static Task<HttpResponseMessage> Json(string json) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });
    }
}
