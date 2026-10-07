using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shortener.Core;
using Shortener.Infrastructure;

namespace Shortener.Tests;

public sealed class ShortenerApiTests
{
    [Fact]
    public async Task Api_creates_redirects_tracks_analytics_and_deactivates()
    {
        using var factory = new ShortenerApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var createdResponse = await client.PostAsJsonAsync("/api/links", new
        {
            destinationUrl = "https://example.com/engineering",
            customCode = "review-demo",
            expiresAt = (DateTimeOffset?)null
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.Equal("nosniff", createdResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", createdResponse.Headers.CacheControl?.ToString());
        using var createdJson = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        var shortUrl = createdJson.RootElement.GetProperty("shortUrl").GetString();
        Assert.Equal("http://short.test/review-demo", shortUrl);
        var listedLinks = await client.GetFromJsonAsync<JsonElement[]>("/api/links");
        Assert.Equal("review-demo", listedLinks?[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        using var redirectRequest = new HttpRequestMessage(HttpMethod.Get, "/review-demo");
        redirectRequest.Headers.Referrer = new Uri("https://newsletter.example/issue");
        var redirectResponse = await client.SendAsync(redirectRequest);
        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        Assert.Equal("https://example.com/engineering", redirectResponse.Headers.Location?.OriginalString);

        using var analyticsJson = JsonDocument.Parse(await client.GetStringAsync("/api/links/review-demo"));
        Assert.Equal(1, analyticsJson.RootElement.GetProperty("link").GetProperty("clickCount").GetInt32());
        Assert.Equal("newsletter.example", analyticsJson.RootElement.GetProperty("topReferrers")[0].GetProperty("host").GetString());

        var deactivateResponse = await client.DeleteAsync("/api/links/review-demo");
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/review-demo")).StatusCode);
    }

    [Fact]
    public async Task Api_rejects_non_http_destination_with_validation_problem()
    {
        using var factory = new ShortenerApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new
        {
            destinationUrl = "javascript:alert(1)",
            customCode = (string?)null,
            expiresAt = (DateTimeOffset?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Api_rate_limits_link_creation_per_source_ip()
    {
        using var factory = new ShortenerApiFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 0; requestNumber < 20; requestNumber++)
        {
            var response = await client.PostAsJsonAsync("/api/links", new
            {
                destinationUrl = $"https://example.com/{requestNumber}",
                customCode = (string?)null,
                expiresAt = (DateTimeOffset?)null
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var limitedResponse = await client.PostAsJsonAsync("/api/links", new
        {
            destinationUrl = "https://example.com/over-limit",
            customCode = (string?)null,
            expiresAt = (DateTimeOffset?)null
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
    }

    [Fact]
    public async Task Production_requires_authentication_for_link_management()
    {
        using var factory = new ShortenerApiFactory(production: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/links");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Production_requires_management_scope_for_link_management()
    {
        using var factory = new ShortenerApiFactory(production: true, legacyDatabase: true, testAuthentication: true);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/links")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Scope", "shortener.links.manage");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/links")).StatusCode);
    }

    [Fact]
    public async Task Development_migration_preserves_pre_migration_links()
    {
        using var factory = new ShortenerApiFactory(legacyDatabase: true);
        using var client = factory.CreateClient();

        var links = await client.GetFromJsonAsync<JsonElement[]>("/api/links");

        Assert.Equal("legacy-link", links?[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private sealed class ShortenerApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"shortener-api-{Guid.NewGuid():N}.db");
        private readonly bool _production;
        private readonly bool _testAuthentication;

        public ShortenerApiFactory(bool production = false, bool legacyDatabase = false, bool testAuthentication = false)
        {
            _production = production;
            _testAuthentication = testAuthentication;
            if (legacyDatabase)
            {
                var options = new DbContextOptionsBuilder<ShortenerDbContext>()
                    .UseSqlite($"Data Source={_databasePath}")
                    .Options;
                using var database = new ShortenerDbContext(options);
                database.Database.EnsureCreated();
                database.Links.Add(new ShortLink
                {
                    Code = "legacy-link",
                    DestinationUrl = "https://example.com/legacy",
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                database.SaveChanges();
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_production ? "Production" : "Development");
            builder.UseSetting("Authentication:Authority", "https://identity.example.test");
            builder.UseSetting("Authentication:Audience", "linkfoundry-api");
            builder.UseSetting("Authentication:RequiredScope", "shortener.links.manage");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Shortener"] = $"Data Source={_databasePath}",
                ["PublicBaseUrl"] = "http://short.test",
                ["Authentication:Authority"] = "https://identity.example.test",
                ["Authentication:Audience"] = "linkfoundry-api",
                ["Authentication:RequiredScope"] = "shortener.links.manage"
            }));
            if (_testAuthentication)
            {
                builder.ConfigureTestServices(services => services.AddAuthentication(options =>
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { }));
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(_databasePath)) File.Delete(_databasePath);
            }
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "IntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var scope = Request.Headers.TryGetValue("X-Test-Scope", out var requestedScope)
                ? requestedScope.ToString()
                : "shortener.links.read";
            var identity = new ClaimsIdentity([new Claim("scope", scope)], SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}