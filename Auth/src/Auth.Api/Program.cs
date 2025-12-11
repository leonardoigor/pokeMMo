using System.Text;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Infrastructure;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthInfrastructure(builder.Configuration);

var jwtKey = builder.Configuration["JWT:Key"] ?? "change-this-key";
var issuer = builder.Configuration["JWT:Issuer"] ?? "creature-realms";
var audience = builder.Configuration["JWT:Audience"] ?? "creature-realms-client";
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(o =>
{
    o.RequireHttpsMetadata = false;
    o.SaveToken = true;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = key
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapPost("/auth/register", async (RegisterRequest req, IAuthService auth, CancellationToken ct) =>
{
    var tokens = await auth.RegisterAsync(req, ct);
    return Results.Ok(tokens);
});

app.MapPost("/auth/login", async (LoginRequest req, IAuthService auth, CancellationToken ct) =>
{
    var tokens = await auth.LoginAsync(req, ct);
    return Results.Ok(tokens);
});

app.MapPost("/auth/refresh", async (RefreshRequest req, IAuthService auth, CancellationToken ct) =>
{
    var tokens = await auth.RefreshAsync(req, ct);
    return Results.Ok(tokens);
});

app.MapPost("/auth/revoke", async (RevokeRequest req, IAuthService auth, CancellationToken ct) =>
{
    var ok = await auth.RevokeAsync(req, ct);
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.Run();
