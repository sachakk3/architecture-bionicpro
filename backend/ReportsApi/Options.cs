namespace ReportsApi;

public sealed class KeycloakOptions
{
    public const string Section = "Keycloak";

    /// <summary>The token's iss value: the realm's public address as seen by the browser.</summary>
    public required string Issuer { get; init; }

    /// <summary>Where the service fetches OIDC metadata and keys from (may be an internal address).</summary>
    public required string MetadataAddress { get; init; }

    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>Keycloak clients (azp claim) whose tokens are allowed to call the API.</summary>
    public string[] AllowedClients { get; init; } = [];
}

public sealed class ClickHouseOptions
{
    public const string Section = "ClickHouse";

    public required Uri Url { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
}

public sealed class ReportOptions
{
    public const string Section = "Reports";

    public int DefaultPeriodDays { get; init; } = 30;
    public int MaxPeriodDays { get; init; } = 366;
}
