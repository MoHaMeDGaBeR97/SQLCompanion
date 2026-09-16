using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using SQLCompanion.Db;
using SQLCompanion.Editor;
using Task = System.Threading.Tasks.Task;

namespace SQLCompanion.Features.Relationships
{
    /// <summary>
    /// WPF UI for the foreign-key relationships panel. Shows both directions (references and
    /// referenced-by) for a table, and inserts a ready-made JOIN when a row is activated.
    /// Built in code (no XAML). Every clickable element has a tooltip.
    /// </summary>
    internal sealed class RelationshipsControl : UserControl
    {
        private readonly DatabaseService _db = new DatabaseService();
        private readonly ObservableCollection<RelationshipInfo> _rels = new ObservableCollection<RelationshipInfo>();

        private readonly TextBox _tableBox;
        private readonly ComboBox _joinTypeCombo;
        private readonly DataGrid _grid;
        private readonly TextBlock _status;
        private readonly TextBlock _connectionInfo;

        public RelationshipsControl()
        {
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(6) };

            var controls = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(controls, Dock.Top);

            // Row 1: table input + load + use-active + join type. A WrapPanel flows the controls onto
            // the next line when the tool window is narrow, instead of clipping off the right edge.
            var row1 = new WrapPanel { Orientation = Orientation.Horizontal };
            _tableBox = new TextBox
            {
                Width = 200,
                MinWidth = 120,
                ToolTip = Strings.Tooltips.RelationshipsTableBox,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _tableBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) LoadRelationships(); };
            row1.Children.Add(Pair("Table:", _tableBox));
            row1.Children.Add(MakeButton(Strings.Captions.Load, Strings.Tooltips.LoadRelationships, (s, e) => LoadRelationships()));
            row1.Children.Add(MakeButton(Strings.Captions.UseActive, Strings.Tooltips.UseActiveTable, (s, e) => UseActiveTable()));

            _joinTypeCombo = new ComboBox { Width = 90, MinWidth = 80, ToolTip = Strings.Tooltips.JoinTypeDropdown };
            _joinTypeCombo.Items.Add("INNER");
            _joinTypeCombo.Items.Add("LEFT");
            _joinTypeCombo.SelectedIndex = 0;
            row1.Children.Add(Pair("Join:", _joinTypeCombo));

            controls.Children.Add(row1);

            _connectionInfo = new TextBlock { Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            controls.Children.Add(_connectionInfo);
            controls.Children.Add(_status);

            root.Children.Add(controls);

            // Results grid.
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = _rels,
                // Fill the available width; overflow scrolls horizontally instead of clipping.
                ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Auto),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _grid.MouseDoubleClick += (s, e) => InsertJoinForSelected();
            _grid.ContextMenu = BuildRowContextMenu();

            var star = new DataGridLength(1, DataGridLengthUnitType.Star);
            _grid.Columns.Add(new DataGridTextColumn { Header = "Direction", Binding = new Binding(nameof(RelationshipInfo.DirectionGlyph)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Related schema", Binding = new Binding(nameof(RelationshipInfo.OtherSchema)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Related table", Binding = new Binding(nameof(RelationshipInfo.OtherTable)), Width = star });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Joining columns", Binding = new Binding(nameof(RelationshipInfo.ColumnsDisplay)), Width = star });
            _grid.Columns.Add(new DataGridTextColumn { Header = "FK name", Binding = new Binding(nameof(RelationshipInfo.ForeignKeyName)) });

            // Tooltip on each row explaining the click action.
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(ToolTipService.ToolTipProperty, Strings.Tooltips.InsertJoinRow));
            _grid.RowStyle = rowStyle;

            root.Children.Add(_grid);
            Content = root;

