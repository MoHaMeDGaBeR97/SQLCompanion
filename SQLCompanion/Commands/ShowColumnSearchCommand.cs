using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using SQLCompanion.Features.ColumnSearch;
using Task = System.Threading.Tasks.Task;

namespace SQLCompanion.Commands
{
    /// <summary>Menu/toolbar command that shows the Column Search tool window.</summary>
    internal sealed class ShowColumnSearchCommand
    {
        private readonly AsyncPackage _package;

        private ShowColumnSearchCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            var id = new CommandID(PackageGuids.CommandSet, PackageIds.ShowColumnSearchCommand);
            commandService.AddCommand(new MenuCommand(Execute, id));
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
                _ = new ShowColumnSearchCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                // FindToolWindow creates the window if it doesn't exist yet (create: true).
                ToolWindowPane window = await _package.ShowToolWindowAsync(
                    typeof(ColumnSearchToolWindow), 0, create: true, cancellationToken: _package.DisposalToken);

                if (window?.Frame == null)
                    throw new NotSupportedException("Cannot create the Column Search tool window.");
            }).FileAndForget("SQLCompanion/ShowColumnSearch");
        }
    }
}
