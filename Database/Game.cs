using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;
using NodaTime;
using NodaTime.Text;

namespace CurlingCalendar
{
    public static partial class Database
    {
        [DebuggerDisplay("Game {Sheet}|{Time} {Teams[0]} vs. {Teams[1]}")]
        public class Game
        {
            private static ZonedDateTimePattern g_timePattern = ZonedDateTimePattern.ExtendedFormatOnlyIso.WithZoneProvider(DateTimeZoneProviders.Tzdb);
            public int LeagueId { get; }
            public ZonedDateTime Time { get; }
            public string Sheet { get; }
            public Team[] Teams { get; }
            public Game(int leagueId, ZonedDateTime time, string sheet, long teamA, long teamB)
            {
                LeagueId = leagueId;
                Time = time;
                Sheet = sheet;
                Teams = new[]
                {
                    Team.Get(teamA)!,
                    Team.Get(teamB)!,
                };
            }

            public static Game FromReader(SqliteDataReader reader)
            {
                var leagueId = reader.GetInt32("league_id");
                var time = g_timePattern.Parse(reader.GetString("time")).GetValueOrThrow();
                var sheet = reader.GetString("sheet");
                var teamA = reader.GetInt64("team_a");
                var teamB = reader.GetInt64("team_b");
                return new Game(leagueId, time, sheet, teamA, teamB);
            }

            public static void Create(int leagueId, ZonedDateTime time, string sheet, long teamA, long teamB)
                => ExecuteNonQuery(
                    "INSERT INTO games (league_id, time, sheet, team_a, team_b) VALUES (@leagueId, @time, @sheet, @teamA, @teamB)",
                    ("leagueId", leagueId),
                    ("time", time.ToString(g_timePattern.PatternText, null)),
                    ("sheet", sheet),
                    ("teamA", teamA),
                    ("teamB", teamB));

            public static Game? Get(int leagueId, ZonedDateTime time, string sheet)
                => ExecuteGet(
                    "SELECT * FROM games WHERE league_id=@leagueId AND time=@time AND sheet=@sheet",
                    FromReader,
                    ("leagueId", leagueId),
                    ("time", time.ToString(g_timePattern.PatternText, null)),
                    ("sheet", sheet));

            public static void Delete(int leagueId, ZonedDateTime time, string sheet)
                => ExecuteNonQuery(
                    "DELETE FROM games WHERE league_id=@leagueId AND time=@time AND sheet=@sheet",
                    ("leagueId", leagueId),
                    ("time", time.ToString(g_timePattern.PatternText, null)),
                    ("sheet", sheet));

            public static IEnumerable<Game> GetAll(int leagueId)
                => ExecuteReader(
                    "SELECT * FROM games WHERE league_id=@leagueId",
                    FromReader,
                    ("leagueId", leagueId));

            public static IEnumerable<Game> GetByUserId(int userId)
            {
                var teamMembers = ExecuteReader("SELECT * FROM team_members WHERE user_id=@id", TeamMember.FromReader, ("id", userId));
                foreach (var teamMember in teamMembers)
                {
                    var team = teamMember.Team;

                    var games = ExecuteReader(
                        "SELECT * FROM games WHERE league_id=@leagueId AND (team_a=@teamId OR team_b=@teamId)",
                        FromReader,
                        ("leagueId", team.LeagueId),
                        ("teamId", team.Id))
                        .ToArray();

                    foreach (var game in games)
                        yield return game;
                }
            }
        }
    }
}
