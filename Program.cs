using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using NodaTime;

namespace CurlingCalendar
{
    class Program
    {
        private static CookieContainer m_cookieContainer = null!;
        private static HttpClientHandler m_httpClientHandler = null!;
        private static HttpClient m_client = null!;
        private static Uri BaseUri = new Uri("https://denvercurlingclub.com");
        private static DateTimeZone Timezone = DateTimeZone.ForOffset(Offset.FromHours(-6)); // Colorado timezone

        [STAThread]
        public static async Task Main()
        {
            var config = (await File.ReadAllLinesAsync("config.txt"))
                .Select(l => l.Split(new[] { '=' }, 2))
                .ToDictionary(s => s[0], s => s[1]);
            Database.Initialize("db.sqlite");

            m_cookieContainer = new CookieContainer();
            m_httpClientHandler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.All,
                CookieContainer = m_cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = false,
            };

            m_client = new HttpClient(m_httpClientHandler);
            m_client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.90 Safari/537.36");
            m_client.BaseAddress = BaseUri;

            var updateData = true;
            {
                var lastUpdateString = Database.Meta.Get("last_update");
                if (lastUpdateString != null)
                {
                    var lastUpdate = DateTimeOffset.Parse(lastUpdateString);
                    var span = DateTimeOffset.UtcNow - lastUpdate;
                    if (span < TimeSpan.FromHours(1))
                    {
                        updateData = false;
                    }
                    else
                    {
                        Console.Write($"Last update was {lastUpdate.ToLocalTime()} ({span} ago). Would you like to update [yN]? ");
                        var line = Console.ReadLine()!;
                        if (line.Length == 0)
                            updateData = false;
                        else if (line[0] == 'n' || line[0] == 'N')
                            updateData = false;
                    }
                }
            }

            if (updateData)
            {
                var username = config["username"];
                var password = config["password"];
                await Login(username, password);

                await UpdateLeagueInformation();
                Database.Meta.Set("last_update", DateTimeOffset.UtcNow.ToString("u"));
            }

            while (true)
            {
                Console.Write("Get games for player (ID or name): ");
                var line = Console.ReadLine()!;
                if (string.Equals(line, "exit", StringComparison.InvariantCultureIgnoreCase))
                {
                    break;
                }

                Database.User? user;
                if (int.TryParse(line, out var userId))
                {
                    user = Database.User.FromId(userId);
                    if (user == null)
                    {
                        Console.WriteLine("Could not find user.");
                        continue;
                    }
                }
                else
                {
                    var users = Database.User.FromName(line).ToArray();
                    if (users.Length == 0)
                    {
                        Console.WriteLine("Invalid user ID or could not find user.");
                        continue;
                    }

                    if (users.Length == 1)
                    {
                        user = users[0];
                    }
                    else
                    {
                        while (true)
                        {
                            for (var i = 0; i < users.Length; i++)
                            {
                                var u = users[i];
                                Console.WriteLine($"{i + 1}: {u.FullName}");
                            }

                            Console.Write("Multiple users were found. Which one would you like to use? [#] ");
                            line = Console.ReadLine();
                            if (!int.TryParse(line, out var index) || index <= 0 || index > users.Length)
                            {
                                Console.WriteLine($"Invalid number.");
                                continue;
                            }

                            user = users[index - 1];
                            break;
                        }
                    }
                }

                var calendar = new Calendar();
                var localTimeZone = TimeZoneInfo.Local;
                calendar.AddTimeZone(localTimeZone);

                var userTeams = Database.TeamMember
                    .GetAll(user.Id)
                    .Select(t => t.Team.Id)
                    .ToHashSet();
                foreach (var game in Database.Game.GetByUserId(user.Id))
                {
                    var calendarEvent = new CalendarEvent();
                    var startTime = game.Time.ToDateTimeOffset();
                    calendarEvent.Start = new CalDateTime(startTime.DateTime, localTimeZone.Id);
                    calendarEvent.End = calendarEvent.Start.AddHours(2);
                    calendarEvent.Uid = $"UID:{game.LeagueId}-{game.Time.LocalDateTime.ToDateTimeUnspecified()}-{game.Sheet}";
                    var opposingTeam = userTeams.Contains(game.Teams[0].Id)
                        ? game.Teams[1]
                        : game.Teams[0];
                    var summary = $"{game.Sheet}: {opposingTeam.Name}";
                    calendarEvent.Summary = summary;
                    calendarEvent.Location = "14100 W 7th Ave, Golden, CO 80401";

                    calendar.Events.Add(calendarEvent);
                }

                var outPath = $"{user.FullName}.ical";
                var serializer = new CalendarSerializer(calendar);
                await File.WriteAllTextAsync(outPath, serializer.SerializeToString());
            }

