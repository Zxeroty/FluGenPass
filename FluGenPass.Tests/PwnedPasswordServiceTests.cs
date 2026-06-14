using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluGenPass.Services;
using Xunit;

namespace FluGenPass.Tests;

public class PwnedPasswordServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFunc;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
        {
            _responseFunc = responseFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFunc(request));
        }
    }

    [Fact]
    public async Task GetPwnCountAsync_ShouldReturnZero_WhenPasswordIsEmptyOrNull()
    {
        using var client = new HttpClient(new MockHttpMessageHandler(req => throw new Exception("Should not call API")));
        using var service = new PwnedPasswordService(client);

        int countNull = await service.GetPwnCountAsync(null!);
        int countEmpty = await service.GetPwnCountAsync("");

        Assert.Equal(0, countNull);
        Assert.Equal(0, countEmpty);
    }

    [Fact]
    public async Task GetPwnCountAsync_ShouldReturnCorrectCount_WhenPasswordLeaked()
    {
        // "password" SHA-1 is 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8
        // Prefix: 5BAA6
        // Suffix: 1E4C9B93F3F0682250B6CF8331B7EE68FD8
        string password = "password";
        string suffix = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";
        int expectedCount = 3847012;

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://api.pwnedpasswords.com/range/5BAA6", req.RequestUri?.ToString());
            Assert.Equal("FluGenPass-PasswordManager", req.Headers.UserAgent.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{suffix}:{expectedCount}\nABCDEF1234567890:12\n")
            };
        });

        using var client = new HttpClient(handler);
        using var service = new PwnedPasswordService(client);

        int count = await service.GetPwnCountAsync(password);

        Assert.Equal(expectedCount, count);
    }

    [Fact]
    public async Task GetPwnCountAsync_ShouldReturnZero_WhenPasswordNotLeaked()
    {
        string password = "password";
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ABCDEF1234567890:12\n")
            };
        });

        using var client = new HttpClient(handler);
        using var service = new PwnedPasswordService(client);

        int count = await service.GetPwnCountAsync(password);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetPwnCountAsync_ShouldPropagateException_WhenRequestFails()
    {
        string password = "password";
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(handler);
        using var service = new PwnedPasswordService(client);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetPwnCountAsync(password));
    }
}
