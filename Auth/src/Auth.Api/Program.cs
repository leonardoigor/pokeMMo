using System.Text;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Infrastructure;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Observability.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, "auth");

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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Auth API", Version = "v1" });
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    var securityRequirement = new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    };
    c.AddSecurityRequirement(securityRequirement);
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRequestResponseLogging();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth API v1");
    c.RoutePrefix = "swagger";
});

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
