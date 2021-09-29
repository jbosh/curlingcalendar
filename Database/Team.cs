using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace CurlingCalendar
{
    public static partial class Database
    {
        [DebuggerDisplay("Team {Id}: {Name}")]
        public class Team
        {
            public long Id { get; }
            public string Name { get; }
            public int LeagueId { get; }
            public Team(long id, string name, int leagueId)
            {
                Id = id;
                Name = name;
                LeagueId = leagueId;
            }

            public static Team FromReader(SqliteDataReader reader)
            {
                var id = reader.GetInt32("id");
                var name = reader.GetString("name");
                var leagueId = reader.GetInt32("league_id");
                return new Team(id, name, leagueId);
            }

            public static Team Create(string name, int leagueId)
            {
                using var transaction = BeginTransaction();
                var count = ExecuteScalar<long>("SELECT COUNT(*) FROM teams WHERE name=@name AND league_id=@leagueId",
                    ("name", name),
                    ("leagueId", leagueId));
                if (count != 0)
                {
                    transaction.Rollback();
                    throw new DuplicateNameException($"Duplicate team {name} in league {leagueId}.");
                }

                var id = ExecuteScalar<long>(
                    "INSERT INTO teams (name, league_id) VALUES (@name, @leagueId); SELECT last_insert_rowid()",
                    ("name", name),
                    ("leagueId", leagueId));
                transaction.Commit();
                return new Team(id, name, leagueId);
            }

            public static Team? Get(long id)
                => ExecuteGet(
                    "SELECT * FROM teams WHERE id=@id",
                    FromReader,
                    ("id", id));

            public static Team? Get(string name, int leagueId)
                => ExecuteGet(
                    "SELECT * FROM teams WHERE name=@name AND league_id=@leagueId",
                    FromReader,
                    ("name", name),
                    ("leagueId", leagueId));

            public static IEnumerable<Team> GetAll(long leagueId)
                => ExecuteReader(
                    "SELECT * FROM teams WHERE league_id=@leagueId",
                    FromReader,
                    ("leagueId", leagueId));
        }
    }
}
