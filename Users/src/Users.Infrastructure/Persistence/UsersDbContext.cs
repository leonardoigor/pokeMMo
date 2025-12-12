using Microsoft.EntityFrameworkCore;
using Users.Domain.Entities;

namespace Users.Infrastructure.Persistence;

public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Character> Characters => Set<Character>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(b =>
        {
            b.ToTable("user_profiles");
            b.HasKey(x => x.Id);
            b.Property(x => x.ExternalId).IsRequired();
            b.Property(x => x.Name).IsRequired().HasMaxLength(128);
            b.Property(x => x.CreatedAt).IsRequired();
            b.HasIndex(x => x.ExternalId).IsUnique();
            b.HasMany(x => x.Characters).WithOne().HasForeignKey(c => c.UserId);
        });

        modelBuilder.Entity<Character>(b =>
        {
            b.ToTable("characters");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserId).IsRequired();
            b.Property(x => x.Name).IsRequired().HasMaxLength(64);
            b.Property(x => x.CreatedAt).IsRequired();
            b.HasIndex(x => x.UserId);
        });
    }
}
