using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SQLCompanion.Features.DeclareVariables
{
    internal sealed class InferredVariable
    {
        public string Name { get; set; }       // includes the leading '@'
        public string SqlType { get; set; }     // e.g. "NVARCHAR(MAX)", "INT", "DECIMAL(18,4)"
    }

    /// <summary>
    /// Pure text-analysis logic for the "declare variables" feature. Kept free of any VS/DTE
    /// dependency so it can be unit-tested in isolation.
    ///
    /// Behaviour:
    ///   * Finds every @variable that is USED but never DECLAREd.
    ///   * Infers a type from usage: string literals -> NVARCHAR(MAX); decimals -> DECIMAL(18,4);
    ///     integers -> INT; anything else -> NVARCHAR(MAX) (the requested fallback).
    ///   * Ignores @@system variables, and variables inside comments/string literals.
    ///
    /// LIMITATIONS (documented on purpose — this is heuristic, not a full T-SQL parser):
    ///   * Stored-procedure/function parameters are treated as "declared" only if they appear in a
    ///     DECLARE; a CREATE PROC parameter list is not parsed, so running this inside a proc body
    ///     may propose DECLAREs for parameters. Intended use is ad-hoc query batches.
    ///   * A variable used only as a default value inside another DECLARE (e.g. DECLARE @a int = @b)
    ///     is considered declared and will not be auto-declared.
    /// </summary>
    internal static class DeclareVariableGenerator
    {
        private static readonly Regex AnyVariable =
            new Regex(@"(?<!@)@(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        public static IReadOnlyList<InferredVariable> FindUndeclared(string sql)
        {
            var result = new List<InferredVariable>();
            if (string.IsNullOrWhiteSpace(sql)) return result;

            string scrubbed = Scrub(sql);

            var declared = GetDeclaredNames(scrubbed);

            // Collect used variables in order of first appearance, de-duplicated.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var used = new List<string>();
            foreach (Match m in AnyVariable.Matches(scrubbed))
            {
                string name = m.Groups["name"].Value;
                if (seen.Add(name)) used.Add(name);
            }

            foreach (var name in used)
            {
                if (declared.Contains(name)) continue;
                result.Add(new InferredVariable
                {
                    Name = "@" + name,
                    SqlType = InferType(scrubbed, name)
                });
            }
            return result;
        }

        /// <summary>Renders the DECLARE block, one statement per line.</summary>
        public static string BuildDeclareBlock(IReadOnlyList<InferredVariable> vars, string newline)
        {
            var sb = new StringBuilder();
            foreach (var v in vars)
                sb.Append("DECLARE ").Append(v.Name).Append(' ').Append(v.SqlType).Append(';').Append(newline);
            return sb.ToString();
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the set of variable names (without '@') that already appear in a DECLARE statement,
        /// including comma-separated multi-declares that may span lines.
        /// </summary>
        private static HashSet<string> GetDeclaredNames(string scrubbed)
        {
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Statement keywords that mark the end of a DECLARE region when found at a line start.
            var stopKeywords = new Regex(
                @"^\s*(SELECT|SET|INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|IF|WHILE|BEGIN|WITH|RETURN|PRINT|CREATE|ALTER|DROP|GO|USE)\b",
                RegexOptions.IgnoreCase);

            foreach (Match decl in Regex.Matches(scrubbed, @"\bDECLARE\b", RegexOptions.IgnoreCase))
            {
                int start = decl.Index + decl.Length;
                int end = FindDeclareRegionEnd(scrubbed, start, stopKeywords);
                string region = scrubbed.Substring(start, end - start);

                // A '=' introduces a default value; keep only the declaration part before it for
                // each item so default-value variables aren't misread as declared names.
                foreach (Match v in AnyVariable.Matches(StripDefaults(region)))
                    declared.Add(v.Groups["name"].Value);
            }
            return declared;
        }

        private static int FindDeclareRegionEnd(string text, int start, Regex stopKeywords)
        {
            // End at the first ';' or at a newline whose next line begins with a statement keyword.
            int i = start;
            while (i < text.Length)
            {
                if (text[i] == ';') return i;
                if (text[i] == '\n')
                {
                    // Peek the remainder of the text from just after the newline.
                    string rest = text.Substring(i + 1);
                    if (stopKeywords.IsMatch(rest)) return i;
                }
                i++;
            }
            return text.Length;
        }

        /// <summary>
        /// Removes default-value expressions (the "= expr" part of each declaration item) so we only
        /// keep declared variable names. Splits on top-level commas and drops anything after '='.
        /// </summary>
        private static string StripDefaults(string region)
        {
            var sb = new StringBuilder();
            int depth = 0;
            bool afterEquals = false;
            foreach (char c in region)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;

                if (depth == 0)
                {
                    if (c == '=') { afterEquals = true; continue; }
                    if (c == ',') { afterEquals = false; sb.Append(','); continue; }
                }
                if (!afterEquals) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Infers a SQL type for @name from how it is used in the scrubbed text.</summary>
        private static string InferType(string scrubbed, string name)
        {
            string n = Regex.Escape(name);
            const string ops = @"(=|<>|!=|>=|<=|>|<)";

            // String usage: adjacent to a (scrubbed) string literal, or used with LIKE/IN.
            var stringForward = new Regex($@"@{n}\b\s*({ops}|\bLIKE\b|\bIN\b)\s*N?'", RegexOptions.IgnoreCase);
            var stringReverse = new Regex($@"N?'[^']*'\s*{ops}\s*@{n}\b", RegexOptions.IgnoreCase);
            if (stringForward.IsMatch(scrubbed) || stringReverse.IsMatch(scrubbed))
                return "NVARCHAR(MAX)";

            // Decimal usage: adjacent to a number that has a fractional part.
            var decForward = new Regex($@"@{n}\b\s*{ops}\s*-?\d+\.\d+", RegexOptions.IgnoreCase);
            var decReverse = new Regex($@"-?\d+\.\d+\s*{ops}\s*@{n}\b", RegexOptions.IgnoreCase);
            if (decForward.IsMatch(scrubbed) || decReverse.IsMatch(scrubbed))
                return "DECIMAL(18,4)";

            // Integer usage: adjacent to a whole number.
            var intForward = new Regex($@"@{n}\b\s*{ops}\s*-?\d+\b", RegexOptions.IgnoreCase);
            var intReverse = new Regex($@"-?\d+\s*{ops}\s*@{n}\b", RegexOptions.IgnoreCase);
            if (intForward.IsMatch(scrubbed) || intReverse.IsMatch(scrubbed))
                return "INT";

            // Requested fallback.
            return "NVARCHAR(MAX)";
        }

        /// <summary>
        /// Produces a copy of the SQL with comments removed and string literals collapsed to a
        /// placeholder ('~'), so variable detection ignores commented-out / quoted @vars while type
        /// inference can still see that a literal was a string.
        /// </summary>
        private static string Scrub(string sql)
        {
            // Block comments first, then line comments, then string literals.
            string s = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            s = Regex.Replace(s, @"--[^\r\n]*", " ");
            // Replace 'text' (with '' escapes) by '~', preserving an optional leading N.
            s = Regex.Replace(s, @"'(?:[^']|'')*'", "'~'");
            return s;
        }
    }
}
