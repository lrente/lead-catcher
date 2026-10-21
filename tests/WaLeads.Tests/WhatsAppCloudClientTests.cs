using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WaLeads.Infrastructure;

namespace WaLeads.Tests;

public sealed class WhatsAppCloudClientTests
{
    [Fact]
    public async Task SendsTextToConfiguredPhoneNumberAndReturnsMetaMessageId()
    {
        using var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        var sender = new WhatsAppCloudClient(
            httpClient,
            Options.Create(new WhatsAppCloudOptions { GraphApiVersion = "v23.0" }));

        var messageId = await sender.SendTextAsync(
            "phone-id",
            "351910000000",
            "Olá, como podemos ajudar?",
            CancellationToken.None);

        Assert.Equal("wamid.test-outbound", messageId);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v23.0/phone-id/messages", handler.Path);
        Assert.Equal("Bearer test-token", handler.Authorization);
        using var requestBody = JsonDocument.Parse(handler.Body!);
        var root = requestBody.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", root.GetProperty("recipient_type").GetString());
        Assert.Equal("351910000000", root.GetProperty("to").GetString());
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Olá, como podemos ajudar?", root.GetProperty("text").GetProperty("body").GetString());
    }

    [Theory]
    [InlineData("v23.0", true)]
    [InlineData("23.0", false)]
    [InlineData("v23", false)]
    [InlineData("v23.0/messages", false)]
    public void ValidatesGraphApiVersionFormat(string version, bool expected) =>
        Assert.Equal(expected, WhatsAppCloudClient.IsGraphApiVersion(version));

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"messages":[{"id":"wamid.test-outbound"}]}""")
            };
        }
    }
}
