using Users.Domain.Entities;

namespace Users.Domain.Repositories;

public interface ICharacterRepository
{
    Task<Character?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Character>> ListByUserAsync(Guid userId, CancellationToken ct);
    Task AddAsync(Character character, CancellationToken ct);
    Task DeleteAsync(Character character, CancellationToken ct);
    Task<int> CountByUserAsync(Guid userId, CancellationToken ct);
}