            Database.Dispose();
        }

        private static async Task Login(string username, string password)
        {
            Console.WriteLine("Logging in...");

            var formData = new Dictionary<string, string>();

            using (var request = new HttpRequestMessage(HttpMethod.Get, "/index.php/member-login"))
            {
                request.Headers.Add("Keep-Alive", "true");
                using var result = await m_client.SendAsync(request);
                var body = await result.Content.ReadAsStringAsync();

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(body);

                var doc = htmlDoc.DocumentNode;
                var form = doc.SelectNodes("//form[@action=\"https://denvercurlingclub.com/index.php/member-login\"]").First();

                var inputs = form.SelectNodes("//input").ToArray();
                foreach (var input in inputs)
                {
                    var name = input.GetAttributeValue("name", null);
                    var value = input.GetAttributeValue("value", null);
                    if (formData.ContainsKey(name))
                    {
                        formData[name] = value;
                    }
                    else
                    {
                        formData.Add(name, value);
                    }
                }
            }

            {
                formData["username"] = username;
                formData["passwd"] = password;

                using var request = new HttpRequestMessage(HttpMethod.Post, "/index.php/member-login");
                var content = new FormUrlEncodedContent(formData);

                request.Headers.Referrer = new Uri("https://denvercurlingclub.com/index.php/");
                request.Headers.TryAddWithoutValidation("Origin", "https://denvercurlingclub.com");

                request.Content = content;
                using var result = await m_client.SendAsync(request);
                if (result.StatusCode != HttpStatusCode.SeeOther)
                {
                    throw new Exception("Invalid credentials to login.");
                }
            }
        }