            RefreshConnection();
        }

        private ContextMenu BuildRowContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(MakeMenuItem("Insert JOIN at cursor", Strings.Tooltips.InsertJoinRow, InsertJoinForSelected));
            return menu;
        }

        // ------------------------------------------------------------------
        //  Load relationships
        // ------------------------------------------------------------------
        private async void LoadRelationships()
        {
            // WPF event handler — already on the UI thread.
            string raw = _tableBox.Text?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                _status.Text = Strings.Messages.EnterTableName;
                return;
            }

            var conn = ConnectionContext.TryGetActiveConnection();
            if (conn == null)
            {
                _status.Text = Strings.Messages.NoActiveConnection;
                return;
            }
            ShowConnection(conn);

            ParseTable(raw, out string schema, out string table);
            _status.Text = "Loading…";

            try
            {
                List<RelationshipInfo> rels = await Task.Run(() => _db.GetRelationshipsAsync(conn, schema, table));
                _rels.Clear();
                foreach (var r in rels) _rels.Add(r);
                _status.Text = rels.Count == 0
                    ? Strings.Messages.NoRelationships
                    : $"{rels.Count} relationship(s) found.";
            }
            catch (Exception ex)
            {
                _status.Text = Strings.Messages.QueryFailed(ex.Message);
            }
        }

        private void UseActiveTable()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string token = EditorService.GetSelectionOrWordUnderCursor();
            if (string.IsNullOrWhiteSpace(token))
            {
                _status.Text = Strings.Messages.NoActiveEditor;
                return;
            }
            _tableBox.Text = token.Trim();
            LoadRelationships();
        }

        /// <summary>Splits "schema.table" (with optional [brackets]) into parts. Schema may be null.</summary>
        private static void ParseTable(string raw, out string schema, out string table)
        {
            schema = null;
            table = raw;

            // Strip surrounding whitespace already done. Handle bracketed identifiers.
            var parts = SplitQualified(raw);
            if (parts.Count >= 2)
            {
                schema = Unbracket(parts[parts.Count - 2]);
                table = Unbracket(parts[parts.Count - 1]);
            }
            else if (parts.Count == 1)
            {
                table = Unbracket(parts[0]);
            }
        }

        private static List<string> SplitQualified(string raw)
        {
            // Split on '.' but not inside [ ].
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inBracket = false;
            foreach (char c in raw)
            {
                if (c == '[') inBracket = true;
                else if (c == ']') inBracket = false;

                if (c == '.' && !inBracket)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }

        private static string Unbracket(string s)
            => s?.Trim().TrimStart('[').TrimEnd(']').Trim();

        // ------------------------------------------------------------------
        //  Insert JOIN
        // ------------------------------------------------------------------
        private void InsertJoinForSelected()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(_grid.SelectedItem is RelationshipInfo rel)) return;

            var kind = _joinTypeCombo.SelectedIndex == 1 ? JoinKind.Left : JoinKind.Inner;
            // "t" is the assumed alias of the focus/base table in the user's FROM clause.
            // Documented assumption: the base table is aliased "t"; the user can rename after paste.
            string clause = JoinClauseBuilder.Build(rel, focusAlias: "t", kind: kind);

            if (!EditorService.InsertAtCursor(Environment.NewLine + clause))
                _status.Text = Strings.Messages.NoActiveEditor;
            else
                _status.Text = "JOIN inserted.";
        }

        // ------------------------------------------------------------------
        //  Connection status + helpers
        // ------------------------------------------------------------------
        private void RefreshConnection()
        {
            var conn = ConnectionContext.TryGetActiveConnection();
            _connectionInfo.Text = conn == null
                ? Strings.Messages.NoActiveConnection
                : Strings.Messages.Connected(conn.Server, conn.Database);
        }

        private void ShowConnection(ActiveConnectionInfo conn)
            => _connectionInfo.Text = Strings.Messages.Connected(conn.Server, conn.Database);

        private static Button MakeButton(string caption, string tooltip, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = caption,
                ToolTip = tooltip,
                Margin = new Thickness(0, 2, 6, 2),
                Padding = new Thickness(8, 2, 8, 2)
            };
            b.Click += onClick;
            return b;
        }

        /// <summary>A label + control pair kept together as one unit inside a wrapping toolbar.</summary>
        private static StackPanel Pair(string label, UIElement element)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 10, 2) };
            sp.Children.Add(new Label { Content = label, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(element);
            return sp;
        }

        private static MenuItem MakeMenuItem(string header, string tooltip, Action onClick)
        {
            var mi = new MenuItem { Header = header, ToolTip = tooltip };
            mi.Click += (s, e) => onClick();
            return mi;
        }
    }
}
