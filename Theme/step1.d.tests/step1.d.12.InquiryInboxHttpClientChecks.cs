using System.Net;
using System.Net.Http;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;

namespace ProGlassAutomation.ThemeTests;

/// <summary>Network-free regression checks for process transport recovery.</summary>
internal static class InquiryInboxHttpClientChecks
{
    public static void Run()
    {
        var first = new DisposedOnceHandler(InquiryInboxClient.DisposeProcessHttpClientForTests);
        var second = new SuccessHandler();
        int factoryCalls = 0;
        InquiryInboxClient.SetHttpHandlerFactoryForTests(() => ++factoryCalls == 1 ? first : second);

        var settings = new InquiryInboxSettings
        {
            SupabaseUrl = "https://unit.invalid",
            AnonKey = "public-test-key",
            BridgeEmail = "operator@example.invalid"
        };
        var client = new InquiryInboxClient(settings);
        try
        {
            var result = client.SignInAsync().GetAwaiter().GetResult();
            if (!result.Success)
                throw new InvalidOperationException("Disposed HttpClient recovery failed: " + result.Error);
            if (first.Calls != 1 || !first.WasDisposed)
                throw new InvalidOperationException("Reset did not dispose the failed transport exactly once.");
            if (second.Calls != 1)
                throw new InvalidOperationException("Reset retry did not succeed exactly once.");
        }
        finally
        {
            client.Dispose();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
        }

        var disposedService = new InquiryInboxService(new InquiryInboxSettings());
        disposedService.Dispose();
        var stopped = disposedService.SynchronizeAsync().GetAwaiter().GetResult();
        if (stopped.Error != null || stopped.Offline || stopped.Ran)
            throw new InvalidOperationException("A disposed service did not return a neutral sync outcome.");
    }

    private sealed class DisposedOnceHandler : HttpMessageHandler
    {
        private readonly Action _disposeClient;
        public int Calls { get; private set; }
        public bool WasDisposed { get; private set; }

        public DisposedOnceHandler(Action disposeClient) => _disposeClient = disposeClient;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            _disposeClient();
            return Task.FromException<HttpResponseMessage>(new ObjectDisposedException(nameof(HttpClient)));
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600}")
            });
        }
    }
}
