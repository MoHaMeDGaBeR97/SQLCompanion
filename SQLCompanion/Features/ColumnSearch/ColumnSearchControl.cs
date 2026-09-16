using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using SQLCompanion.Db;
using SQLCompanion.Editor;
using Task = System.Threading.Tasks.Task;

namespace SQLCompanion.Features.ColumnSearch
{
    /// <summary>
    /// WPF UI for cross-table column search. Built entirely in code (no XAML) to keep the classic
    /// VSIX build simple. Every clickable element gets a tooltip sourced from <see cref="Strings.Tooltips"/>.
    /// </summary>
    internal sealed class ColumnSearchControl : UserControl
    {
        private readonly DatabaseService _db = new DatabaseService();

        // Backing collection + a view that supports schema filtering and column sorting.
        private readonly ObservableCollection<ColumnResult> _all = new ObservableCollection<ColumnResult>();
        private readonly ListCollectionView _view;

        // UI elements referenced by handlers.
        private readonly TextBox _searchBox;
        private readonly ComboBox _scopeCombo;
        private readonly ComboBox _schemaCombo;
        private readonly ComboBox _recentCombo;
        private readonly DataGrid _grid;
        private readonly TextBlock _status;
        private readonly TextBlock _connectionInfo;

        private readonly ObservableCollection<string> _recentSearches = new ObservableCollection<string>();
        private bool _suppressRecentEvent;

        public ColumnSearchControl()
        {
            _view = new ListCollectionView(_all);

            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(6) };

            // ---- Toolbar rows -------------------------------------------------
            var controls = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(controls, Dock.Top);

            // Row 1: search box + search button + scope. A WrapPanel lets the controls flow onto the
            // next line when the tool window is docked narrow, instead of clipping off the right edge.
            var row1 = new WrapPanel { Orientation = Orientation.Horizontal };
            _searchBox = new TextBox
            {
                Width = 220,
                MinWidth = 120,
                ToolTip = Strings.Tooltips.SearchBox,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _searchBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) RunSearch(); };
            row1.Children.Add(Pair("Column:", _searchBox));
            row1.Children.Add(MakeButton(Strings.Captions.Search, Strings.Tooltips.RunSearch, (s, e) => RunSearch()));

            _scopeCombo = new ComboBox
            {
                Width = 170,
                MinWidth = 120,
                ToolTip = Strings.Tooltips.ScopeToggle
            };
            _scopeCombo.Items.Add(Strings.Captions.ScopeCurrentDb);
            _scopeCombo.Items.Add(Strings.Captions.ScopeAllDbs);
            _scopeCombo.SelectedIndex = 0;
            row1.Children.Add(Pair("Scope:", _scopeCombo));

            controls.Children.Add(row1);

            // Row 2: schema filter + recent searches + export/copy/refresh (also wraps when narrow).
            var row2 = new WrapPanel { Orientation = Orientation.Horizontal };

            _schemaCombo = new ComboBox { Width = 150, MinWidth = 110, ToolTip = Strings.Tooltips.SchemaFilter };
            _schemaCombo.SelectionChanged += (s, e) => ApplySchemaFilter();
            row2.Children.Add(Pair("Schema:", _schemaCombo));

            _recentCombo = new ComboBox
            {
                Width = 160,
                MinWidth = 110,
                ToolTip = Strings.Tooltips.RecentSearches,
                ItemsSource = _recentSearches
            };
            _recentCombo.SelectionChanged += RecentCombo_SelectionChanged;
            row2.Children.Add(Pair("Recent:", _recentCombo));

            row2.Children.Add(MakeButton(Strings.Captions.Export, Strings.Tooltips.ExportCsv, (s, e) => ExportCsv()));
            row2.Children.Add(MakeButton(Strings.Captions.CopyAll, Strings.Tooltips.CopyAllToClipboard, (s, e) => CopyAll()));
            row2.Children.Add(MakeButton(Strings.Captions.Refresh, Strings.Tooltips.RefreshConnection, (s, e) => RefreshConnection()));

            controls.Children.Add(row2);

