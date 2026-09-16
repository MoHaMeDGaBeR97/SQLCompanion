using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace SQLCompanion.Db
{
    /// <summary>
    /// The DB access layer. Everything that talks to SQL Server lives here so the feature code
    /// stays free of ADO.NET. All methods are async and throw <see cref="SqlException"/> (or a
    /// general exception) on failure — callers translate that into a friendly message.
    /// </summary>
    internal sealed class DatabaseService
    {
        // ------------------------------------------------------------------
        //  Column search
        // ------------------------------------------------------------------

        /// <summary>
        /// Finds every column (in tables AND views) whose name contains <paramref name="term"/>.
        /// Matching is partial and case-insensitive. When <paramref name="allDatabases"/> is true,
        /// the search runs across all online user databases on the server.
        /// </summary>
        public async Task<List<ColumnResult>> SearchColumnsAsync(
            ActiveConnectionInfo conn, string term, bool allDatabases, CancellationToken ct = default)
        {
            var results = new List<ColumnResult>();

            if (!allDatabases)
            {
                await SearchColumnsInDatabaseAsync(conn.ConnectionString, conn.Database, term, results, ct)
                    .ConfigureAwait(false);
                return results;
            }

            // All-databases scope: enumerate online user DBs and query each one independently.
            var dbNames = await GetDatabaseNamesAsync(conn, ct).ConfigureAwait(false);
            foreach (var db in dbNames)
            {
                ct.ThrowIfCancellationRequested();
                // Point the connection at each database in turn.
                var perDb = new SqlConnectionStringBuilder(conn.ConnectionString) { InitialCatalog = db }
                    .ConnectionString;
                try
                {
                    await SearchColumnsInDatabaseAsync(perDb, db, term, results, ct).ConfigureAwait(false);
                }
                catch (SqlException)
                {
                    // Skip databases we can't access (offline, no permission, etc.) rather than fail
                    // the whole search. Flagged: silent skip is intentional for cross-DB scope.
                }
            }
            return results;
        }

        private async Task SearchColumnsInDatabaseAsync(
            string connectionString, string databaseName, string term, List<ColumnResult> sink, CancellationToken ct)
        {
            const string sql = @"
;WITH pk AS (
    SELECT tc.TABLE_SCHEMA, tc.TABLE_NAME, kcu.COLUMN_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
      ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
     AND kcu.TABLE_SCHEMA   = tc.TABLE_SCHEMA
    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
),
fk AS (
    SELECT tc.TABLE_SCHEMA, tc.TABLE_NAME, kcu.COLUMN_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
      ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
     AND kcu.TABLE_SCHEMA   = tc.TABLE_SCHEMA
    WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
)
SELECT
    c.TABLE_SCHEMA,
    c.TABLE_NAME,
    t.TABLE_TYPE,
    c.COLUMN_NAME,
    CASE
        WHEN c.DATA_TYPE IN ('varchar','char','varbinary','binary')
            THEN c.DATA_TYPE + '(' + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX'
                 ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS varchar(11)) END + ')'
        WHEN c.DATA_TYPE IN ('nvarchar','nchar')
            THEN c.DATA_TYPE + '(' + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX'
                 ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS varchar(11)) END + ')'
        WHEN c.DATA_TYPE IN ('decimal','numeric')
            THEN c.DATA_TYPE + '(' + CAST(c.NUMERIC_PRECISION AS varchar(11)) + ',' + CAST(c.NUMERIC_SCALE AS varchar(11)) + ')'
        ELSE c.DATA_TYPE
    END AS FULL_TYPE,
    c.IS_NULLABLE,
    CAST(CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IS_PK,
    CAST(CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IS_FK
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES t
      ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
LEFT JOIN pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
LEFT JOIN fk ON fk.TABLE_SCHEMA = c.TABLE_SCHEMA AND fk.TABLE_NAME = c.TABLE_NAME AND fk.COLUMN_NAME = c.COLUMN_NAME
WHERE LOWER(c.COLUMN_NAME) LIKE LOWER(@pattern)
ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME;";

            using (var connection = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.CommandTimeout = 60;
                cmd.Parameters.Add("@pattern", SqlDbType.NVarChar, 300).Value = "%" + (term ?? string.Empty) + "%";
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await r.ReadAsync(ct).ConfigureAwait(false))
                    {
                        sink.Add(new ColumnResult
                        {
                            DatabaseName = databaseName,
                            Schema = r.GetString(0),
                            TableName = r.GetString(1),
                            ObjectType = r.GetString(2),
                            ColumnName = r.GetString(3),
                            DataType = r.GetString(4),
                            IsNullable = string.Equals(r.GetString(5), "YES", StringComparison.OrdinalIgnoreCase),
                            IsPrimaryKey = r.GetBoolean(6),
                            IsForeignKey = r.GetBoolean(7)
                        });
                    }
                }
            }
        }

        /// <summary>Online user databases (excludes system DBs and tempdb).</summary>
        public async Task<List<string>> GetDatabaseNamesAsync(ActiveConnectionInfo conn, CancellationToken ct = default)
        {
            var names = new List<string>();
            const string sql = @"SELECT name FROM sys.databases
                                 WHERE state = 0 AND database_id > 4
                                 ORDER BY name;"; // > 4 excludes master/tempdb/model/msdb
            using (var connection = new SqlConnection(conn.ConnectionString))
            using (var cmd = new SqlCommand(sql, connection))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await r.ReadAsync(ct).ConfigureAwait(false))
                        names.Add(r.GetString(0));
                }
            }
            return names;
        }

        // ------------------------------------------------------------------
        //  Foreign-key relationships
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns every FK relationship touching the given table: both the tables it references
        /// (outgoing) and the tables that reference it (incoming), including the joining columns.
        /// Reads sys.foreign_keys / sys.foreign_key_columns as requested.
        /// </summary>
        /// <param name="schema">Schema name, or null/empty to match by table name only.</param>
        public async Task<List<RelationshipInfo>> GetRelationshipsAsync(
            ActiveConnectionInfo conn, string schema, string table, CancellationToken ct = default)
        {
            bool haveSchema = !string.IsNullOrWhiteSpace(schema);

            // Match the focus table on either side of each FK.
            string where = haveSchema
                ? "((ps.name = @schema AND pt.name = @table) OR (rs.name = @schema AND rt.name = @table))"
                : "(pt.name = @table OR rt.name = @table)";

            string sql = $@"
SELECT
    fk.name                                   AS FKName,
    ps.name AS ParentSchema,  pt.name AS ParentTable,
    rs.name AS RefSchema,     rt.name AS RefTable,
    pc.name AS ParentColumn,  rc.name AS RefColumn,
    fkc.constraint_column_id  AS Ordinal
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables  pt ON pt.object_id = fk.parent_object_id
JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.tables  rt ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id     AND pc.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE {where}
ORDER BY fk.name, fkc.constraint_column_id;";

            // Group rows by FK name into RelationshipInfo objects, preserving column order.
            var byFk = new Dictionary<string, RelationshipInfo>(StringComparer.Ordinal);

            using (var connection = new SqlConnection(conn.ConnectionString))
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.CommandTimeout = 60;
                cmd.Parameters.Add("@table", SqlDbType.NVarChar, 300).Value = table ?? string.Empty;
                if (haveSchema)
                    cmd.Parameters.Add("@schema", SqlDbType.NVarChar, 300).Value = schema;

                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await r.ReadAsync(ct).ConfigureAwait(false))
                    {
                        string fkName = r.GetString(0);
                        string parentSchema = r.GetString(1);
                        string parentTable = r.GetString(2);
                        string refSchema = r.GetString(3);
                        string refTable = r.GetString(4);
                        string parentColumn = r.GetString(5);
                        string refColumn = r.GetString(6);

                        if (!byFk.TryGetValue(fkName, out var rel))
                        {
                            // Direction is relative to the focus table:
                            //  - if the focus table is the parent (the one holding the FK), it's Outgoing.
                            //  - otherwise the focus table is referenced, so it's Incoming.
                            bool focusIsParent = TableMatches(parentSchema, parentTable, schema, table, haveSchema);

                            rel = new RelationshipInfo
                            {
                                ForeignKeyName = fkName,
                                Direction = focusIsParent
                                    ? RelationshipInfo.RelDirection.Outgoing
                                    : RelationshipInfo.RelDirection.Incoming,
                                ParentSchema = parentSchema,
                                ParentTable = parentTable,
                                ReferencedSchema = refSchema,
                                ReferencedTable = refTable
                            };
                            byFk.Add(fkName, rel);
                        }

                        rel.ColumnPairs.Add(new RelationshipInfo.ColumnPair
                        {
                            ParentColumn = parentColumn,
                            ReferencedColumn = refColumn
                        });
                    }
                }
            }

            var list = new List<RelationshipInfo>(byFk.Values);
            // Outgoing first, then incoming, then by other-table name for a stable, readable order.
            list.Sort((a, b) =>
            {
                int d = a.Direction.CompareTo(b.Direction);
                if (d != 0) return d;
                return string.Compare(a.OtherTable, b.OtherTable, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static bool TableMatches(string s, string t, string wantSchema, string wantTable, bool haveSchema)
        {
            bool tableOk = string.Equals(t, wantTable, StringComparison.OrdinalIgnoreCase);
            if (!haveSchema) return tableOk;
            return tableOk && string.Equals(s, wantSchema, StringComparison.OrdinalIgnoreCase);
        }
    }
}
