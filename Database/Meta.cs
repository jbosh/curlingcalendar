using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NodaTime;
using NodaTime.Text;

namespace CurlingCalendar
{
    public static partial class Database
    {
        public static class Meta
        {
            public static void Set(string key, string value)
                => ExecuteNonQuery(
                    @"INSERT INTO __meta (key, value) VALUES (@key, @value)
                        ON CONFLICT(key)
                        DO UPDATE SET value=@value",
                    ("key", key),
                    ("value", value));

            public static string? Get(string key)
                => ExecuteGet(
                    "SELECT value FROM __meta WHERE key=@key",
                    r => r.GetString(0),
                    ("key", key));

            public static void Delete(string key)
                => ExecuteNonQuery(
                    "DELETE FROM __meta WHERE key=@key",
                    ("key", key));

            public static IEnumerable<KeyValuePair<string, string>> GetAll()
                => ExecuteReader(
                    "SELECT key, value FROM __meta",
                    r => new KeyValuePair<string, string>(r.GetString(0), r.GetString(1)));
        }
    }
}