            // Row 3: connection + status text. Wrap so long messages don't get cut off when narrow.
            _connectionInfo = new TextBlock { Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            controls.Children.Add(_connectionInfo);
            controls.Children.Add(_status);

            root.Children.Add(controls);

            // ---- Results grid -------------------------------------------------
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Extended,
                ItemsSource = _view,
                // Fill the available width; overflow scrolls horizontally instead of clipping.
                ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Auto),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _grid.MouseDoubleClick += Grid_MouseDoubleClick;
            _grid.ContextMenu = BuildRowContextMenu();

            var star = new DataGridLength(1, DataGridLengthUnitType.Star);
            _grid.Columns.Add(new DataGridTextColumn { Header = "Database", Binding = new Binding(nameof(ColumnResult.DatabaseName)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Schema", Binding = new Binding(nameof(ColumnResult.Schema)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Table/View", Binding = new Binding(nameof(ColumnResult.TableName)), Width = star });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(ColumnResult.ObjectType)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Column", Binding = new Binding(nameof(ColumnResult.ColumnName)), Width = star });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Data Type", Binding = new Binding(nameof(ColumnResult.DataType)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Nullable", Binding = new Binding(nameof(ColumnResult.Nullable)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Key", Binding = new Binding(nameof(ColumnResult.KeyInfo)) });

            root.Children.Add(_grid);

            Content = root;

