using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PlayerFeedback.Api;
using PlayerFeedback.Api.Auth;
using PlayerFeedback.Api.Hubs;
using PlayerFeedback.Api.Workers;
using PlayerFeedback.Core.Analysis;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;
using PlayerFeedback.Core.Scraping;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Options ----
builder.Services.Configure<AuthOptions>(config.GetSection("Auth"));
builder.Services.Configure<LlmOptions>(config.GetSection("Llm"));
builder.Services.Configure<ScraperOptions>(config.GetSection("GooglePlay"));
builder.Services.Configure<WorkerOptions>(config.GetSection("Workers"));
builder.Services.Configure<FeedbackOptions>(config.GetSection("Feedback"));

// ---- Database ----
var connString = config.GetConnectionString("Postgres")
    ?? "Host=postgres;Port=5432;Database=playerfeedback;Username=pf;Password=pf_local_dev";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connString));

// ---- MVC / JSON ----
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();

// ---- Auth ----
var authOptions = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = authOptions.JwtIssuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(JwtTokenService.KeyBytes(authOptions.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        // Allow SignalR to pass the token via query string for websockets.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ---- CORS (only needed for cross-origin local dev; deployed stack is same-origin) ----
var allowedOrigins = config.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(allowedOrigins.Length > 0 ? allowedOrigins : new[] { "http://localhost:5173", "http://localhost:8090" })
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ---- SignalR ----
var signalrEnabled = config.GetValue("SignalR:Enabled", true);
builder.Services.AddSignalR();
if (signalrEnabled)
    builder.Services.AddScoped<IFeedbackNotifier, SignalRFeedbackNotifier>();
else
    builder.Services.AddScoped<IFeedbackNotifier, NoopNotifier>();

// ---- HTTP clients ----
var llmOptions = config.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
builder.Services.AddHttpClient<OllamaChatClient>(c =>
    c.Timeout = TimeSpan.FromSeconds(Math.Max(10, llmOptions.TimeoutSeconds) + 15));
builder.Services.AddHttpClient<IGooglePlayScraper, GooglePlayScraperClient>(c =>
    c.Timeout = TimeSpan.FromSeconds(140));

// ---- Analyzer + summarizer provider selection ----
var provider = (config.GetValue<string>("Llm:Provider") ?? "Ollama").Trim();
if (provider.Equals("Mock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IFeedbackAnalyzer, DeterministicMockFeedbackAnalyzer>();
    builder.Services.AddScoped<IAggregateSummarizer, MockAggregateSummarizer>();
}
else
{
    builder.Services.AddScoped<IFeedbackAnalyzer, OllamaFeedbackAnalyzer>();
    builder.Services.AddScoped<IAggregateSummarizer, OllamaAggregateSummarizer>();
}

// ---- Activity log (live worker stream) ----
builder.Services.AddSingleton<PlayerFeedback.Api.Activity.IActivityLog, PlayerFeedback.Api.Activity.ActivityLog>();

// ---- Background workers ----
builder.Services.AddHostedService<ImportWorker>();
builder.Services.AddHostedService<AnalysisWorker>();
builder.Services.AddHostedService<SummaryWorker>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

// ---- Health endpoints ----
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db) =>
{
    try { return await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.Json(new { status = "not-ready" }, statusCode: 503); }
    catch { return Results.Json(new { status = "not-ready" }, statusCode: 503); }
}).AllowAnonymous();
app.MapGet("/health/dependencies", async (AppDbContext db, IHttpClientFactory http,
    Microsoft.Extensions.Options.IOptions<LlmOptions> llm, Microsoft.Extensions.Options.IOptions<ScraperOptions> scr) =>
{
    var dbOk = await SafeCheck(async () => await db.Database.CanConnectAsync());
    var client = http.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(3);
    var ollamaOk = await SafeCheck(async () => (await client.GetAsync($"{llm.Value.BaseUrl.TrimEnd('/')}/api/tags")).IsSuccessStatusCode);
    var scraperOk = await SafeCheck(async () => (await client.GetAsync($"{scr.Value.ScraperBaseUrl.TrimEnd('/')}/health")).IsSuccessStatusCode);
    var queueDepth = await SafeCount(() => db.Feedback.CountAsync(f =>
        f.Status == FeedbackStatus.Pending || f.Status == FeedbackStatus.RetryScheduled));
    var oldest = await db.Feedback.Where(f => f.Status == FeedbackStatus.Pending)
        .OrderBy(f => f.CreatedAt).Select(f => (DateTime?)f.CreatedAt).FirstOrDefaultAsync();
    return Results.Ok(new
    {
        postgres = dbOk ? "healthy" : "unhealthy",
        ollama = ollamaOk ? "healthy" : "degraded",
        scraper = scraperOk ? "healthy" : "degraded",
        queueDepth,
        oldestPendingAgeSeconds = oldest is null ? 0 : (int)(DateTime.UtcNow - oldest.Value).TotalSeconds
    });
}).RequireAuthorization();

app.MapGet("/api/activity", (PlayerFeedback.Api.Activity.IActivityLog log, int? limit) =>
    Results.Ok(log.Snapshot(Math.Clamp(limit ?? 200, 1, 500)))).RequireAuthorization();

app.MapControllers();
app.MapHub<FeedbackHub>("/hubs/feedback");

// ---- Startup: create schema (with retry) + seed ----
await InitializeDatabaseAsync(app);

app.Run();
return;

static async Task<bool> SafeCheck(Func<Task<bool>> check)
{
    try { return await check(); } catch { return false; }
}
static async Task<int> SafeCount(Func<Task<int>> count)
{
    try { return await count(); } catch { return -1; }
}

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    for (var attempt = 1; attempt <= 15; attempt++)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Database schema ready.");
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Database not ready (attempt {Attempt}/15): {Message}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (attempt == 15) throw;
        }
    }

    // Idempotent schema patches — EnsureCreated does not ALTER existing tables.
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE feedback ADD COLUMN IF NOT EXISTS author_name varchar(120)");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Schema patch (author_name) skipped.");
    }

    if (app.Configuration.GetValue("DemoSeed:Enabled", false))
    {
        try { await DemoSeeder.SeedAsync(db, logger); }
        catch (Exception ex) { logger.LogWarning(ex, "Demo seed skipped."); }
    }
}
