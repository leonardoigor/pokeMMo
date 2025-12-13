using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Users.Application.Interfaces;
using Users.Domain.Repositories;
using Users.Infrastructure.Persistence;

namespace Users.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddUsersInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Default") ?? "Host=localhost;Port=5432;Database=creature_realms;Username=cro_admin;Password=postgres";
        services.AddDbContext<UsersDbContext>(o =>
            o.UseNpgsql(cs, b =>
            {
                b.MigrationsAssembly(typeof(UsersDbContext).Assembly.GetName().Name);
                b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null);
            }));
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<ICharacterStateRepository, CharacterStateRepository>();
        return services;
    }
}
