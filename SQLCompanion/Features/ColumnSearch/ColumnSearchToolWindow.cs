using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLCompanion.Features.ColumnSearch
{
    /// <summary>
    /// Dockable tool window that hosts the column-search UI.
    /// The GUID must match <see cref="PackageGuids.ColumnSearchToolWindowGuidString"/> and the
    /// ProvideToolWindow attribute on the package.
    /// </summary>
    [Guid(PackageGuids.ColumnSearchToolWindowGuidString)]
    internal sealed class ColumnSearchToolWindow : ToolWindowPane
    {
        public ColumnSearchToolWindow() : base(null)
        {
            Caption = Strings.Captions.ColumnSearchWindow;
            // ToolWindowPane hosts a WPF element directly via the Content property.
            Content = new ColumnSearchControl();
        }
    }
}
