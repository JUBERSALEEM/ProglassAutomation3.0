using System.Net;
using System.Net.Http;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;

namespace ProGlassAutomation.ThemeTests;

/// <summary>Network-free checks for Storage request authentication and 401 diagnosis.</summary>
internal static class InquiryInboxStorageChecks
{
    public static void Run()
    {
        VerifyStorage(HttpStatusCode.OK);
        VerifyStorage(HttpStatusCode.Unauthorized);
        VerifyStorage(HttpStatusCode.Forbidden);
    }

    private static void VerifyStorage(HttpStatusCode storageStatus)
    {
        var handler = new StorageHandler(storageStatus);
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
            client.GetMyInquiryAccessAsync().GetAwaiter().GetResult();

            if (storageStatus == HttpStatusCode.OK)
            {
                var bytes = client.DownloadObjectAsync("inquiry", "documents/file.pdf", CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (System.Text.Encoding.UTF8.GetString(bytes) != "test-file")
                    throw new InvalidOperationException("Storage response bytes were not returned.");
            }
            else
            {
                try
                {
                    client.DownloadObjectAsync("inquiry", "documents/file.pdf", CancellationToken.None)
                        .GetAwaiter().GetResult();
                    throw new InvalidOperationException("Missing-API-key 401 was unexpectedly accepted.");
                }
                catch (InboxRemoteException error)
                {
                    var expectedKind = storageStatus == HttpStatusCode.Forbidden
                        ? InboxRemoteErrorKind.PermissionDenied
                        : InboxRemoteErrorKind.Remote;
                    if (error.Kind != expectedKind || error.HttpStatusCode != (int)storageStatus)
                        throw new InvalidOperationException("Storage refusal lost its error kind or HTTP status.");
                    if (storageStatus == HttpStatusCode.Unauthorized
                        && !error.Message.Contains("Storage download API-key", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Storage 401 was not identified as an API-key download issue.");
                }
            }

            if (!handler.StorageHadApiKey || !handler.StorageHadBearer)
                throw new InvalidOperationException("Storage GET omitted apikey or Bearer Authorization.");
        }
        finally
        {
            client.Dispose();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
        }
    }

    private sealed class StorageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _storageStatus;
        public bool StorageHadApiKey { get; private set; }
        public bool StorageHadBearer { get; private set; }

        public StorageHandler(HttpStatusCode storageStatus) => _storageStatus = storageStatus;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/auth/v1/token", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires_in\":3600}");
            if (path.EndsWith("/rpc/get_my_inquiry_access_v1", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK,
                    "{\"account_email\":\"staff@example.invalid\",\"roles\":[\"inquiry_reader\"],\"capabilities\":[\"inbox.plan.bridge\"],\"denial_reason\":null}");
            if (path.Contains("/storage/v1/object/", StringComparison.Ordinal))
            {
                StorageHadApiKey = request.Headers.Contains("apikey");
                StorageHadBearer = request.Headers.Authorization?.Scheme == "Bearer"
                    && request.Headers.Authorization.Parameter == "test-token";
                return Json(_storageStatus, _storageStatus == HttpStatusCode.Unauthorized
                    ? "{\"message\":\"No API key found in request\"}"
                    : "test-file");
            }
            throw new InvalidOperationException("Unexpected fake HTTP path: " + path);
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string text) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(text) });
    }
}
