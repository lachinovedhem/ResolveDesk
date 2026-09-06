using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using ResolveDesk.Application;
using ResolveDesk.Core;
using ResolveDesk.Infrastructure;
using ResolveDesk.Infrastructure.Auth;
using ResolveDesk.WebApi;
using Serilog;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext().WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

var auth = AuthOptions.Read(builder.Configuration);
// Built here, not inside AddAuth, so the validation parameters below can trust the very same key
// object. Constructing it eagerly also means a deployment missing its signing key fails at startup.
var tokenIssuer = auth.Enabled ? new JwtTokenIssuer(auth, builder.Environment.IsDevelopment()) : null;

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuth(auth, tokenIssuer);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
builder.Services.AddOpenApi();

builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .AllowAnyHeader().AllowAnyMethod()
    .WithOrigins((builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? "http://localhost:5173").Split(','))));

// Login is the one endpoint worth guessing at, so it gets its own budget per client address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

if (auth.Enabled)
{
    // JWT bearer is the one authentication handler that supports Native AOT, which is why the OIDC
    // option validates an external token here rather than running the interactive flow server-side —
    // the SPA performs the flow and sends the resulting token.
    var jwt = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

    // Two shapes, and they disagree about whose token this API trusts.
    //
    //   Interactive OIDC — the API runs the code flow itself and issues its own token, so it
    //   validates its own signing key. This is what lets SSO sit beside a password and a passkey:
    //   whichever method someone used, the session that comes out is identical.
    //
    //   Bearer passthrough — no client id configured, so the SPA gets tokens from the provider and
    //   this API validates the provider's. Fewer moving parts where an identity layer already exists.
    //
    // Mutually exclusive by construction; the interactive flow wins when both look configured.
    var passthrough = auth.Provider is AuthProvider.Oidc && !auth.Oidc.IsInteractive;
    if (passthrough)
    {
        jwt.AddJwtBearer(o =>
        {
            o.Authority = auth.Oidc.Authority;
            o.Audience = auth.Oidc.Audience;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                RoleClaimType = auth.Oidc.RoleClaim,
                NameClaimType = "name",
            };
        });
    }
    else
    {
        jwt.AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = auth.Jwt.Issuer,
                ValidAudience = auth.Jwt.Audience,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = "name",
                IssuerSigningKey = tokenIssuer!.SigningKey,
            };
        });
    }

    builder.Services.AddAuthorization(o =>
    {
        o.AddPolicy("coordinator", p => p.RequireRole(nameof(UserRole.Coordinator), nameof(UserRole.Admin)));
        o.AddPolicy("admin", p => p.RequireRole(nameof(UserRole.Admin)));
    });
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("frontend");
app.UseRateLimiter();

if (auth.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// Dev-only schema (Postgres). Real migrations in prod — standards 02 §5.
try { await DbSchema.EnsureAsync(app.Services); }
catch (Exception ex) { app.Logger.LogWarning(ex, "DB schema ensure skipped — is Postgres reachable?"); }

// The vector index is optional: without pgvector the suggestion engine degrades to keyword search
// rather than failing, so a plain PostgreSQL stays a supported deployment.
var aiOptions = app.Services.GetRequiredService<AiOptions>();
if (aiOptions.EmbeddingEnabled)
{
    var index = app.Services.GetRequiredService<ISemanticIndex>();
    if (await index.EnsureSchemaAsync(aiOptions.Embedding.Dimensions))
        app.Logger.LogInformation("pgvector index ready ({Dimensions}-d, model {Model}).",
            aiOptions.Embedding.Dimensions, aiOptions.Embedding.Model);
    else
        app.Logger.LogWarning("pgvector unavailable — suggestions will use keyword search only.");
}

try { await AuthBootstrap.EnsureAdminAsync(app.Services, app.Logger); }
catch (Exception ex) { app.Logger.LogWarning(ex, "Admin bootstrap skipped."); }

app.MapOpenApi();
app.MapHealthChecks("/health/live", new() { Predicate = h => h.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = h => h.Tags.Contains("ready") });
app.MapAuth();
app.MapAccounts();
app.MapOidc();
if (auth.Enabled && auth.Allows(AuthMethod.Oidc))
{
    app.Logger.LogInformation(
        auth.Oidc.IsInteractive
            ? "OIDC: interactive flow via {Authority}; this API issues its own tokens."
            : "OIDC: bearer passthrough from {Authority}; set Auth:Oidc:ClientId for the interactive flow.",
        auth.Oidc.Authority);
}
app.MapApi(auth.Enabled);
app.MapTriage(auth.Enabled);

app.Run();