            // Show initial connection status.
            RefreshConnection();
        }

        // ------------------------------------------------------------------
        //  Search
        // ------------------------------------------------------------------
        private async void RunSearch()
        {
            // This is a WPF event handler, so we are already on the UI thread.
            string term = _searchBox.Text?.Trim();
            if (string.IsNullOrEmpty(term))
            {
                _status.Text = Strings.Messages.EnterSearchText;
                return;
            }

            var conn = ConnectionContext.TryGetActiveConnection();
            if (conn == null)
            {
                _status.Text = Strings.Messages.NoActiveConnection;
                return;
            }
            ShowConnection(conn);

            bool allDbs = _scopeCombo.SelectedIndex == 1;
            _status.Text = "Searching…";

            try
            {
                // Run the DB work off the UI thread; await resumes back on the UI thread.
                List<ColumnResult> results = await Task.Run(() => _db.SearchColumnsAsync(conn, term, allDbs));

                _all.Clear();
                foreach (var r in results) _all.Add(r);

                PopulateSchemaFilter();
                ApplySchemaFilter();
                AddRecentSearch(term);

                _status.Text = results.Count == 0 ? Strings.Messages.NoResults : Strings.Messages.SearchStatus(results.Count);
            }
            catch (Exception ex)
            {
                _status.Text = Strings.Messages.QueryFailed(ex.Message);
            }
        }

        private void PopulateSchemaFilter()
        {
            var current = _schemaCombo.SelectedItem as string;
            var schemas = _all.Select(r => r.Schema).Distinct().OrderBy(s => s).ToList();

            _schemaCombo.Items.Clear();
            _schemaCombo.Items.Add(Strings.Captions.AllSchemas);
            foreach (var s in schemas) _schemaCombo.Items.Add(s);

            _schemaCombo.SelectedItem = (current != null && _schemaCombo.Items.Contains(current))
                ? current : Strings.Captions.AllSchemas;
        }

        private void ApplySchemaFilter()
        {
            string schema = _schemaCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(schema) || schema == Strings.Captions.AllSchemas)
                _view.Filter = null;
            else
                _view.Filter = o => (o as ColumnResult)?.Schema == schema;
            _view.Refresh();
        }

        // ------------------------------------------------------------------
        //  Recent searches
        // ------------------------------------------------------------------
        private void AddRecentSearch(string term)
        {
            _recentSearches.Remove(term);
            _recentSearches.Insert(0, term);
            while (_recentSearches.Count > 10) _recentSearches.RemoveAt(_recentSearches.Count - 1);
        }

        private void RecentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRecentEvent) return;
            if (_recentCombo.SelectedItem is string term)
            {
                _searchBox.Text = term;
                RunSearch();
                // Reset selection so the same item can be picked again later.
                _suppressRecentEvent = true;
                _recentCombo.SelectedIndex = -1;
                _suppressRecentEvent = false;
            }
        }

        // ------------------------------------------------------------------
        //  Row actions
        // ------------------------------------------------------------------
        private ContextMenu BuildRowContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(MakeMenuItem("Insert table name", Strings.Tooltips.InsertQualifiedTable,
                () => WithSelected(r => EditorService.InsertAtCursor(r.QualifiedTable))));
            menu.Items.Add(MakeMenuItem("Insert column name", Strings.Tooltips.InsertColumnName,
                () => WithSelected(r => EditorService.InsertAtCursor(r.ColumnName))));
            menu.Items.Add(MakeMenuItem("Generate SELECT", Strings.Tooltips.GenerateSelect,
                () => WithSelected(r => EditorService.InsertAtCursor($"SELECT TOP 100 * FROM {r.QualifiedTable}"))));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Copy column name", Strings.Tooltips.CopyColumnName,
                () => WithSelected(r => TrySetClipboard(r.ColumnName))));
            menu.Items.Add(MakeMenuItem("Copy table name", Strings.Tooltips.CopyQualifiedTable,
                () => WithSelected(r => TrySetClipboard(r.QualifiedTable))));
            return menu;
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Double-click inserts the fully-qualified schema.table at the cursor.
            ThreadHelper.ThrowIfNotOnUIThread();
            WithSelected(r =>
            {
                if (!EditorService.InsertAtCursor(r.QualifiedTable))
                    _status.Text = Strings.Messages.NoActiveEditor;
            });
        }

        private void WithSelected(Action<ColumnResult> action)
        {
            if (_grid.SelectedItem is ColumnResult r)
                action(r);
        }

        // ------------------------------------------------------------------
        //  Export / copy
        // ------------------------------------------------------------------
        private void ExportCsv()
        {
            if (_view.Count == 0) { _status.Text = Strings.Messages.NoResults; return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "column-search.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                System.IO.File.WriteAllText(dlg.FileName, BuildDelimited(",", quote: true), Encoding.UTF8);
                _status.Text = $"Exported {_view.Count} rows to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                _status.Text = "Export failed: " + ex.Message;
            }
        }

        private void CopyAll()
        {
            if (_view.Count == 0) { _status.Text = Strings.Messages.NoResults; return; }
            if (TrySetClipboard(BuildDelimited("\t", quote: false)))
                _status.Text = $"Copied {_view.Count} rows to the clipboard.";
        }

        private string BuildDelimited(string sep, bool quote)
        {
            var sb = new StringBuilder();
            string[] headers = { "Database", "Schema", "Table/View", "Type", "Column", "DataType", "Nullable", "Key" };
            sb.AppendLine(string.Join(sep, headers.Select(h => Field(h, sep, quote))));

            foreach (ColumnResult r in _view)
            {
                string[] cells =
                {
                    r.DatabaseName, r.Schema, r.TableName, r.ObjectType,
                    r.ColumnName, r.DataType, r.Nullable, r.KeyInfo
                };
                sb.AppendLine(string.Join(sep, cells.Select(c => Field(c, sep, quote))));
            }
            return sb.ToString();
        }

        private static string Field(string value, string sep, bool quote)
        {
            value = value ?? string.Empty;
            if (!quote) return value;
            if (value.Contains("\"") || value.Contains(sep) || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        // ------------------------------------------------------------------
        //  Connection status
        // ------------------------------------------------------------------
        private void RefreshConnection()
        {
            var conn = ConnectionContext.TryGetActiveConnection();
            if (conn == null)
            {
                _connectionInfo.Text = Strings.Messages.NoActiveConnection;
                return;
            }
            ShowConnection(conn);
        }

        private void ShowConnection(ActiveConnectionInfo conn)
            => _connectionInfo.Text = Strings.Messages.Connected(conn.Server, conn.Database);

        // ------------------------------------------------------------------
        //  Small UI helpers
        // ------------------------------------------------------------------
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

        private bool TrySetClipboard(string text)
        {
            try { Clipboard.SetText(text ?? string.Empty); return true; }
            catch (Exception ex) { _status.Text = "Clipboard error: " + ex.Message; return false; }
        }
    }
}
