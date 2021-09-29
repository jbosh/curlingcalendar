using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace CurlingCalendar
{
    public static partial class Database
    {
        private static SqliteConnection m_connection = null!;
        private static readonly Version DatabaseVersion = new Version(1, 0);
        public static void Initialize(string path)
        {
            m_connection = new SqliteConnection($"Data Source={path}");
            m_connection.Open();

            Populate();
        }

        private static void Populate()
        {
            var metaName = ExecuteScalar<string?>("SELECT name FROM sqlite_master WHERE type='table' AND name='__meta'");
            if (metaName == null)
            {
                PopulateAll();
                return;
            }

            var version = Version.Parse(Meta.Get("version")!);
            if (version == DatabaseVersion)
                return;

            if (version > DatabaseVersion)
                throw new NotImplementedException("Database version was too new.");

            PopulateAll();
        }

        private static void PopulateAll()
        {
            using var transaction = m_connection.BeginTransaction();
            var assembly = typeof(Database).GetTypeInfo().Assembly;
            string script;
            using (var resourceStream = assembly.GetManifestResourceStream("CurlingCalendar.Database.Scripts.full.sql")!)
            {
                using var reader = new StreamReader(resourceStream);
                script = reader.ReadToEnd();
            }

            ExecuteNonQuery(script);
            Meta.Set("version", DatabaseVersion.ToString());
            transaction.Commit();
        }

        private static void ExecuteNonQuery(string text, params (string name, object? value)[] parameters)
        {
            using var command = m_connection.CreateCommand();
            command.CommandText = text;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }

        private static IEnumerable<T> ExecuteReader<T>(string text, Func<SqliteDataReader, T> read, params (string name, object value)[] parameters)
        {
            using var command = m_connection.CreateCommand();
            command.CommandText = text;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                yield return read(reader);
            }
        }

        private static T? ExecuteGet<T>(string text, Func<SqliteDataReader, T> read, params (string name, object value)[] parameters) where T : class
        {
            using var command = m_connection.CreateCommand();
            command.CommandText = text;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using var reader = command.ExecuteReader();

            if (!reader.Read())
                return null;
            var result = read(reader);
            if (reader.Read())
                throw new Exception("Too many rows for query.");
            return result;
        }

        private static T ExecuteScalar<T>(string text, params (string name, object value)[] parameters)
        {
            using var command = m_connection.CreateCommand();
            command.CommandText = text;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var obj = command.ExecuteScalar();
            return (T)obj;
        }

        public static SqliteTransaction BeginTransaction() => m_connection.BeginTransaction();

        public static void Dispose()
        {
            m_connection.Dispose();
        }
    }

    public static class DatabaseExtensions
    {
        public static int GetInt32(this SqliteDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.GetInt32(ordinal);
        }

        public static long GetInt64(this SqliteDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.GetInt64(ordinal);
        }

        public static string? GetStringOrNull(this SqliteDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        public static string GetString(this SqliteDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.GetString(ordinal);
        }

        public static DateTime GetDateTime(this SqliteDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.GetDateTime(ordinal);
        }
    }
}
