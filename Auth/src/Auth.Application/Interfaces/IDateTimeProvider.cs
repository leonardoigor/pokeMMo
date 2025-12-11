namespace Auth.Application.Interfaces;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
