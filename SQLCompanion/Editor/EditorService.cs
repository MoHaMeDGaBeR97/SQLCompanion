using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace SQLCompanion.Editor
{
    /// <summary>
    /// Thin wrapper over the active query editor using the DTE automation model
    /// (ActiveDocument -> TextDocument -> TextSelection). This works for SSMS query
    /// windows because they are standard text editors.
    ///
    /// NOTE (alternative API): the lower-level IVsTextManager/IVsTextView route
    /// (GetActiveView -> IVsTextLines) is more robust to some edge cases but far more
    /// verbose. DTE is used here for clarity; swap the internals if you hit a case where
    /// DTE returns null in your SSMS 20 build.
    ///
    /// ALL methods must be called on the UI thread.
    /// </summary>
    internal static class EditorService
    {
        /// <summary>Returns the active TextDocument, or null if there is no active text editor.</summary>
        private static TextDocument GetActiveTextDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // GetGlobalService is fine here; the package also exposes DTE, but this keeps the
            // service self-contained for the editor commands.
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            var doc = dte?.ActiveDocument;
            if (doc == null) return null;

            // "TextDocument" is the well-known key for the text model of a text editor document.
            return doc.Object("TextDocument") as TextDocument;
        }

        /// <summary>True when a query editor is active and writable.</summary>
        public static bool HasActiveEditor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetActiveTextDocument() != null;
        }

        /// <summary>Inserts text at the current cursor position (replacing any selection).</summary>
        public static bool InsertAtCursor(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var td = GetActiveTextDocument();
            if (td == null) return false;
            var sel = (TextSelection)td.Selection;
            sel.Insert(text);
            return true;
        }

        /// <summary>Returns the full text of the active document, or null if unavailable.</summary>
        public static string GetActiveText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var td = GetActiveTextDocument();
            if (td == null) return null;
            var start = td.StartPoint.CreateEditPoint();
            return start.GetText(td.EndPoint);
        }

        /// <summary>1-based line number of the cursor, or 1 if unavailable.</summary>
        public static int GetCursorLine()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var td = GetActiveTextDocument();
            if (td == null) return 1;
            return ((TextSelection)td.Selection).ActivePoint.Line;
        }

        /// <summary>Inserts <paramref name="text"/> at the very start of the given 1-based line.</summary>
        public static bool InsertAtLineStart(int oneBasedLine, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var td = GetActiveTextDocument();
            if (td == null) return false;
            var ep = td.StartPoint.CreateEditPoint();
            ep.MoveToLineAndOffset(oneBasedLine, 1);
            ep.Insert(text);
            return true;
        }

        /// <summary>
        /// Returns the current selection if non-empty; otherwise the "word" under the cursor.
        /// Used by the relationships panel's "Use active table" action. The returned text may be
        /// a bare table name or a schema.table token — the caller parses it.
        /// </summary>
        public static string GetSelectionOrWordUnderCursor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var td = GetActiveTextDocument();
            if (td == null) return null;

            var sel = (TextSelection)td.Selection;
            if (!string.IsNullOrWhiteSpace(sel.Text))
                return sel.Text.Trim();

            // No selection: grab the identifier around the caret. We expand left/right over
            // characters valid in a (possibly schema-qualified, possibly bracketed) identifier.
            var point = sel.ActivePoint;
            var lineStart = point.CreateEditPoint();
            lineStart.StartOfLine();
            var lineEnd = point.CreateEditPoint();
            lineEnd.EndOfLine();
            string line = lineStart.GetText(lineEnd);
            int caretCol = point.LineCharOffset - 1; // 0-based within the line

            return ExtractIdentifierAt(line, caretCol);
        }

        /// <summary>Expands around <paramref name="index"/> to capture a SQL identifier token.</summary>
        private static string ExtractIdentifierAt(string line, int index)
        {
            if (string.IsNullOrEmpty(line)) return null;
            if (index < 0) index = 0;
            if (index >= line.Length) index = line.Length - 1;
            if (index < 0) return null;

            bool IsIdentChar(char c) =>
                char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '[' || c == ']' || c == '@';

            if (!IsIdentChar(line[index]))
            {
                // Caret sits on a separator; nudge left onto the preceding token if possible.
                if (index > 0 && IsIdentChar(line[index - 1])) index--;
                else return null;
            }

            int start = index;
            while (start > 0 && IsIdentChar(line[start - 1])) start--;
            int end = index;
            while (end < line.Length - 1 && IsIdentChar(line[end + 1])) end++;

            return line.Substring(start, end - start + 1).Trim();
        }
    }
}
