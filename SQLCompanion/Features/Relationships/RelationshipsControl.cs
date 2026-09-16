using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    ///
    /// The table picker is a dropdown that depends on the selected database: choose a database and
    /// its base tables populate the Table dropdown. The Table combo stays editable so a
    /// schema.table can also be typed (or filled by "Use active table").
    /// </summary>
    internal sealed class RelationshipsControl : UserControl
    {
        private readonly DatabaseService _db = new DatabaseService();
        private readonly ObservableCollection<RelationshipInfo> _rels = new ObservableCollection<RelationshipInfo>();

        private readonly ComboBox _dbCombo;
        private readonly ComboBox _tableCombo;
        private readonly ComboBox _joinTypeCombo;
        private readonly DataGrid _grid;
        private readonly TextBlock _status;
        private readonly TextBlock _connectionInfo;

        private ActiveConnectionInfo _conn;
        // Guards handlers while we change combo contents/selection programmatically.
        private bool _populating;

        public RelationshipsControl()
        {
            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(6) };

            var controls = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(controls, Dock.Top);

            // Row 1: database + table dropdowns + actions + join type. A WrapPanel flows the controls
            // onto the next line when the tool window is narrow, instead of clipping off the edge.
            var row1 = new WrapPanel { Orientation = Orientation.Horizontal };

            _dbCombo = new ComboBox { Width = 180, MinWidth = 120, ToolTip = Strings.Tooltips.DatabaseDropdown };
            _dbCombo.SelectionChanged += DbCombo_SelectionChanged;
            row1.Children.Add(Pair("Database:", _dbCombo));

            _tableCombo = new ComboBox
            {
                Width = 240,
                MinWidth = 140,
                IsEditable = true,       // allow typing schema.table as well as picking
                IsTextSearchEnabled = true,
                ToolTip = Strings.Tooltips.TableDropdown
            };
            _tableCombo.SelectionChanged += TableCombo_SelectionChanged;
            // Enter in the editable text box loads the typed table.
            _tableCombo.KeyDown += (s, e) => { if (e.Key == Key.Enter) LoadRelationships(); };
            row1.Children.Add(Pair("Table:", _tableCombo));

            row1.Children.Add(MakeButton(Strings.Captions.Load, Strings.Tooltips.LoadRelationships, (s, e) => LoadRelationships()));
            row1.Children.Add(MakeButton(Strings.Captions.UseActive, Strings.Tooltips.UseActiveTable, (s, e) => UseActiveTable()));
            row1.Children.Add(MakeButton(Strings.Captions.Refresh, Strings.Tooltips.RefreshLists, (s, e) => LoadLists()));

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
            _grid.Columns.Add(new DataGridTextColumn { Header = "Direction", Binding = new System.Windows.Data.Binding(nameof(RelationshipInfo.DirectionGlyph)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Related schema", Binding = new System.Windows.Data.Binding(nameof(RelationshipInfo.OtherSchema)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Related table", Binding = new System.Windows.Data.Binding(nameof(RelationshipInfo.OtherTable)), Width = star });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Joining columns", Binding = new System.Windows.Data.Binding(nameof(RelationshipInfo.ColumnsDisplay)), Width = star });
            _grid.Columns.Add(new DataGridTextColumn { Header = "FK name", Binding = new System.Windows.Data.Binding(nameof(RelationshipInfo.ForeignKeyName)) });

            // Tooltip on each row explaining the click action.
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(ToolTipService.ToolTipProperty, Strings.Tooltips.InsertJoinRow));
            _grid.RowStyle = rowStyle;

            root.Children.Add(_grid);
            Content = root;

            // Populate the database + table dropdowns from the active connection.
            LoadLists();
        }

        // ------------------------------------------------------------------
        //  Populate the database + table dropdowns
        // ------------------------------------------------------------------

        /// <summary>Reloads the database list (and the tables for the selected database).</summary>
        private async void LoadLists()
        {
            _conn = ConnectionContext.TryGetActiveConnection();
            if (_conn == null)
            {
                _connectionInfo.Text = Strings.Messages.NoActiveConnection;
                _populating = true;
                _dbCombo.ItemsSource = null;
                _tableCombo.ItemsSource = null;
                _populating = false;
                return;
            }
            ShowConnection(_conn);

            try
            {
                List<string> dbs = await _db.GetDatabaseNamesAsync(_conn);

                _populating = true;
                _dbCombo.ItemsSource = dbs;
                if (_conn.Database != null && dbs.Contains(_conn.Database))
                    _dbCombo.SelectedItem = _conn.Database;
                else if (dbs.Count > 0)
                    _dbCombo.SelectedIndex = 0;
                _populating = false;

                await PopulateTablesAsync();
            }
            catch (Exception ex)
            {
                _status.Text = Strings.Messages.QueryFailed(ex.Message);
            }
        }

        /// <summary>Fills the Table dropdown with the base tables of the selected database.</summary>
        private async Task PopulateTablesAsync()
        {
            if (_conn == null) return;
            string db = _dbCombo.SelectedItem as string ?? _conn.Database;
            if (string.IsNullOrEmpty(db)) return;

            try
            {
                List<string> tables = await _db.GetTableNamesAsync(_conn, db);

                _populating = true;
                _tableCombo.ItemsSource = tables;
                _tableCombo.SelectedIndex = -1;
                _tableCombo.Text = string.Empty;
                _populating = false;

                _status.Text = tables.Count == 1
                    ? $"1 table in {db}."
                    : $"{tables.Count} tables in {db}.";
            }
            catch (Exception ex)
            {
                _status.Text = Strings.Messages.QueryFailed(ex.Message);
            }
        }

        private async void DbCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_populating) return;
            await PopulateTablesAsync();
        }

        private void TableCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_populating) return;
            // Only auto-load when a real item was picked from the dropdown (not on text typing).
            if (_tableCombo.SelectedItem is string)
                LoadRelationships();
        }

        // ------------------------------------------------------------------
        //  Load relationships
        // ------------------------------------------------------------------
        private async void LoadRelationships()
        {
            // WPF event handler — already on the UI thread.
            string raw = (_tableCombo.Text ?? (_tableCombo.SelectedItem as string))?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                _status.Text = Strings.Messages.EnterTableName;
                return;
            }

            if (_conn == null) _conn = ConnectionContext.TryGetActiveConnection();
            if (_conn == null)
            {
                _status.Text = Strings.Messages.NoActiveConnection;
                return;
            }
            ShowConnection(_conn);

            string db = _dbCombo.SelectedItem as string ?? _conn.Database;
            ParseTable(raw, out string schema, out string table);
            _status.Text = "Loading…";

            try
            {
                List<RelationshipInfo> rels = await _db.GetRelationshipsAsync(_conn, schema, table, db);
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
            // Point at the active connection's database so the typed table resolves there.
            var active = ConnectionContext.TryGetActiveConnection();
            if (active != null)
            {
                _conn = active;
                if (active.Database != null && _dbCombo.Items.Contains(active.Database))
                {
                    _populating = true;
                    _dbCombo.SelectedItem = active.Database;
                    _populating = false;
                }
            }
            _tableCombo.Text = token.Trim();
            LoadRelationships();
        }

        private ContextMenu BuildRowContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(MakeMenuItem("Insert JOIN at cursor", Strings.Tooltips.InsertJoinRow, InsertJoinForSelected));
            return menu;
        }

        /// <summary>Splits "schema.table" (with optional [brackets]) into parts. Schema may be null.</summary>
        private static void ParseTable(string raw, out string schema, out string table)
        {
            schema = null;
            table = raw;

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

        private static MenuItem MakeMenuItem(string header, string tooltip, Action onClick)
        {
            var mi = new MenuItem { Header = header, ToolTip = tooltip };
            mi.Click += (s, e) => onClick();
            return mi;
        }

        /// <summary>A label + control pair kept together as one unit inside a wrapping toolbar.</summary>
        private static StackPanel Pair(string label, UIElement element)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 10, 2) };
            sp.Children.Add(new Label { Content = label, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(element);
            return sp;
        }
    }
}
