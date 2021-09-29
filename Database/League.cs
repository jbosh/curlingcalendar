using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace CurlingCalendar
{
    public static partial class Database
    {
        public class League
        {
            public int Id { get; }
            public string Name { get; }
            public League(int id, string name)
            {
                Id = id;
                Name = name;
            }

            public static League FromReader(SqliteDataReader reader)
            {
                var id = reader.GetInt32("id");
                var name = reader.GetString("name");
                var league = new League(id, name);
                return league;
            }

            public static League Create(int id, string name)
            {
                ExecuteNonQuery("INSERT INTO leagues (id, name) VALUES (@id, @name)", ("id", id), ("name", name));
                return new League(id, name);
            }

            public static League? Get(int id) => ExecuteGet("SELECT * FROM leagues WHERE id=@id", FromReader, ("id", id));
        }
    }
}
