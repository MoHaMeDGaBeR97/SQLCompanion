using System.Text;
using SQLCompanion.Db;

namespace SQLCompanion.Editor
{
    internal enum JoinKind { Inner, Left }

    /// <summary>
    /// Builds a ready-to-paste JOIN clause from a foreign-key relationship, choosing the correct
    /// column sides based on the relationship direction and generating a sensible table alias.
    /// </summary>
    internal static class JoinClauseBuilder
    {
        /// <summary>
        /// Produces e.g.:  INNER JOIN [dbo].[OrderItem] oi ON oi.OrderId = t.OrderId
        /// </summary>
        /// <param name="rel">The relationship to join to.</param>
        /// <param name="focusAlias">Alias already used for the focus/base table (defaults to "t").</param>
        /// <param name="kind">INNER or LEFT.</param>
        public static string Build(RelationshipInfo rel, string focusAlias = "t", JoinKind kind = JoinKind.Inner)
        {
            string otherAlias = GenerateAlias(rel.OtherTable, focusAlias);
            string joinWord = kind == JoinKind.Left ? "LEFT JOIN" : "INNER JOIN";

            var sb = new StringBuilder();
            sb.Append(joinWord)
              .Append(" [").Append(rel.OtherSchema).Append("].[").Append(rel.OtherTable).Append("] ")
              .Append(otherAlias)
              .Append(" ON ");

            for (int i = 0; i < rel.ColumnPairs.Count; i++)
            {
                var pair = rel.ColumnPairs[i];

                // Map each column to the correct table side based on direction:
                //  Outgoing: focus is the PARENT (holds the FK) -> parent col is on the focus table.
                //  Incoming: focus is the REFERENCED table       -> referenced col is on the focus table.
                string focusCol, otherCol;
                if (rel.Direction == RelationshipInfo.RelDirection.Outgoing)
                {
                    focusCol = pair.ParentColumn;
                    otherCol = pair.ReferencedColumn;
                }
                else
                {
                    focusCol = pair.ReferencedColumn;
                    otherCol = pair.ParentColumn;
                }

                if (i > 0) sb.Append(" AND ");
                sb.Append(otherAlias).Append('.').Append(otherCol)
                  .Append(" = ")
                  .Append(focusAlias).Append('.').Append(focusCol);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates a short alias from a table name (e.g. OrderItem -> "oi", Customer -> "c"),
        /// avoiding a collision with the focus alias.
        /// </summary>
        public static string GenerateAlias(string tableName, string focusAlias)
        {
            if (string.IsNullOrEmpty(tableName)) return "x";

            var sb = new StringBuilder();
            foreach (char c in tableName)
            {
                if (char.IsUpper(c) || char.IsDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            // If the name had no obvious word boundaries, fall back to its first letter.
            string alias = sb.Length > 0 ? sb.ToString() : char.ToLowerInvariant(tableName[0]).ToString();

            // Keep it short.
            if (alias.Length > 4) alias = alias.Substring(0, 4);

            // Avoid clashing with the focus alias.
            if (string.Equals(alias, focusAlias, System.StringComparison.OrdinalIgnoreCase))
                alias += "2";

            return alias;
        }
    }
}
