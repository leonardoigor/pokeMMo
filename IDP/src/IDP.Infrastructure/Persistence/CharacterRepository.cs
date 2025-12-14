using Microsoft.EntityFrameworkCore;
using IDP.Domain.Entities;
using IDP.Domain.Repositories;

namespace IDP.Infrastructure.Persistence;

public class CharacterRepository : ICharacterRepository
{
    private readonly UsersDbContext _db;
    public CharacterRepository(UsersDbContext db) { _db = db; }

    public Task<Character?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Characters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<Character>> ListByUserAsync(Guid userId, CancellationToken ct)
        => _db.Characters.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(Character character, CancellationToken ct)
    {
        await _db.Characters.AddAsync(character, ct);
        await _db.SaveChangesAsync(ct);
    }
    
    public async Task DeleteAsync(Character character, CancellationToken ct)
    {
        _db.Characters.Remove(character);
        await _db.SaveChangesAsync(ct);
    }
    
    public Task<int> CountByUserAsync(Guid userId, CancellationToken ct)
        => _db.Characters.Where(x => x.UserId == userId).CountAsync(ct);
}
