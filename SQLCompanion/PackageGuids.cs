using System;

namespace SQLCompanion
{
    /// <summary>
    /// Central registry of all GUIDs and command IDs used by the extension.
    /// These MUST stay in sync with the values in SQLCompanionPackage.vsct.
    /// (The .vsct uses the string form under &lt;Symbols&gt;; C# uses the Guid form.)
    /// </summary>
    internal static class PackageGuids
    {
        // The package itself.
        public const string PackageGuidString = "0e5f2d3a-1a11-4b22-9c33-a1b2c3d4e001";

        // The command set that owns every menu, group, toolbar and button.
        public const string CommandSetGuidString = "0e5f2d3a-1a11-4b22-9c33-a1b2c3d4e002";
        public static readonly Guid CommandSet = new Guid(CommandSetGuidString);

        // Tool window GUIDs (must be unique and stable across versions).
        public const string ColumnSearchToolWindowGuidString = "0e5f2d3a-1a11-4b22-9c33-a1b2c3d4e003";
        public const string RelationshipsToolWindowGuidString = "0e5f2d3a-1a11-4b22-9c33-a1b2c3d4e004";
    }

    /// <summary>
    /// Command IDs (the numeric ids of each &lt;Button&gt; in the .vsct).
    /// Keep these identical to the IDs declared in the .vsct &lt;Symbols&gt; section.
    /// </summary>
    internal static class PackageIds
    {
        public const int ShowColumnSearchCommand = 0x0100;
        public const int ShowRelationshipsCommand = 0x0101;
        public const int DeclareVariablesCommand = 0x0102;
    }
}
