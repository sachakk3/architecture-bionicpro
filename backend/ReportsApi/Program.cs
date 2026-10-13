using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using ReportsApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ClickHouseOptions>().BindConfiguration(ClickHouseOptions.Section).ValidateOnStart();
builder.Services.AddOptions<ReportOptions>().BindConfiguration(ReportOptions.Section);
builder.Services.AddHttpClient<ClickHouseReportStore>(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var keycloak = builder.Configuration.GetRequiredSection(KeycloakOptions.Section).Get<KeycloakOptions>()!;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MetadataAddress = keycloak.MetadataAddress;
        o.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new()
        {
            ValidIssuer = keycloak.Issuer,
            // keycloak-js gets tokens with aud=account; the client is checked via azp in OnTokenValidated
            ValidateAudience = false,
            NameClaimType = KeycloakClaims.Username,
            RoleClaimType = ClaimTypes.Role,
        };
        o.Events = new() { OnTokenValidated = ctx => KeycloakClaims.OnTokenValidated(ctx, keycloak) };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("reports", p => p.RequireRole("prothetic_user", "administrator"));

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .WithHeaders("Authorization")
    .WithMethods("GET")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

// Only the caller's own report: the username comes solely from the validated token,
// so there is no way to request another user's data
app.MapGet("/reports", (ClaimsPrincipal user, DateOnly? from, DateOnly? to,
        ClickHouseReportStore store, IOptions<ReportOptions> options, CancellationToken ct) =>
    GetReport(user.Identity!.Name!, from, to, store, options.Value, ct))
    .RequireAuthorization("reports");

app.Run();

static async Task<IResult> GetReport(string username, DateOnly? from, DateOnly? to,
    ClickHouseReportStore store, ReportOptions options, CancellationToken ct)
{
    // Default: the last N full days, since today's mart data isn't built yet
    var periodTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    var periodFrom = from ?? periodTo.AddDays(1 - options.DefaultPeriodDays);

    if (periodFrom > periodTo)
    {
        return TypedResults.Problem("Параметр from должен быть не позже to", statusCode: 400);
    }
    if (periodTo.DayNumber - periodFrom.DayNumber + 1 > options.MaxPeriodDays)
    {
        return TypedResults.Problem($"Период не может быть длиннее {options.MaxPeriodDays} дней", statusCode: 400);
    }

    try
    {
        var days = await store.GetDailyUsageAsync(username, periodFrom, periodTo, ct);
        return TypedResults.Ok(UsageReport.Create(username, periodFrom, periodTo, days));
    }
    catch (Exception e) when (e is ClickHouseException or HttpRequestException or TaskCanceledException
                               && !ct.IsCancellationRequested)
    {
        return TypedResults.Problem("Хранилище отчётов временно недоступно", statusCode: 503);
    }
}
