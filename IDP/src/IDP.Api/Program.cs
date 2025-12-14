using Microsoft.OpenApi.Models;
using IDP.Application.DTOs;
using IDP.Application.Interfaces;
using IDP.Domain.Entities;
using IDP.Domain.Repositories;
using IDP.Infrastructure;
using IDP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Observability.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIDPInfrastructure(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, "idp");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "IDP API", Version = "v1" });
});

var jwtIssuer = builder.Configuration["JWT:Issuer"] ?? "creature-realms";
var jwtAudience = builder.Configuration["JWT:Audience"] ?? "creature-realms-client";
var jwtKey = builder.Configuration["JWT:Key"] ?? "change-this-key";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseRequestResponseLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "IDP API v1");
    c.RoutePrefix = "swagger";
});

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapPost("/idp/users", async (CreateUserRequest req, IUserProfileRepository repo, CancellationToken ct) =>
{
    var existing = await repo.GetByExternalIdAsync(req.ExternalId, ct);
    if (existing is not null) return Results.Conflict("user_exists");
    var profile = new UserProfile(req.ExternalId, req.Name);
    await repo.AddAsync(profile, ct);
    return Results.Ok(new UserResponse(profile.Id, profile.ExternalId, profile.Name));
});

app.MapPost("/idp/users/{userId:guid}/characters", async (Guid userId, CreateCharacterRequest req, ICharacterRepository repo, CancellationToken ct) =>
{
    var count = await repo.CountByUserAsync(userId, ct);
    if (count >= 11) return Results.BadRequest("characters_limit_reached");
    var character = new Character(userId, req.Name);
    await repo.AddAsync(character, ct);
    return Results.Ok(new CharacterResponse(character.Id, character.Name));
});

app.MapGet("/idp/users/{userId:guid}/characters", async (Guid userId, ICharacterRepository repo, CancellationToken ct) =>
{
    var list = await repo.ListByUserAsync(userId, ct);
    return Results.Ok(list.Select(x => new CharacterResponse(x.Id, x.Name)));
});

app.MapGet("/idp/users/me/characters", async (ClaimsPrincipal user, IUserProfileRepository userRepo, ICharacterRepository charRepo, CancellationToken ct) =>
{
    var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(sub)) return Results.Unauthorized();
    if (!Guid.TryParse(sub, out var externalId)) return Results.Unauthorized();
    var profile = await userRepo.GetByExternalIdAsync(externalId, ct);
    if (profile is null)
    {
        var name = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value ?? "Player";
        var created = new UserProfile(externalId, name);
        await userRepo.AddAsync(created, ct);
        profile = created;
    }
    var chars = await charRepo.ListByUserAsync(profile.Id, ct);
    var payload = new CharactersListResponse(chars.Select(x => new CharacterResponse(x.Id, x.Name)));
    return Results.Ok(payload);
}).RequireAuthorization();
app.MapPost("/idp/users/me/characters", async (ClaimsPrincipal user, CreateCharacterRequest req, IUserProfileRepository userRepo, ICharacterRepository charRepo, CancellationToken ct) =>
{
    var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(sub)) return Results.Unauthorized();
    if (!Guid.TryParse(sub, out var externalId)) return Results.Unauthorized();
    var profile = await userRepo.GetByExternalIdAsync(externalId, ct);
    if (profile is null)
    {
        var name = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value ?? "Player";
        var created = new UserProfile(externalId, name);
        await userRepo.AddAsync(created, ct);
        profile = created;
    }
    var limit = await charRepo.CountByUserAsync(profile.Id, ct);
    if (limit >= 11) return Results.BadRequest("characters_limit_reached");
    var character = new Character(profile.Id, req.Name);
    await charRepo.AddAsync(character, ct);
    return Results.Ok(new CharacterResponse(character.Id, character.Name));
}).RequireAuthorization();
app.MapDelete("/idp/characters/{characterId:guid}", async (Guid characterId, ClaimsPrincipal user, IUserProfileRepository userRepo, ICharacterRepository charRepo, CancellationToken ct) =>
{
    var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(sub)) return Results.Unauthorized();
    if (!Guid.TryParse(sub, out var externalId)) return Results.Unauthorized();
    var profile = await userRepo.GetByExternalIdAsync(externalId, ct);
    if (profile is null) return Results.NotFound();
    var character = await charRepo.GetByIdAsync(characterId, ct);
    if (character is null) return Results.NotFound();
    if (character.UserId != profile.Id) return Results.Forbid();
    await charRepo.DeleteAsync(character, ct);
    return Results.NoContent();
}).RequireAuthorization();
app.MapDelete("/idp/users/characters/{characterId:guid}", async (Guid characterId, ClaimsPrincipal user, IUserProfileRepository userRepo, ICharacterRepository charRepo, CancellationToken ct) =>
{
    var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(sub)) return Results.Unauthorized();
    if (!Guid.TryParse(sub, out var externalId)) return Results.Unauthorized();
    var profile = await userRepo.GetByExternalIdAsync(externalId, ct);
    if (profile is null) return Results.NotFound();
    var character = await charRepo.GetByIdAsync(characterId, ct);
    if (character is null) return Results.NotFound();
    if (character.UserId != profile.Id) return Results.Forbid();
    await charRepo.DeleteAsync(character, ct);
    return Results.NoContent();
}).RequireAuthorization();
app.MapPut("/idp/characters/{characterId:guid}/state", async (Guid characterId, CharacterStateRequest req, ICharacterStateRepository stateRepo, CancellationToken ct) =>
{
    await stateRepo.EnsureTableAsync(characterId, ct);
    await stateRepo.UpsertAsync(characterId, req.PosX, req.PosY, req.PosZ, req.World, req.ItemsJson, req.ClothesJson, req.PartyJson, req.PcJson, DateTime.UtcNow, ct);
    return Results.NoContent();
});

app.MapGet("/idp/characters/{characterId:guid}/state", async (Guid characterId, ICharacterStateRepository stateRepo, CancellationToken ct) =>
{
    await stateRepo.EnsureTableAsync(characterId, ct);
    var state = await stateRepo.GetAsync(characterId, ct);
    if (state is null) return Results.NotFound();
    return Results.Ok(new CharacterStateResponse(state.PosX, state.PosY, state.PosZ, state.World, state.ItemsJson, state.ClothesJson, state.PartyJson, state.PcJson, state.UpdatedAt));
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
    app.Logger.LogInformation("Applying EF Core migrations for UsersDbContext...");
    try
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
        db.Database.Migrate();
        var applied = db.Database.GetAppliedMigrations().ToArray();
        var pending = db.Database.GetPendingMigrations().ToArray();
        app.Logger.LogInformation("EF Core migrations applied successfully. applied={applied} pending={pending}",
            string.Join(",", applied), string.Join(",", pending));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to apply EF Core migrations");
        throw;
    }
}

app.MapGet("/debug/migrations", (UsersDbContext db) =>
{
    var applied = db.Database.GetAppliedMigrations();
    var pending = db.Database.GetPendingMigrations();
    return Results.Ok(new
    {
        applied = applied,
        pending = pending
    });
});

app.Run();
