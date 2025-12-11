namespace Auth.Infrastructure.Security;

public class JwtOptions
{
    public string Issuer { get; set; } = "creature-realms";
    public string Audience { get; set; } = "creature-realms-client";
    public string Key { get; set; } = "change-this-key";
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}
