using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Repositories;

namespace Auth.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenService _jwt;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenService jwt,
        IPasswordHasher hasher,
        IDateTimeProvider clock)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _jwt = jwt;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<TokenResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var existing = await _users.GetByEmailAsync(request.Email, ct);
        if (existing is not null) throw new InvalidOperationException("email_in_use");
        var hp = _hasher.Hash(request.Password);
        var user = new User(request.Email, hp.Hash, hp.Salt);
        await _users.AddAsync(user, ct);
        var now = _clock.UtcNow;
        var access = _jwt.CreateAccessToken(user, now);
        var refresh = _jwt.CreateRefreshToken(now);
        var rt = new RefreshToken(user.Id, refresh.Hash, refresh.ExpiresAt);
        await _refreshTokens.AddAsync(rt, ct);
        return new TokenResponse(access, refresh.Raw);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is null) throw new InvalidOperationException("invalid_credentials");
        var ok = _hasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt);
        if (!ok) throw new InvalidOperationException("invalid_credentials");
        var now = _clock.UtcNow;
        var access = _jwt.CreateAccessToken(user, now);
        var refresh = _jwt.CreateRefreshToken(now);
        var rt = new RefreshToken(user.Id, refresh.Hash, refresh.ExpiresAt);
        await _refreshTokens.AddAsync(rt, ct);
        return new TokenResponse(access, refresh.Raw);
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var providedHash = HashString(request.RefreshToken);
        var existing = await _refreshTokens.GetByHashAsync(providedHash, ct);
        if (existing is null) throw new InvalidOperationException("invalid_refresh_token");
        if (existing.RevokedAt is not null) throw new InvalidOperationException("revoked_refresh_token");
        if (existing.ExpiresAt <= now) throw new InvalidOperationException("expired_refresh_token");
        var user = await _users.GetByIdAsync(existing.UserId, ct) ?? throw new InvalidOperationException("user_not_found");
        existing.Revoke(now);
        await _refreshTokens.UpdateAsync(existing, ct);
        var access = _jwt.CreateAccessToken(user, now);
        var refresh = _jwt.CreateRefreshToken(now);
        var rt = new RefreshToken(user.Id, refresh.Hash, refresh.ExpiresAt);
        await _refreshTokens.AddAsync(rt, ct);
        return new TokenResponse(access, refresh.Raw);
    }

    public async Task<bool> RevokeAsync(RevokeRequest request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var providedHash = HashString(request.RefreshToken);
        var existing = await _refreshTokens.GetByHashAsync(providedHash, ct);
        if (existing is null) return false;
        existing.Revoke(now);
        await _refreshTokens.UpdateAsync(existing, ct);
        return true;
    }

    private static string HashString(string raw)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
