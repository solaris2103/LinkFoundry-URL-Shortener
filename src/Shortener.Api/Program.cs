using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shortener.Core;
using Shortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 32 * 1024;
});
var oidcAuthority = builder.Configuration["Authentication:Authority"];
var oidcAudience = builder.Configuration["Authentication:Audience"];
var requiredScope = builder.Configuration["Authentication:RequiredScope"];
if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(oidcAuthority) ||
    string.IsNullOrWhiteSpace(oidcAudience) || string.IsNullOrWhiteSpace(requiredScope)))
{
    throw new InvalidOperationException("Authentication:Authority, Authentication:Audience, and Authentication:RequiredScope are required outside Development.");
}

builder.Services.AddDbContext<ShortenerDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Shortener") ?? "Data Source=linkfoundry.db",
        sqlite => sqlite.MigrationsAssembly(typeof(ShortenerDbContext).Assembly.FullName)));
builder.Services.AddScoped<IShortLinkService, ShortLinkService>();
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(builder.Configuration["WebOrigin"] ?? "http://localhost:5173")
    .WithHeaders("Content-Type", "Authorization")
    .WithMethods("GET", "POST", "DELETE")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("create-link", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 20,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    }));
});
builder.Services.AddAuthorization(options => options.AddPolicy("link-operator", policy => policy
    .RequireAuthenticatedUser()
    .RequireAssertion(context => context.User.Claims
        .Where(claim => claim.Type is "scope" or "scp")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Contains(requiredScope ?? "shortener.links.manage", StringComparer.Ordinal))));

if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = oidcAuthority;
            options.Audience = oidcAudience;
            options.RequireHttpsMetadata = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });
    builder.Services.AddAuthorization();
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<ShortenerDbContext>();
    await database.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next();
});
app.UseCors("web");
app.UseRateLimiter();
if (!app.Environment.IsDevelopment())
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
Func<ShortenerDbContext, ILogger<Program>, CancellationToken, Task<IResult>> readinessCheck = async
    (ShortenerDbContext database, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await database.Database.CanConnectAsync(cancellationToken) ||
            (await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new { status = "ready" });
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Readiness check failed.");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
};
app.MapGet("/health/ready", readinessCheck);
app.MapGet("/health", readinessCheck);

var adminLinks = app.MapGroup("/api/links");
if (!app.Environment.IsDevelopment())
{
    adminLinks.RequireAuthorization("link-operator");
}

adminLinks.MapPost("/", async (CreateLinkRequest request, IShortLinkService links, CancellationToken cancellationToken) =>
{
    try
    {
        var created = await links.CreateAsync(request, cancellationToken);
        return Results.Created($"/api/links/{created.Code}", created);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
})
.RequireRateLimiting("create-link");
adminLinks.MapGet("/", async (IShortLinkService links, CancellationToken cancellationToken) =>
    Results.Ok(await links.ListAsync(cancellationToken)));
adminLinks.MapGet("/{code}", async (string code, IShortLinkService links, CancellationToken cancellationToken) =>
{
    var analytics = await links.GetAnalyticsAsync(code, cancellationToken);
    return analytics is null ? Results.NotFound() : Results.Ok(analytics);
});
adminLinks.MapDelete("/{code}", async (string code, IShortLinkService links, CancellationToken cancellationToken) =>
    await links.DeactivateAsync(code, cancellationToken) ? Results.NoContent() : Results.NotFound());
app.MapGet("/{code}", async (string code, HttpContext context, IShortLinkService links, CancellationToken cancellationToken) =>
{
    var destination = await links.RecordClickAsync(code, context.Request.Headers.Referer, cancellationToken);
    if (destination is null)
    {
        return Results.NotFound();
    }

    context.Response.Headers.CacheControl = "no-store";
    return Results.Redirect(destination);
});

app.Run();

public partial class Program;
