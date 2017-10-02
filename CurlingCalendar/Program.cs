using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace CurlingCalendar
{
	class DownloadCurlingCalendar
	{
		struct CalendarEvent
		{
			public string Sheet;
			public string Name;
			public DateTime Time;
			public CalendarEvent(string name, string sheet, DateTime time)
			{
				Sheet = sheet;
				Name = name;
				Time = time;
			}
			public override string ToString()
			{
				return string.Format("{0} - {1}", Time, Name);
			}
		}

		private static string Username;
		private static string Password;
		[STAThread]
		public static void Main()
		{
			Console.WriteLine("Converts Denver Curling Club your next games into an iCal file.");
			GetCredentials();

			CompileFromWeb();
		}

		public static void GetCredentials()
		{
			Console.Write("Username: ");
			Username = ReadInput();
			Console.Write("Password (ctrl + v): ");
			Password = ReadInput(isPassword: true);
		}

		private static string ReadInput(bool isPassword = false)
		{
			var builder = new StringBuilder();
			while (true)
			{
				var key = Console.ReadKey(true);

				if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
				{
					switch (key.Key)
					{
						case ConsoleKey.V:
							{
								var clipboard = System.Windows.Forms.Clipboard.GetText();
								builder.Append(clipboard);
								Console.Write(new string('*', clipboard.Length));
							}
							break;
					}
					continue;
				}

				switch (key.Key)
				{
					case ConsoleKey.Backspace:
						{
							if (builder.Length > 0)
							{
								builder.Remove(builder.Length - 1, 1);
								Console.Write("\b \b");
							}
						}
						break;
					case ConsoleKey.Enter:
						{
							Console.WriteLine();
							return builder.ToString();
						}
					default:
						{
							builder.Append(key.KeyChar);
							var c = key.KeyChar;
							if (isPassword)
								c = '*';
							Console.Write(c);
						}
						break;
				}
			}
		}

		public static void CompileFromWeb()
		{
			Console.WriteLine("Getting index...");
			var formData = new Dictionary<string, string>();
			var cookieContainer = new CookieContainer();
			var handler = new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip, CookieContainer = cookieContainer, UseCookies = true, AllowAutoRedirect = false };
			var client = new HttpClient(handler);
			client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.90 Safari/537.36");
			client.BaseAddress = new Uri("https://denvercurlingclub.com");

			using (var request = new HttpRequestMessage(HttpMethod.Get, "https://denvercurlingclub.com/index.php/"))
			{
				request.Headers.Add("Keep-Alive", "true");
				var result = client.SendAsync(request).Result;
				var body = result.Content.ReadAsStringAsync().Result;

				var htmlDoc = new HtmlDocument();
				htmlDoc.LoadHtml(body);

				var doc = htmlDoc.DocumentNode;
				var form = doc.SelectNodes("//form[@action=\"https://denvercurlingclub.com/index.php/member-login\"]").First();

				var inputs = form.SelectNodes("//input").ToArray();
				foreach (var input in inputs)
				{
					var name = input.GetAttributeValue("name", null);
					var value = input.GetAttributeValue("value", null);
					formData.Add(name, value);
				}
			}

			formData.Add("Submit", null);


			Console.WriteLine("Logging in...");
			while (true)
			{
				formData["username"] = Username;
				formData["passwd"] = Password;
				using (var request = new HttpRequestMessage(HttpMethod.Post, "https://denvercurlingclub.com/index.php/member-login"))
				using (var content = new FormUrlEncodedContent(formData))
				{
					request.Headers.Referrer = new Uri("https://denvercurlingclub.com/index.php/");
					request.Headers.TryAddWithoutValidation("Origin", "https://denvercurlingclub.com");

					request.Content = content;
					var result = client.SendAsync(request).Result;
					if (result.StatusCode == HttpStatusCode.SeeOther)
						break;

					Console.WriteLine("Invalid credentials");
					Console.Write("Password: ");
					Password = ReadInput(isPassword: true);
				}
			}

			Console.WriteLine("Getting games...");
			var days = new Dictionary<DayOfWeek, List<CalendarEvent>>();
			using (var request = new HttpRequestMessage(HttpMethod.Get, "https://denvercurlingclub.com/index.php/member-s-home/247-your-next-games"))
			{
				var result = client.SendAsync(request).Result;
				var body = result.Content.ReadAsStringAsync().Result;

				var htmlDoc = new HtmlDocument();
				htmlDoc.LoadHtml(body);

				var mainBody = htmlDoc.GetElementbyId("rt-mainbody");
				var tables = mainBody.SelectNodes(".//table");
				foreach (var table in tables)
				{
					var rows = table.SelectNodes(".//tr");
					foreach (var row in rows)
					{
						var columns = row.SelectNodes(".//td").ToArray();
						if (columns.Length != 5)
							continue;

						var date = DateTime.Parse(columns[1].InnerText);
						var time = DateTime.Parse(columns[2].InnerText);
						var datetime = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second);

						var dayOfWeek = datetime.DayOfWeek;
						var game = columns[3].InnerText;

						var sheet = columns[4].InnerText.Replace("Sheet: ", "");
						if (!days.ContainsKey(dayOfWeek))
							days.Add(dayOfWeek, new List<CalendarEvent>());
						days[dayOfWeek].Add(new CalendarEvent(game, sheet, datetime));
					}
				}
			}

			ProcessDays(days);
		}

		private static void ProcessDays(Dictionary<DayOfWeek, List<CalendarEvent>> days)
		{
			var sfd = new System.Windows.Forms.SaveFileDialog
			{
				Title = "Title",
				AddExtension = true,
				AutoUpgradeEnabled = true,
				DefaultExt = ".iCal",
				Filter = "Calendar File (*.iCal)|*.ical",
			};
			var result = sfd.ShowDialog();
			if (result != System.Windows.Forms.DialogResult.OK)
				return;

			var outPath = sfd.FileName;
			var events = new List<CalendarEvent>();
			foreach (var day in days)
			{
				var names = new Dictionary<string, int>();
				var teamName = day.Value
					.SelectMany(s => s.Name.Split(new[] { "vs" }, StringSplitOptions.None))
					.Select(s => s.Trim())
					.GroupBy(s => s)
					.OrderByDescending(g => g.Count())
					.First()
					.Key;

				foreach (var e in day.Value)
				{
					var name = e.Name.Replace("vs", "").Replace(teamName, "").Trim();
					name = string.Format("{0}: {1}", e.Sheet, name);
					events.Add(new CalendarEvent(name, e.Sheet, e.Time));
				}
			}

			var builder = new StringBuilder();
			builder.AppendLine("BEGIN:VCALENDAR");
			builder.AppendLine("PRODID:CurlingCalendar");
			builder.AppendLine("CALSCALE:GREGORIAN");
			builder.AppendLine("VERSION:2.0");
			//builder.AppendLine("METHOD:PUBLISH");
			//builder.AppendLine("X-WR-TIMEZONE:America/Denver");
			//builder.AppendLine(GetTimezoneBlock(TimezoneName.Mountain));

			var tzid = TimeZone.CurrentTimeZone.StandardName;

			foreach (var e in events)
			{
				builder.AppendLine("BEGIN:VEVENT");

				var time = e.Time;
				var endTime = time.AddHours(2);

				builder.AppendFormat("DTSTART:{0}", GetICalDateTime(time)).AppendLine();
				builder.AppendFormat("DTEND:{0}", GetICalDateTime(endTime)).AppendLine();
				builder.AppendFormat("SUMMARY:{0}", e.Name).AppendLine();
				builder.AppendFormat("LOCATION:{0}", "14100 W 7th Ave, Golden, CO 80401").AppendLine();

				builder.AppendLine("END:VEVENT");
			}
			builder.AppendLine("END:VCALENDAR");
			File.WriteAllText(outPath, builder.ToString());
		}

		enum TimezoneName
		{
			Pacific,
			Mountain,
			Arizona,
			Central,
			Eastern,
		}

		static string GetICalDateTime(DateTime time)
		{
			time = time.ToUniversalTime();
			return time.ToString("yyyy") + time.ToString("MM") + time.ToString("dd") + "T" + time.ToString("HH") + time.ToString("mm") + "00Z";
		}
		static string GetTimezoneBlock(TimezoneName name)
		{
			switch (name)
			{
				case TimezoneName.Pacific:
					return
@"BEGIN:VTIMEZONE
TZID:America/Los_Angeles
X-LIC-LOCATION:America/Los_Angeles
BEGIN:DAYLIGHT
TZOFFSETFROM:-0800
TZOFFSETTO:-0700
TZNAME:PDT
DTSTART:19700308T020000
RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU
END:DAYLIGHT
BEGIN:STANDARD
TZOFFSETFROM:-0700
TZOFFSETTO:-0800
TZNAME:PST
DTSTART:19701101T020000
RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU
END:STANDARD
END:VTIMEZONE";
				case TimezoneName.Arizona:
					return
@"BEGIN:VTIMEZONE
TZID:America/Phoenix
X-LIC-LOCATION:America/Phoenix
BEGIN:STANDARD
TZOFFSETFROM:-0700
TZOFFSETTO:-0700
TZNAME:MST
DTSTART:19700101T000000
END:STANDARD
END:VTIMEZONE";
				case TimezoneName.Central:
					return
@"BEGIN:VTIMEZONE
TZID:America/Chicago
X-LIC-LOCATION:America/Chicago
BEGIN:DAYLIGHT
TZOFFSETFROM:-0600
TZOFFSETTO:-0500
TZNAME:CDT
DTSTART:19700308T020000
RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU
END:DAYLIGHT
BEGIN:STANDARD
TZOFFSETFROM:-0500
TZOFFSETTO:-0600
TZNAME:CST
DTSTART:19701101T020000
RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU
END:STANDARD
END:VTIMEZONE";
				case TimezoneName.Eastern:
					return
					@"BEGIN:VTIMEZONE
TZID:America/New_York
X-LIC-LOCATION:America/New_York
BEGIN:DAYLIGHT
TZOFFSETFROM:-0500
TZOFFSETTO:-0400
TZNAME:EDT
DTSTART:19700308T020000
RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU
END:DAYLIGHT
BEGIN:STANDARD
TZOFFSETFROM:-0400
TZOFFSETTO:-0500
TZNAME:EST
DTSTART:19701101T020000
RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU
END:STANDARD
END:VTIMEZONE";
				case TimezoneName.Mountain:
					return
@"BEGIN:VTIMEZONE
TZID:America/Denver
X-LIC-LOCATION:America/Denver
BEGIN:DAYLIGHT
TZOFFSETFROM:-0700
TZOFFSETTO:-0600
TZNAME:MDT
DTSTART:19700308T020000
RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU
END:DAYLIGHT
BEGIN:STANDARD
TZOFFSETFROM:-0600
TZOFFSETTO:-0700
TZNAME:MST
DTSTART:19701101T020000
RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU
END:STANDARD
END:VTIMEZONE";
				default:
					throw new NotImplementedException();
			}
		}
	}
}
