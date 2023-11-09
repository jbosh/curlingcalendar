using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace CurlingCalendar
{
    public static partial class Database
    {
        public class TeamMember
        {
            public int UserId { get; }
            public Team Team { get; }

            public TeamMember(int userId, Team team)
            {
                UserId = userId;
                Team = team;
            }

            public static TeamMember FromReader(SqliteDataReader reader)
            {
                var userId = reader.GetInt32("user_id");
                var teamId = reader.GetInt32("team_id");
                var team = Team.Get(teamId)!;
                return new TeamMember(userId, team);
            }

            public static void Create(int userId, Team team)
                => ExecuteNonQuery(
                    "INSERT INTO team_members (user_id, team_id) VALUES (@userId, @teamId)",
                    ("userId", userId),
                    ("teamId", team.Id));

            public static TeamMember? Get(int userId, Team team)
                => ExecuteGet(
                    "SELECT * FROM team_members WHERE user_id=@userId AND team_id=@team",
                    FromReader,
                    ("userId", userId),
                    ("team", team.Id));

            public static IEnumerable<TeamMember> GetAll(Team team)
                => ExecuteReader(
                    "SELECT * FROM team_members WHERE team_id=@teamId",
                    FromReader,
                    ("teamId", team.Id));

            public static IEnumerable<TeamMember> GetAll(long userId)
                => ExecuteReader(
                    "SELECT * FROM team_members WHERE user_id=@userId",
                    FromReader,
                    ("userId", userId));

            public static void Delete(int userId, Team team)
                => ExecuteNonQuery(
                    "DELETE FROM team_members WHERE user_id=@userId AND team_id=@teamId",
                    ("userId", userId),
                    ("teamId", team.Id));

            public static Dictionary<string, Team> GetAllTeamsByMemberInLeague(int leagueId)
            {
                var result = new Dictionary<string, Team>();
                foreach (var team in Team.GetAll(leagueId))
                {
                    foreach (var teamMember in GetAll(team))
                    {
                        var user = User.FromId(teamMember.UserId)!;
                        var fullName = user.FullName!;
                        if (result.ContainsKey(fullName))
                        {
                            if (result[fullName] != team)
                                throw new Exception($"Ambiguous people on different teams. {fullName} may be on {result[fullName]} and {team}.");
                        }
                        else
                        {
                            result.Add(fullName, team);
                        }
                    }
                }

                return result;
            }
        }
    }
}