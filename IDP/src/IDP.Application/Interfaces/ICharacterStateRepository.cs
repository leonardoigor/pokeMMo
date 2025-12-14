namespace IDP.Application.Interfaces;

public interface ICharacterStateRepository
{
    Task EnsureTableAsync(Guid characterId, CancellationToken ct);
    Task UpsertAsync(Guid characterId, double x, double y, double z, string world, string itemsJson, string clothesJson, string partyJson, string pcJson, DateTime now, CancellationToken ct);
    Task<CharacterState?> GetAsync(Guid characterId, CancellationToken ct);
}

public record CharacterState(double PosX, double PosY, double PosZ, string World, string ItemsJson, string ClothesJson, string PartyJson, string PcJson, DateTime UpdatedAt);
