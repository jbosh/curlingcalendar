using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace CurlingCalendar
{
    public static partial class Database
    {
        public class User
        {
            public int Id { get; set; }
            public string FullName { get; set; }

            public User(int id, string fullName)
            {
                Id = id;
                FullName = fullName;
            }

            private static User FromReader(SqliteDataReader reader)
            {
                var id = reader.GetInt32("id");
                var fullname = reader.GetString("fullname");
                var user = new User(id, fullname);
                return user;
            }

            public static void Create(User user)
                => ExecuteNonQuery(
                    "INSERT INTO users (id, fullname) VALUES (@id, @fullname)",
                    ("id", user.Id),
                    ("fullname", user.FullName));

            public static void Update(User user)
                => ExecuteNonQuery(
                    "UPDATE users SET fullname=@fullname WHERE id=@id",
                    ("id", user.Id),
                    ("fullname", user.FullName));

            public static User? FromId(int id)
                => ExecuteGet("SELECT id, fullname FROM users WHERE id=@id", FromReader, ("id", id));

            public static IEnumerable<User> FromName(string name)
            {
                var query = "SELECT id, fullname FROM users WHERE";
                var parameters = new List<(string, object)>();
                var splits = name.Split(' ').Select(s => s.Trim(' ', ',')).ToArray();
                for (var i = 0; i < splits.Length; i++)
                {
                    var split = splits[i];
                    if (i != 0)
                        query += " AND ";
                    query += $" fullname LIKE @query{i}";
                    parameters.Add(($"query{i}", $"%{split}%"));
                }

                return ExecuteReader(query, FromReader, parameters.ToArray());
            }
        }
    }
}