using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using SQLCompanion.Features.Relationships;
using Task = System.Threading.Tasks.Task;

namespace SQLCompanion.Commands
{
    /// <summary>Menu/toolbar command that shows the Relationships tool window.</summary>
    internal sealed class ShowRelationshipsCommand
    {
        private readonly AsyncPackage _package;

        private ShowRelationshipsCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            var id = new CommandID(PackageGuids.CommandSet, PackageIds.ShowRelationshipsCommand);
            commandService.AddCommand(new MenuCommand(Execute, id));
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
                _ = new ShowRelationshipsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                ToolWindowPane window = await _package.ShowToolWindowAsync(
                    typeof(RelationshipsToolWindow), 0, create: true, cancellationToken: _package.DisposalToken);

                if (window?.Frame == null)
                    throw new NotSupportedException("Cannot create the Relationships tool window.");
            }).FileAndForget("SQLCompanion/ShowRelationships");
        }
    }
}
