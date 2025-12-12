using Microsoft.EntityFrameworkCore;
using Users.Domain.Entities;
using Users.Domain.Repositories;

namespace Users.Infrastructure.Persistence;

public class UserProfileRepository : IUserProfileRepository
{
    private readonly UsersDbContext _db;
    public UserProfileRepository(UsersDbContext db) { _db = db; }

    public Task<UserProfile?> GetByExternalIdAsync(Guid externalId, CancellationToken ct)
        => _db.UserProfiles.Include(x => x.Characters).AsNoTracking().FirstOrDefaultAsync(x => x.ExternalId == externalId, ct);

    public Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.UserProfiles.Include(x => x.Characters).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(UserProfile profile, CancellationToken ct)
    {
        await _db.UserProfiles.AddAsync(profile, ct);
        await _db.SaveChangesAsync(ct);
    }
}
