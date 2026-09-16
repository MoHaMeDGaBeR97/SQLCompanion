using System;
using System.ComponentModel.Design;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLCompanion.Editor;
using Task = System.Threading.Tasks.Task;

namespace SQLCompanion.Features.DeclareVariables
{
    /// <summary>
    /// Wires the "Declare variables" command, which is exposed BOTH on the SQL Companion toolbar and
    /// on the query-editor right-click context menu (see the .vsct). On execute it scans the active
    /// batch, finds @variables used without a DECLARE, infers their types, and inserts the DECLARE
    /// statements at the top of the current batch — without disturbing existing formatting.
    /// </summary>
    internal sealed class DeclareVariablesCommand
    {
        private readonly AsyncPackage _package;

        private DeclareVariablesCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            var id = new CommandID(PackageGuids.CommandSet, PackageIds.DeclareVariablesCommand);
            var cmd = new OleMenuCommand(Execute, id);
            cmd.BeforeQueryStatus += (s, e) =>
            {
                // Enable only when a query editor is active, so the button/menu greys out otherwise.
                ThreadHelper.ThrowIfNotOnUIThread();
                ((OleMenuCommand)s).Enabled = EditorService.HasActiveEditor();
            };
            commandService.AddCommand(cmd);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
                _ = new DeclareVariablesCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string sql = EditorService.GetActiveText();
            if (sql == null)
            {
                ShowInfo(Strings.Messages.NoActiveEditor);
                return;
            }

            var undeclared = DeclareVariableGenerator.FindUndeclared(sql);
            if (undeclared.Count == 0)
            {
                ShowInfo(Strings.Messages.DeclareNothingToDo);
                return;
            }

            string newline = sql.Contains("\r\n") ? "\r\n" : "\n";
            string block = DeclareVariableGenerator.BuildDeclareBlock(undeclared, newline);

            int batchStart = FindBatchStartLine(sql, EditorService.GetCursorLine());
            if (!EditorService.InsertAtLineStart(batchStart, block))
            {
                ShowInfo(Strings.Messages.NoActiveEditor);
                return;
            }

            SetStatus(Strings.Messages.DeclareAdded(undeclared.Count));
        }

        /// <summary>
        /// Finds the 1-based line at the top of the batch containing <paramref name="cursorLine"/>,
        /// i.e. the line just after the nearest preceding "GO" batch separator (or line 1).
        /// </summary>
        private static int FindBatchStartLine(string sql, int cursorLine)
        {
            string[] lines = sql.Replace("\r\n", "\n").Split('\n');
            var goLine = new Regex(@"^\s*GO\s*(--.*)?$", RegexOptions.IgnoreCase);

            // Search upward from the cursor for a GO separator.
            int idx = Math.Min(cursorLine, lines.Length) - 1; // 0-based
            for (int i = idx; i >= 0; i--)
            {
                if (goLine.IsMatch(lines[i]))
                    return i + 2; // line after the GO, converted back to 1-based
            }
            return 1;
        }

        private void ShowInfo(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                _package, message, Strings.ProductName,
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private void SetStatus(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var statusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
            statusBar?.SetText(message);
        }
    }
}
