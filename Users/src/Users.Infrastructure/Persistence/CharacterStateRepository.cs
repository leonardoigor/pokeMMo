using Microsoft.EntityFrameworkCore;
using Npgsql;
using Users.Application.Interfaces;

namespace Users.Infrastructure.Persistence;

public class CharacterStateRepository : ICharacterStateRepository
{
    private readonly UsersDbContext _db;
    public CharacterStateRepository(UsersDbContext db) { _db = db; }

    public async Task EnsureTableAsync(Guid characterId, CancellationToken ct)
    {
        var name = TableName(characterId);
        var sql = $@"
CREATE TABLE IF NOT EXISTS {name} (
    id integer PRIMARY KEY,
    pos_x double precision NOT NULL,
    pos_y double precision NOT NULL,
    pos_z double precision NOT NULL,
    world text NOT NULL,
    items jsonb NOT NULL,
    clothes jsonb NOT NULL,
    party jsonb NOT NULL,
    pc jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);";
        await _db.Database.ExecuteSqlRawAsync(sql, ct);
        var init = $@"INSERT INTO {name}(id,pos_x,pos_y,pos_z,world,items,clothes,party,pc,updated_at)
VALUES(1,0,0,0,'', '{{}}'::jsonb, '{{}}'::jsonb, '{{}}'::jsonb, '{{}}'::jsonb, now())
ON CONFLICT (id) DO NOTHING;";
        await _db.Database.ExecuteSqlRawAsync(init, ct);
    }

    public async Task UpsertAsync(Guid characterId, double x, double y, double z, string world, string itemsJson, string clothesJson, string partyJson, string pcJson, DateTime now, CancellationToken ct)
    {
        var name = TableName(characterId);
        var sql = $@"INSERT INTO {name}(id,pos_x,pos_y,pos_z,world,items,clothes,party,pc,updated_at)
VALUES(1,@x,@y,@z,@world,@items::jsonb,@clothes::jsonb,@party::jsonb,@pc::jsonb,@now)
ON CONFLICT (id) DO UPDATE SET pos_x=@x,pos_y=@y,pos_z=@z,world=@world,items=@items::jsonb,clothes=@clothes::jsonb,party=@party::jsonb,pc=@pc::jsonb,updated_at=@now;";
        var cn = _db.Database.GetDbConnection();
        await cn.OpenAsync(ct);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("x", x));
        cmd.Parameters.Add(new NpgsqlParameter("y", y));
        cmd.Parameters.Add(new NpgsqlParameter("z", z));
        cmd.Parameters.Add(new NpgsqlParameter("world", world));
        cmd.Parameters.Add(new NpgsqlParameter("items", itemsJson));
        cmd.Parameters.Add(new NpgsqlParameter("clothes", clothesJson));
        cmd.Parameters.Add(new NpgsqlParameter("party", partyJson));
        cmd.Parameters.Add(new NpgsqlParameter("pc", pcJson));
        cmd.Parameters.Add(new NpgsqlParameter("now", now));
        await cmd.ExecuteNonQueryAsync(ct);
        await cn.CloseAsync();
    }

    public async Task<CharacterState?> GetAsync(Guid characterId, CancellationToken ct)
    {
        var name = TableName(characterId);
        var sql = $@"SELECT pos_x,pos_y,pos_z,world,items::text,clothes::text,party::text,pc::text,updated_at FROM {name} WHERE id=1;";
        var cn = _db.Database.GetDbConnection();
        await cn.OpenAsync(ct);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await cn.CloseAsync();
            return null;
        }
        var state = new CharacterState(
            reader.GetDouble(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetDateTime(8)
        );
        await cn.CloseAsync();
        return state;
    }

    private static string TableName(Guid id) => $"character_state_{id.ToString().Replace("-", string.Empty)}";
}
