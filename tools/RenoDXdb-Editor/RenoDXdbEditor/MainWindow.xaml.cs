using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace RenoDXdbEditor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private ObservableCollection<GameMod> _allMods = new();
    private ICollectionView? _modsView;
    private string? _currentFilePath;
    private bool _isUpdatingFields;
    private bool _isDirty;

    // ── Bindable properties ───────────────────────────────────────────────────

    private bool _hasFile;
    public bool HasFile
    {
        get => _hasFile;
        set { _hasFile = value; OnPropertyChanged(nameof(HasFile)); }
    }

    private bool _hasSelection;
    public bool HasSelection
    {
        get => _hasSelection;
        set { _hasSelection = value; OnPropertyChanged(nameof(HasSelection)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Init ──────────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ClearEditor();
    }

    // ── File operations ───────────────────────────────────────────────────────

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard()) return;

        var dlg = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open RenoDXdb JSON",
        };
        if (dlg.ShowDialog() != true) return;

        LoadFile(dlg.FileName);
    }

    private void LoadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var mods = JsonSerializer.Deserialize<List<GameMod>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<GameMod>();

            _allMods = new ObservableCollection<GameMod>(
                mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));

            _modsView = CollectionViewSource.GetDefaultView(_allMods);
            _modsView.Filter = FilterMod;
            GameList.ItemsSource = _modsView;

            _currentFilePath = path;
            _isDirty = false;
            HasFile = true;
            UpdateStatusBar();
            UpdateCount();
            ClearEditor();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath == null) { SaveFileAs_Click(sender, e); return; }
        SaveToPath(_currentFilePath);
    }

    private void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Save RenoDXdb JSON",
            FileName = Path.GetFileName(_currentFilePath) ?? "RenoDXdb.json",
        };
        if (dlg.ShowDialog() != true) return;
        _currentFilePath = dlg.FileName;
        SaveToPath(_currentFilePath);
    }

    private void SaveToPath(string path)
    {
        try
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            var json = JsonSerializer.Serialize(_allMods.ToList(), opts);
            File.WriteAllText(path, json);
            _isDirty = false;
            StatusBar.Text = $"Saved → {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Game list operations ──────────────────────────────────────────────────

    private void NewGame_Click(object sender, RoutedEventArgs e)
    {
        var mod = new GameMod { Name = "New Game", Status = "Done" };
        InsertAlphabetically(mod);
        GameList.SelectedItem = mod;
        GameList.ScrollIntoView(mod);
        NameBox.Focus();
        NameBox.SelectAll();
        _isDirty = true;
    }

    private void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not GameMod mod) return;
        if (MessageBox.Show($"Delete \"{mod.Name}\"?", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _allMods.Remove(mod);
        ClearEditor();
        HasSelection = false;
        _isDirty = true;
        UpdateCount();
    }

    private void InsertAlphabetically(GameMod mod)
    {
        // Find insertion point
        int i = 0;
        while (i < _allMods.Count &&
               string.Compare(_allMods[i].Name, mod.Name, StringComparison.OrdinalIgnoreCase) < 0)
            i++;
        _allMods.Insert(i, mod);
        UpdateCount();
    }

    // ── Selection & editor ────────────────────────────────────────────────────

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameList.SelectedItem is GameMod mod)
        {
            HasSelection = true;
            PopulateEditor(mod);
        }
        else
        {
            HasSelection = false;
            ClearEditor();
        }
    }

    private void PopulateEditor(GameMod mod)
    {
        _isUpdatingFields = true;
        NameBox.Text = mod.Name;
        StatusCombo.SelectedIndex = mod.Status == "WIP" ? 1 : 0;
        AuthorBox.Text = mod.Author;
        SnapshotUrlBox.Text = mod.SnapshotUrl ?? "";
        SnapshotUrl32Box.Text = mod.SnapshotUrl32 ?? "";
        NexusUrlBox.Text = mod.NexusUrl ?? "";
        DiscordUrlBox.Text = mod.DiscordUrl ?? "";
        DiscussionUrlBox.Text = mod.DiscussionUrl ?? "";
        NotesBox.Text = mod.Notes ?? "";
        StatusLabel.Text = "";
        _isUpdatingFields = false;
    }

    private void ClearEditor()
    {
        _isUpdatingFields = true;
        NameBox.Text = "";
        StatusCombo.SelectedIndex = 0;
        AuthorBox.Text = "";
        SnapshotUrlBox.Text = "";
        SnapshotUrl32Box.Text = "";
        NexusUrlBox.Text = "";
        DiscordUrlBox.Text = "";
        DiscussionUrlBox.Text = "";
        NotesBox.Text = "";
        StatusLabel.Text = "";
        _isUpdatingFields = false;
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        // No-op — changes applied on Apply button
    }

    private void ApplyChanges_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not GameMod mod) return;

        var newName = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusLabel.Foreground = System.Windows.Media.Brushes.Tomato;
            StatusLabel.Text = "⚠ Game name is required.";
            return;
        }

        var nameChanged = !string.Equals(mod.Name, newName, StringComparison.Ordinal);

        mod.Name = newName;
        mod.Status = (StatusCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Done";
        mod.Author = AuthorBox.Text.Trim();
        mod.SnapshotUrl = NullIfEmpty(SnapshotUrlBox.Text);
        mod.SnapshotUrl32 = NullIfEmpty(SnapshotUrl32Box.Text);
        mod.NexusUrl = NullIfEmpty(NexusUrlBox.Text);
        mod.DiscordUrl = NullIfEmpty(DiscordUrlBox.Text);
        mod.DiscussionUrl = NullIfEmpty(DiscussionUrlBox.Text);
        mod.Notes = NullIfEmpty(NotesBox.Text);

        // Re-sort if name changed
        if (nameChanged)
        {
            _allMods.Remove(mod);
            InsertAlphabetically(mod);
            GameList.SelectedItem = mod;
            GameList.ScrollIntoView(mod);
        }

        _isDirty = true;
        StatusLabel.Foreground = System.Windows.Media.Brushes.LightGreen;
        StatusLabel.Text = "✓ Changes applied.";
        UpdateStatusBar();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _modsView?.Refresh();
        UpdateCount();
    }

    private bool FilterMod(object obj)
    {
        if (obj is not GameMod mod) return false;
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return mod.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || mod.Author.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void UpdateStatusBar()
    {
        if (_currentFilePath == null) { StatusBar.Text = "No file open"; return; }
        StatusBar.Text = $"{_currentFilePath}  ({_allMods.Count} games){(_isDirty ? "  •  Unsaved changes" : "")}";
    }

    private void UpdateCount()
    {
        var visible = _modsView?.Cast<object>().Count() ?? 0;
        CountLabel.Text = $"{visible} / {_allMods.Count}";
    }

    private bool ConfirmDiscard()
    {
        if (!_isDirty) return true;
        return MessageBox.Show("You have unsaved changes. Discard them?", "Unsaved Changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isDirty)
        {
            var result = MessageBox.Show("Save changes before closing?", "Unsaved Changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) SaveFile_Click(this, new RoutedEventArgs());
            else if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        }
        base.OnClosing(e);
    }
}
