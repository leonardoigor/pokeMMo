using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Security;

public class PasswordHasher : IPasswordHasher
{
    public (string Hash, string Salt) Hash(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToBase64String(saltBytes);
        var hashBytes = HashInternal(password, saltBytes);
        var hash = Convert.ToBase64String(hashBytes);
        return (hash, salt);
    }

    public bool Verify(string password, string hash, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var candidateHash = HashInternal(password, saltBytes);
        var expected = Convert.FromBase64String(hash);
        return CryptographicOperations.FixedTimeEquals(candidateHash, expected);
    }

    private static byte[] HashInternal(string password, byte[] salt)
    {
        var iter = 100_000;
        using var derive = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iter, HashAlgorithmName.SHA256);
        return derive.GetBytes(32);
    }
}
