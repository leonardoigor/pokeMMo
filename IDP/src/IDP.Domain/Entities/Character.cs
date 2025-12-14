using IDP.Domain.Primitives;

namespace IDP.Domain.Entities;

public class Character : Entity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public Character(Guid userId, string name)
    {
        UserId = userId;
        Name = name;
    }
}
