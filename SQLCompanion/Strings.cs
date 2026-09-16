namespace SQLCompanion
{
    /// <summary>
    /// Single source of truth for ALL user-facing text: tooltips, button captions,
    /// and messages. Localize the whole extension by translating this one file
    /// (or by swapping it for a ResourceManager-backed implementation with the same members).
    ///
    /// Tooltip guideline: one short, action-oriented sentence describing what a click does.
    /// </summary>
    internal static class Strings
    {
        // --- Product ---
        public const string ProductName = "SQL Companion";

        // ============================================================
        //  TOOLTIPS  (every clickable element must have one)
        // ============================================================
        internal static class Tooltips
        {
            // Menu / toolbar entry points
            public const string ShowColumnSearch = "Open the column search window to find columns across all tables and views.";
            public const string ShowRelationships = "Open the relationships panel to see foreign-key links for a table.";
            public const string DeclareVariables = "Adds DECLARE statements for any variables used without one.";

            // Column search window
            public const string SearchBox = "Type a full or partial column name; matching is case-insensitive.";
            public const string RunSearch = "Search the connected database for columns matching your text.";
            public const string SchemaFilter = "Show only results from the selected schema.";
            public const string ScopeToggle = "Switch between searching just the current database and every database on the server.";
            public const string RecentSearches = "Pick one of your recent searches to run it again.";
            public const string ExportCsv = "Save the current results to a CSV file.";
            public const string CopyAllToClipboard = "Copy all current results to the clipboard as tab-separated text.";
            public const string RefreshConnection = "Re-read the active server and database from SSMS.";

            // Column search result-row actions
            public const string InsertQualifiedTable = "Insert the fully-qualified schema.table name at the cursor.";
            public const string InsertColumnName = "Insert this column name at the cursor.";
            public const string CopyColumnName = "Copy this column name to the clipboard.";
            public const string CopyQualifiedTable = "Copy the fully-qualified schema.table name to the clipboard.";
            public const string GenerateSelect = "Insert 'SELECT TOP 100 * FROM schema.table' at the cursor.";

            // Relationships panel
            public const string RelationshipsTableBox = "Type schema.table (or just table) to load its foreign-key relationships.";
            public const string DatabaseDropdown = "Choose which database to list tables from.";
            public const string TableDropdown = "Pick a table to load its foreign-key relationships (you can also type schema.table).";
            public const string RefreshLists = "Reload the database and table lists from the active connection.";
            public const string LoadRelationships = "Load foreign-key relationships for the selected table.";
            public const string UseActiveTable = "Use the table name currently selected in the query editor.";
            public const string JoinTypeDropdown = "Choose whether inserted joins use INNER JOIN or LEFT JOIN.";
            public const string InsertJoinInner = "Insert an INNER JOIN to this related table at the cursor.";
            public const string InsertJoinRow = "Insert a JOIN to this related table at the cursor using its foreign-key columns.";
        }

        // ============================================================
        //  CAPTIONS  (button/labels/column headers)
        // ============================================================
        internal static class Captions
        {
            public const string ColumnSearchWindow = "SQL Companion: Column Search";
            public const string RelationshipsWindow = "SQL Companion: Relationships";

            public const string Search = "Search";
            public const string Export = "Export CSV";
            public const string CopyAll = "Copy All";
            public const string Refresh = "Refresh";
            public const string Load = "Load";
            public const string UseActive = "Use active table";
            public const string ScopeCurrentDb = "Current database";
            public const string ScopeAllDbs = "All databases (server)";
            public const string AllSchemas = "(all schemas)";
        }

        // ============================================================
        //  MESSAGES  (status + error text)
        // ============================================================
        internal static class Messages
        {
            public const string NoActiveConnection =
                "No active SQL connection was found. Open or click into a query window that is connected to a server, then try again.";

            public const string NoResults = "No matching columns were found.";
            public const string NoRelationships = "No foreign-key relationships were found for that table.";
            public const string EnterSearchText = "Enter at least one character to search.";
            public const string EnterTableName = "Enter a table name (optionally schema-qualified) to load relationships.";
            public const string NoActiveEditor =
                "No active query editor was found. Click into a SQL query window and try again.";
            public const string DeclareNothingToDo =
                "No undeclared variables were found in the current query.";

            public static string Connected(string server, string database) =>
                $"Connected: {server} / {database}";

            public static string QueryFailed(string detail) =>
                $"The query failed: {detail}";

            public static string DeclareAdded(int count) =>
                count == 1 ? "Added 1 DECLARE statement." : $"Added {count} DECLARE statements.";

            public static string SearchStatus(int count) =>
                count == 1 ? "1 column found." : $"{count} columns found.";
        }
    }
}
