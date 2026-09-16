using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SQLCompanion.Db
{
    /// <summary>Describes the active SSMS connection (server + database + how to connect).</summary>
    internal sealed class ActiveConnectionInfo
    {
        public string Server { get; set; }
        public string Database { get; set; }
        /// <summary>A ready-to-use ADO.NET connection string built from the SSMS connection context.</summary>
        public string ConnectionString { get; set; }

        public bool IsValid => !string.IsNullOrEmpty(Server) && !string.IsNullOrEmpty(ConnectionString);
    }

    /// <summary>One row of a column-search result. Implements INotifyPropertyChanged for WPF binding.</summary>
    internal sealed class ColumnResult : INotifyPropertyChanged
    {
        public string DatabaseName { get; set; }
        public string Schema { get; set; }
        public string TableName { get; set; }
        /// <summary>"BASE TABLE" or "VIEW".</summary>
        public string ObjectType { get; set; }
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }

        /// <summary>schema.table, safely bracket-quoted.</summary>
        public string QualifiedTable => $"[{Schema}].[{TableName}]";

        /// <summary>Human-friendly key badge for the grid ("PK", "FK", "PK,FK", or "").</summary>
        public string KeyInfo
        {
            get
            {
                if (IsPrimaryKey && IsForeignKey) return "PK,FK";
                if (IsPrimaryKey) return "PK";
                if (IsForeignKey) return "FK";
                return string.Empty;
            }
        }

        public string Nullable => IsNullable ? "YES" : "NO";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// One foreign-key relationship as seen from a "focus" table.
    /// Direction tells you whether the focus table references another table (Outgoing)
    /// or is referenced by another table (Incoming).
    /// </summary>
    internal sealed class RelationshipInfo
    {
        public enum RelDirection { Outgoing, Incoming }

        public RelDirection Direction { get; set; }
        public string ForeignKeyName { get; set; }

        // The "parent" side owns the FK column list (the referencing table).
        public string ParentSchema { get; set; }
        public string ParentTable { get; set; }

        // The "referenced" side is the primary/unique key table.
        public string ReferencedSchema { get; set; }
        public string ReferencedTable { get; set; }

        /// <summary>Column pairs (parentColumn -> referencedColumn), same order as the FK definition.</summary>
        public System.Collections.Generic.List<ColumnPair> ColumnPairs { get; }
            = new System.Collections.Generic.List<ColumnPair>();

        /// <summary>The table on the "other side" relative to the focus table.</summary>
        public string OtherSchema => Direction == RelDirection.Outgoing ? ReferencedSchema : ParentSchema;
        public string OtherTable => Direction == RelDirection.Outgoing ? ReferencedTable : ParentTable;

        public string DirectionGlyph => Direction == RelDirection.Outgoing ? "→ references" : "← referenced by";

        /// <summary>Compact description of the joining columns for display.</summary>
        public string ColumnsDisplay
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var p in ColumnPairs)
                    parts.Add($"{p.ParentColumn} = {p.ReferencedColumn}");
                return string.Join(", ", parts);
            }
        }

        public sealed class ColumnPair
        {
            public string ParentColumn { get; set; }
            public string ReferencedColumn { get; set; }
        }
    }
}
