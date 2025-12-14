namespace IDP.Application.DTOs;

public record CreateUserRequest(Guid ExternalId, string Name);
public record CreateCharacterRequest(string Name);
public record CharacterResponse(Guid Id, string Name);
public record UserResponse(Guid Id, Guid ExternalId, string Name);
public record CharacterStateRequest(double PosX, double PosY, double PosZ, string World, string ItemsJson, string ClothesJson, string PartyJson, string PcJson);
public record CharacterStateResponse(double PosX, double PosY, double PosZ, string World, string ItemsJson, string ClothesJson, string PartyJson, string PcJson, DateTime UpdatedAt);
public record CharactersListResponse(IEnumerable<CharacterResponse> Items);
