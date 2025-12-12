using Microsoft.OpenApi.Models;
using Users.Application.DTOs;
using Users.Application.Interfaces;
using Users.Domain.Entities;
using Users.Domain.Repositories;
using Users.Infrastructure;
using Users.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Observability.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUsersInfrastructure(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, "users");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Users API", Version = "v1" });
});

var app = builder.Build();
app.UseRequestResponseLogging();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Users API v1");
    c.RoutePrefix = "swagger";
});

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapPost("/users", async (CreateUserRequest req, IUserProfileRepository repo, CancellationToken ct) =>
{
    var existing = await repo.GetByExternalIdAsync(req.ExternalId, ct);
    if (existing is not null) return Results.Conflict("user_exists");
    var profile = new UserProfile(req.ExternalId, req.Name);
    await repo.AddAsync(profile, ct);
    return Results.Ok(new UserResponse(profile.Id, profile.ExternalId, profile.Name));
});

app.MapPost("/users/{userId:guid}/characters", async (Guid userId, CreateCharacterRequest req, ICharacterRepository repo, CancellationToken ct) =>
{
    var character = new Character(userId, req.Name);
    await repo.AddAsync(character, ct);
    return Results.Ok(new CharacterResponse(character.Id, character.Name));
});

app.MapGet("/users/{userId:guid}/characters", async (Guid userId, ICharacterRepository repo, CancellationToken ct) =>
{
    var list = await repo.ListByUserAsync(userId, ct);
    return Results.Ok(list.Select(x => new CharacterResponse(x.Id, x.Name)));
});

app.MapPut("/characters/{characterId:guid}/state", async (Guid characterId, CharacterStateRequest req, ICharacterStateRepository stateRepo, CancellationToken ct) =>
{
    await stateRepo.EnsureTableAsync(characterId, ct);
    await stateRepo.UpsertAsync(characterId, req.PosX, req.PosY, req.PosZ, req.World, req.ItemsJson, req.ClothesJson, req.PartyJson, req.PcJson, DateTime.UtcNow, ct);
    return Results.NoContent();
});

app.MapGet("/characters/{characterId:guid}/state", async (Guid characterId, ICharacterStateRepository stateRepo, CancellationToken ct) =>
{
    await stateRepo.EnsureTableAsync(characterId, ct);
    var state = await stateRepo.GetAsync(characterId, ct);
    if (state is null) return Results.NotFound();
    return Results.Ok(new CharacterStateResponse(state.PosX, state.PosY, state.PosZ, state.World, state.ItemsJson, state.ClothesJson, state.PartyJson, state.PcJson, state.UpdatedAt));
});

app.Run();
