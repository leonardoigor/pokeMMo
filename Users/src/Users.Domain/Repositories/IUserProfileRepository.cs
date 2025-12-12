using Users.Domain.Entities;

namespace Users.Domain.Repositories;

public interface IUserProfileRepository
{
    Task<UserProfile?> GetByExternalIdAsync(Guid externalId, CancellationToken ct);
    Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(UserProfile profile, CancellationToken ct);
}
