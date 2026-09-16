using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using SQLCompanion.Commands;
using SQLCompanion.Features.ColumnSearch;
using SQLCompanion.Features.DeclareVariables;
using SQLCompanion.Features.Relationships;
using Task = System.Threading.Tasks.Task;

namespace SQLCompanion
{
    /// <summary>
    /// The AsyncPackage that hosts SQL Companion inside SSMS 20.
    ///
    /// Registration attributes:
    ///   * PackageRegistration  — marks this as a VS package (background-loading).
    ///   * InstalledProductRegistration — Help > About entry.
    ///   * ProvideMenuResource  — points at the compiled .vsct command table (Menus.ctmenu).
    ///   * ProvideToolWindow    — declares the two dockable tool windows.
    ///   * ProvideAutoLoad      — auto-load so the toolbar/commands appear without opening a window
    ///                            first. UICONTEXT_NoSolution is used because SSMS has no "solution";
    ///                            this is a pragmatic auto-load trigger — see the comment below.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("SQL Companion for SSMS", "Column search, auto-declare variables, and FK relationships.", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.PackageGuidString)]
    [ProvideToolWindow(typeof(ColumnSearchToolWindow), Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Right)]
    [ProvideToolWindow(typeof(RelationshipsToolWindow), Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Right)]
    // Auto-load context.
    // NOTE (verified on SSMS 20): UICONTEXT_NoSolution does NOT fire in SSMS — SSMS is an isolated
    // shell with no solution concept, so that context never becomes "active" and the package never
    // background-loaded. UICONTEXT_ShellInitialized fires once when the shell finishes starting and
    // is the reliable trigger for isolated-shell apps, so the toolbar/menus and tool windows are
    // ready without waiting for the first command click. (The menu/toolbar entries themselves come
    // from the merged .vsct and appear even before the package loads.)
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SQLCompanionPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // Command initializers switch to the UI thread themselves as needed.
            await ShowColumnSearchCommand.InitializeAsync(this);
            await ShowRelationshipsCommand.InitializeAsync(this);
            await DeclareVariablesCommand.InitializeAsync(this);
        }
    }
}
