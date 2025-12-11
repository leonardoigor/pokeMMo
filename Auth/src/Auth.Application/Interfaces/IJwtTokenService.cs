using Auth.Domain.Entities;

namespace Auth.Application.Interfaces;

public interface IJwtTokenService
{
    string CreateAccessToken(User user, DateTime now);
    (string Raw, string Hash, DateTime ExpiresAt) CreateRefreshToken(DateTime now);
    bool ValidateAccessToken(string token);
}