        private static async Task UpdateLeagueInformation()
        {
            Console.WriteLine("Getting league information...");
            var leagues = new List<Database.League>();
            var teamTasks = new List<Task<HtmlNode[]>>();
            var scheduleTasks = new List<Task<HtmlNode[]>>();
            {
                var infoTables = FetchInfoTable("/index.php/league-information/teams-schedules-standings").Result;

                var rows = infoTables.First().QuerySelectorAll("> tr");
                foreach (var row in rows)
                {
                    var columns = row.QuerySelectorAll("> td");
                    var leagueName = HttpUtility.HtmlDecode(columns[0].InnerText).Trim();
                    var teamsLink = HttpUtility.HtmlDecode(columns[2].FirstChild.GetAttributeValue("href", null));
                    var scheduleLink = HttpUtility.HtmlDecode(columns[3].FirstChild.GetAttributeValue("href", null));
                    var url = new Uri(BaseUri, teamsLink);
                    var leagueId = int.Parse(HttpUtility.ParseQueryString(url.Query).Get("id"));

                    var league = Database.League.Get(leagueId);
                    if (league == null)
                        league = Database.League.Create(leagueId, leagueName);
                    leagues.Add(league);
                    teamTasks.Add(FetchInfoTable($"/index.php/league-information/teams-schedules-standings?view=tss&layout=leagues_teams&id={leagueId}"));
                    scheduleTasks.Add(FetchInfoTable($"/index.php/league-information/teams-schedules-standings?view=tss&layout=leagues_schedule&id={leagueId}"));
                }
            }

            for (var leagueIndex = 0; leagueIndex < teamTasks.Count; leagueIndex++)
            {
                var league = leagues[leagueIndex];
                Console.WriteLine($"Getting {league.Name}.");
                {
                    var infoTables = await teamTasks[leagueIndex];
                    var rows = infoTables
                        .First()
                        .QuerySelectorAll("> tbody > tr");

                    foreach (var row in rows)
                    {
                        var columns = row.QuerySelectorAll("> td");
                        var teamName = columns[0].InnerText.Trim();

                        var team = default(Database.Team);
                        if (teamName.Length != 0)
                        {
                            team = Database.Team.Get(teamName, league.Id);
                            if (team == null)
                            {
                                team = Database.Team.Create(teamName, league.Id);
                            }
                        }

                        var teamMembers = new HashSet<int>();
                        for (var columnIndex = 1; columnIndex < columns.Count - 1; columnIndex++)
                        {
                            var linkNode = columns[columnIndex].QuerySelector("a");
                            if (linkNode == null)
                                continue; // 5th probably

                            var memberLink = HttpUtility.HtmlDecode(linkNode.GetAttributeValue("href", null));
                            var url = new Uri(BaseUri, memberLink);
                            var memberId = int.Parse(HttpUtility.ParseQueryString(url.Query).Get("searchid"));
                            var memberName = linkNode.InnerText.Trim();
                            if (memberName.Length == 0)
                                continue;

                            if (team == null)
                            {
                                teamName = memberName;
                                team = Database.Team.Get(teamName, league.Id);
                                if (team == null)
                                {
                                    team = Database.Team.Create(teamName, league.Id);
                                }
                            }

                            // Create or update the user if their name has changed.
                            var member = Database.User.FromId(memberId);
                            if (member == null)
                            {
                                member = new Database.User(memberId, memberName);
                                Database.User.Create(member);
                            }
                            else if (member.FullName != memberName)
                            {
                                member.FullName = memberName;
                                Database.User.Update(member);
                            }

                            // Add team members to database.
                            teamMembers.Add(memberId);
                            if (Database.TeamMember.Get(memberId, team) == null)
                            {
                                Database.TeamMember.Create(memberId, team);
                            }
                        }

                        // Delete any members of the team that may have left.
                        if (team != null)
                        {
                            var existingTeamMembers = Database.TeamMember.GetAll(team).ToArray();
                            foreach (var teamMember in existingTeamMembers)
                            {
                                if (!teamMembers.Contains(teamMember.UserId))
                                {
                                    Database.TeamMember.Delete(teamMember.UserId, team);
                                }
                            }
                        }
                    }
                }

                {
                    var infoTables = await scheduleTasks[leagueIndex];
                    if (infoTables.Length < 2)
                    {
                        Console.WriteLine("> No schedule information.");
                        continue;
                    }

                    var rows = infoTables
                        .Skip(1)
                        .First()
                        .QuerySelectorAll("> tbody > tr")
                        .ToArray();

                    var teamsByMember = Database.TeamMember.GetAllTeamsByMemberInLeague(league.Id);

                    var deletedGames = Database.Game
                        .GetAll(league.Id)
                        .Select(g => (g.LeagueId, g.Time, g.Sheet))
                        .ToHashSet();
                    var localDate = LocalDate.MinIsoValue;
                    for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                    {
                        var row = rows[rowIndex];
                        if (row.HasClass("row0") || row.HasClass("row1"))
                        {
                            var columns = row.QuerySelectorAll("> td");

                            var localTime = DateTime.Parse(columns[0].InnerText);
                            var localDateTime = new LocalDateTime(localDate.Year, localDate.Month, localDate.Day, localTime.Hour, localTime.Minute);
                            var time = localDateTime.InZoneStrictly(Timezone);
                            var sheet = columns[1].InnerText.Trim().ToUpperInvariant();
                            var teamNames = columns[2].InnerText
                                .Split(new[] { " vs." }, 2, StringSplitOptions.None)
                                .Select(s => s.Trim())
                                .Where(s => s.Length != 0)
                                .ToArray();

                            if (sheet.Length == 0 || sheet == "BYE")
                                continue;

                            if (teamNames.Length != 2)
                            {
                                if (columns[2].InnerText.Contains(" vs."))
                                    continue; // bye

                                throw new NotImplementedException("Don't know how to interpret team names.");
                            }

                            var teams = teamNames
                                .Select(s => Database.Team.Get(s, league.Id) ?? teamsByMember[s])
                                .ToArray();

                            var game = Database.Game.Get(league.Id, time, sheet);
                            if (game == null)
                            {
                                Database.Game.Create(league.Id, time, sheet, teams[0].Id, teams[1].Id);
                            }
                            else if (game.Teams[0].Id != teams[0].Id || game.Teams[1].Id != teams[1].Id)
                            {
                                throw new NotImplementedException("Teams changed.");
                            }

                            deletedGames.Remove((league.Id, time, sheet));
                        }
                        else
                        {
                            // New game day
                            localDate = LocalDate.FromDateTime(DateTime.Parse(row.InnerText));
                        }
                    }

                    // Remove any deleted games from this league
                    foreach (var game in deletedGames)
                    {
                        Database.Game.Delete(game.LeagueId, game.Time, game.Sheet);
                    }
                }
            }
        }

        private static async Task<HtmlNode[]> FetchInfoTable(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var result = await m_client.SendAsync(request);
            var body = await result.Content.ReadAsStringAsync();

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(body);

            var doc = htmlDoc.DocumentNode;
            var infoTable = doc.QuerySelectorAll("section#sp-main-body #sp-component > .sp-column > table").ToArray();

            return infoTable;
        }
    }
}
