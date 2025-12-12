using Users.Domain.Primitives;

namespace Users.Domain.Entities;

public class UserProfile : Entity
{
    public Guid ExternalId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public List<Character> Characters { get; private set; } = new();

    public UserProfile(Guid externalId, string name)
    {
        ExternalId = externalId;
        Name = name;
    }
}
