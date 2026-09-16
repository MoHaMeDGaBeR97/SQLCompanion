using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLCompanion.Features.Relationships
{
    /// <summary>
    /// Dockable tool window hosting the FK relationships panel.
    /// GUID must match <see cref="PackageGuids.RelationshipsToolWindowGuidString"/>.
    /// </summary>
    [Guid(PackageGuids.RelationshipsToolWindowGuidString)]
    internal sealed class RelationshipsToolWindow : ToolWindowPane
    {
        public RelationshipsToolWindow() : base(null)
        {
            Caption = Strings.Captions.RelationshipsWindow;
            Content = new RelationshipsControl();
        }
    }
}
