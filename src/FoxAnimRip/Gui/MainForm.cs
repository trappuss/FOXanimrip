// SPDX-License-Identifier: MIT
namespace FoxAnimRip.Gui;

/// <summary>
/// The whole window. Four numbered steps top to bottom: game, characters,
/// animations, destination.
///
/// This is deliberately a thin shell. Everything that decides anything lives in
/// FoxAnimRip.Core and runs headlessly, so the parts that can be wrong are the
/// parts that can be tested.
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>One animation set, and which of the chosen characters it fits.</summary>
    private sealed class SetRow
    {
        public CatalogEntry Entry;
        public int Clips;
        public readonly Dictionary<string, MtarMatch> Fits =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Best bone count seen, whether or not it cleared the threshold.</summary>
        public int BestBones;

        /// <summary>Why the set could not be read at all, if it could not.</summary>
        public string Error = "";

        /// <summary>Ticked by the user. Kept on the row so filtering the list
        /// cannot silently drop a choice that is scrolled out of view.</summary>
        public bool Ticked;

        public bool Fitting => Fits.Count > 0;

        public string Label(int characterCount)
        {
            var clips = $"{Clips} clip{(Clips == 1 ? "" : "s")}";
            if (Error.Length > 0)
                return $"{Entry.Stem}   -   could not be read ({Error})";
            if (!Fitting)
                return $"{Entry.Stem}   -   {clips}, does not fit "
                     + $"({BestBones} bone{(BestBones == 1 ? "" : "s")} matched)";
            if (characterCount <= 1)
            {
                var bones = Fits.Values.FirstOrDefault()?.MatchedBones ?? 0;
                return $"{Entry.Stem}   -   {clips}, {bones} bones matched";
            }
            return $"{Entry.Stem}   -   {clips}, fits {Fits.Count} of {characterCount}";
        }

        /// <summary>The "Fits" column: the verdict on its own, name and counts
        /// having their own columns now.</summary>
        public string Verdict(int characterCount)
        {
            if (Error.Length > 0) return "could not be read — " + Error;
            if (!Fitting) return "does not fit";
            return characterCount <= 1
                ? "fits"
                : $"fits {Fits.Count} of {characterCount}";
        }
    }

    // -- state
    private string _fbPath = "";
    private GameInstall _install;
    private GameCatalog _catalog;
    private readonly List<CatalogEntry> _chosenModels = new();
    private string _modelPath = "";
    private readonly List<ModelContext> _contexts = new();
    private readonly List<SetRow> _sets = new();
    private CancellationTokenSource _cancel;
    private bool _busy;
    private ThemeMode _theme = ThemeMode.Dark;
    private List<CatalogEntry> _visibleModels = new();

    // -- controls
    private readonly ComboBox _gameBox = new();
    private readonly Label _gamePath = new();
    private readonly Button _gameBrowse = new();
    private readonly Button _scanButton = new();

    private readonly TextBox _modelSearch = new();
    private readonly ListBox _modelList = new();
    private readonly Button _modelAll = new();
    private readonly Button _modelNone = new();
    private readonly Button _modelFile = new();
    private readonly Label _modelInfo = new();

    private readonly RadioButton _setsAll = new();
    private readonly RadioButton _setsPick = new();
    private readonly Button _findSets = new();
    // A ListView rather than a CheckedListBox: that control only ever supports
    // one selected row -- it throws on MultiExtended -- so ticking twenty sets
    // meant twenty individual clicks. This also affords real columns.
    private readonly ListView _setList = new();
    private bool _bulkTicking;
    private readonly TextBox _clipFilter = new();
    private readonly TextBox _setSearch = new();
    private readonly CheckBox _setsShowAll = new();
    private readonly List<SetRow> _shownSets = new();

    private readonly TextBox _outBox = new();
    private readonly Button _outBrowse = new();

    private readonly Button _exportButton = new();
    private readonly Button _previewButton = new();
    private readonly Button _cancelButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new();

    private readonly CheckBox _withMesh = new();
    private readonly CheckBox _keepStatic = new();
    private readonly CheckBox _dedupe = new();
    private readonly CheckBox _exportModel = new();
    private readonly NumericUpDown _pack = new();
    private readonly NumericUpDown _minMatch = new();
    private readonly NumericUpDown _step = new();
    private readonly ComboBox _themeBox = new();
    private readonly LinkLabel _fbLink = new();

    public MainForm()
    {
        Text = $"foxanimrip {AppVersion.Current} - bulk animation export for Fox Engine games";
        MinimumSize = new Size(880, 720);
        Size = new Size(960, 840);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 1 game
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); // 2 characters
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); // 3 animations
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 4 output
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // advanced
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // buttons
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        Controls.Add(root);

        root.Controls.Add(BuildGameStep(), 0, 0);
        root.Controls.Add(BuildModelStep(), 0, 1);
        root.Controls.Add(BuildSetsStep(), 0, 2);
        root.Controls.Add(BuildOutputStep(), 0, 3);
        root.Controls.Add(BuildAdvanced(), 0, 4);
        root.Controls.Add(BuildButtons(), 0, 5);
        root.Controls.Add(BuildLog(), 0, 6);

        Load += (_, _) => BeginInvoke(new Action(FirstRun));
        FormClosing += OnFormClosing;
    }

    // -- layout ------------------------------------------------------------

    private static GroupBox Step(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Fill,
        Padding = new Padding(10, 6, 10, 10),
        Margin = new Padding(0, 0, 0, 8),
    };

    private Control BuildGameStep()
    {
        var box = Step("1.  Which game");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, AutoSize = true,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _gameBox.Dock = DockStyle.Fill;
        _gameBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _gameBox.SelectedIndexChanged += (_, _) => OnGameChanged();

        _gameBrowse.Text = "Browse...";
        _gameBrowse.AutoSize = true;
        _gameBrowse.Click += (_, _) => BrowseForGame();

        _scanButton.Text = "Scan game files";
        _scanButton.AutoSize = true;
        _scanButton.Click += (_, _) => _ = ScanAsync(force: true);

        _gamePath.Dock = DockStyle.Fill;
        _gamePath.AutoEllipsis = true;
        _gamePath.ForeColor = SystemColors.GrayText;
        _gamePath.Text = "Looking for installed games...";

        grid.Controls.Add(_gameBox, 0, 0);
        grid.Controls.Add(_gameBrowse, 1, 0);
        grid.Controls.Add(_scanButton, 2, 0);
        grid.Controls.Add(_gamePath, 0, 1);
        grid.SetColumnSpan(_gamePath, 3);
        box.Controls.Add(grid);
        return box;
    }

    private Control BuildModelStep()
    {
        var box = Step("2.  Which characters   (pick one, or ctrl-click for several)");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _modelSearch.Dock = DockStyle.Fill;
        _modelSearch.PlaceholderText = "Type to search, e.g. sna2";
        _modelSearch.TextChanged += (_, _) => RefreshModelList();

        _modelAll.Text = "Select all shown";
        _modelAll.AutoSize = true;
        _modelAll.Click += (_, _) => SelectAllShown(true);

        _modelNone.Text = "Clear";
        _modelNone.AutoSize = true;
        _modelNone.Click += (_, _) => SelectAllShown(false);

        _modelFile.Text = "Use a .fmdl file...";
        _modelFile.AutoSize = true;
        _modelFile.Click += (_, _) => BrowseForModel();

        _modelList.Dock = DockStyle.Fill;
        _modelList.IntegralHeight = false;
        _modelList.SelectionMode = SelectionMode.MultiExtended;
        _modelList.SelectedIndexChanged += (_, _) => OnModelSelectionChanged();

        _modelInfo.Dock = DockStyle.Fill;
        _modelInfo.AutoEllipsis = true;
        _modelInfo.ForeColor = SystemColors.GrayText;
        _modelInfo.Text = "Scan the game files to list characters.";

        grid.Controls.Add(_modelSearch, 0, 0);
        grid.Controls.Add(_modelAll, 1, 0);
        grid.Controls.Add(_modelNone, 2, 0);
        grid.Controls.Add(_modelFile, 3, 0);
        grid.Controls.Add(_modelList, 0, 1);
        grid.SetColumnSpan(_modelList, 4);
        grid.Controls.Add(_modelInfo, 0, 2);
        grid.SetColumnSpan(_modelInfo, 4);
        box.Controls.Add(grid);
        return box;
    }

    private Control BuildSetsStep()
    {
        var box = Step("3.  Which animations");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _setsAll.Text = "Everything that fits each character";
        _setsAll.AutoSize = true;
        _setsAll.Checked = true;
        _setsAll.CheckedChanged += (_, _) => UpdateEnabled();

        _setsPick.Text = "Only the sets I tick below";
        _setsPick.AutoSize = true;
        _setsPick.CheckedChanged += (_, _) => UpdateEnabled();

        _findSets.Text = "Find animation sets";
        _findSets.AutoSize = true;
        _findSets.Click += (_, _) => _ = FindSetsAsync();

        _setList.Dock = DockStyle.Fill;
        _setList.View = View.Details;
        _setList.CheckBoxes = true;
        _setList.MultiSelect = true;
        _setList.FullRowSelect = true;
        _setList.HideSelection = false;
        _setList.Columns.Add("Animation set", 260);
        _setList.Columns.Add("Clips", 60);
        _setList.Columns.Add("Bones", 60);
        _setList.Columns.Add("Fits", 200);
        _setList.ItemChecked += OnSetItemChecked;
        _setList.ContextMenuStrip = BuildSetMenu();
        _setList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.A && e.Control)
            {
                foreach (ListViewItem item in _setList.Items) item.Selected = true;
                e.Handled = e.SuppressKeyPress = true;
            }
        };

        _setSearch.Width = 200;
        _setSearch.PlaceholderText = "Search sets, e.g. player2";
        _setSearch.TextChanged += (_, _) => RefreshSetList();

        // The list used to drop anything that did not fit, which is
        // indistinguishable from the archive not existing. If someone is hunting
        // for a set by name they need to see it and the reason, then decide for
        // themselves -- naming a set explicitly is allowed to override the check.
        _setsShowAll.Text = "Show every set, including ones that do not fit";
        _setsShowAll.AutoSize = true;
        _setsShowAll.CheckedChanged += (_, _) => RefreshSetList();

        var filterRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0),
        };
        filterRow.Controls.Add(new Label
        {
            Text = "Only clips whose name contains", AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
        });
        _clipFilter.Width = 220;
        _clipFilter.PlaceholderText = "leave blank for all";
        filterRow.Controls.Add(_clipFilter);
        filterRow.Controls.Add(_setsShowAll);
        _setsShowAll.Margin = new Padding(18, 6, 0, 0);
        filterRow.Controls.Add(new Label
        {
            Text = "Shift or Ctrl selects several — ticking one ticks them all. "
                 + "Right-click for tick / untick all.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(18, 6, 0, 0),
        });

        _previewButton.Text = "Browse & preview animations…";
        _previewButton.AutoSize = true;
        _previewButton.Enabled = false;
        _previewButton.Click += (_, _) => _ = PreviewAsync();

        var setButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0),
        };
        setButtons.Controls.Add(_findSets);
        setButtons.Controls.Add(_previewButton);
        setButtons.Controls.Add(_setSearch);
        _setSearch.Margin = new Padding(10, 2, 0, 0);

        grid.Controls.Add(_setsAll, 0, 0);
        grid.Controls.Add(_setsPick, 1, 0);
        grid.Controls.Add(setButtons, 2, 0);
        grid.Controls.Add(_setList, 0, 1);
        grid.SetColumnSpan(_setList, 3);
        grid.Controls.Add(filterRow, 0, 2);
        grid.SetColumnSpan(filterRow, 3);
        box.Controls.Add(grid);
        return box;
    }

    private Control BuildOutputStep()
    {
        var box = Step("4.  Where to save");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _outBox.Dock = DockStyle.Fill;
        _outBox.PlaceholderText = @"e.g. C:\rips\anims";
        _outBox.TextChanged += (_, _) => UpdateEnabled();

        _outBrowse.Text = "Browse...";
        _outBrowse.AutoSize = true;
        _outBrowse.Click += (_, _) => BrowseForOutput();

        grid.Controls.Add(_outBox, 0, 0);
        grid.Controls.Add(_outBrowse, 1, 0);
        box.Controls.Add(grid);
        return box;
    }

    private Control BuildAdvanced()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Margin = new Padding(2, 0, 0, 6),
        };

        _withMesh.Text = "Include the mesh in every clip (much bigger files)";
        _withMesh.AutoSize = true;
        _withMesh.Margin = new Padding(0, 4, 16, 0);

        _keepStatic.Text = "Keep clips where nothing moves";
        _keepStatic.AutoSize = true;
        _keepStatic.Margin = new Padding(0, 4, 16, 0);

        _dedupe.Text = "Skip duplicate clips";
        _dedupe.AutoSize = true;
        _dedupe.Checked = true;
        _dedupe.Margin = new Padding(0, 4, 16, 0);

        _exportModel.Text = "Also export the character model";
        _exportModel.AutoSize = true;
        _exportModel.Checked = true;
        _exportModel.Margin = new Padding(0, 4, 16, 0);

        _pack.Minimum = 0;
        _pack.Maximum = 500;
        _pack.Value = 50;
        _pack.Width = 64;

        _minMatch.Minimum = 1;
        _minMatch.Maximum = 200;
        _minMatch.Value = 8;
        _minMatch.Width = 56;

        _step.Minimum = 1;
        _step.Maximum = 20;
        _step.Value = 1;
        _step.Width = 48;

        _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeBox.Width = 90;
        _themeBox.Items.AddRange(new object[] { "Dark", "Light", "System" });
        _themeBox.SelectedIndex = 0;
        _themeBox.Margin = new Padding(16, 2, 0, 0);
        _themeBox.SelectedIndexChanged += (_, _) => OnThemeChanged();

        flow.Controls.Add(_exportModel);
        flow.Controls.Add(_dedupe);
        flow.Controls.Add(_withMesh);
        flow.Controls.Add(_keepStatic);
        flow.Controls.Add(new Label
        {
            Text = "Clips per file", AutoSize = true, Margin = new Padding(0, 6, 4, 0),
        });
        flow.Controls.Add(_pack);
        flow.Controls.Add(new Label
        {
            Text = "Min. matching bones", AutoSize = true, Margin = new Padding(0, 6, 4, 0),
        });
        flow.Controls.Add(_minMatch);
        flow.Controls.Add(new Label
        {
            Text = "Keep every", AutoSize = true, Margin = new Padding(16, 6, 4, 0),
        });
        flow.Controls.Add(_step);
        flow.Controls.Add(new Label
        {
            Text = "th frame", AutoSize = true, Margin = new Padding(4, 6, 0, 0),
        });
        flow.Controls.Add(new Label
        {
            Text = "Theme", AutoSize = true, Margin = new Padding(16, 6, 0, 0),
        });
        flow.Controls.Add(_themeBox);

        _fbLink.Text = "FoxBrowser: not found";
        _fbLink.AutoSize = true;
        _fbLink.Margin = new Padding(16, 6, 0, 0);
        _fbLink.LinkClicked += (_, _) => BrowseForFoxBrowser();
        flow.Controls.Add(_fbLink);

        return flow;
    }

    private Control BuildButtons()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _exportButton.Text = "Export animations";
        _exportButton.AutoSize = true;
        _exportButton.Padding = new Padding(14, 6, 14, 6);
        _exportButton.Font = new Font(Font, FontStyle.Bold);
        _exportButton.Click += (_, _) => _ = ExportAsync();

        _cancelButton.Text = "Stop";
        _cancelButton.AutoSize = true;
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => _cancel?.Cancel();

        _progress.Dock = DockStyle.Fill;
        _progress.Height = 22;
        _progress.Margin = new Padding(12, 6, 12, 0);

        _status.AutoSize = true;
        _status.Margin = new Padding(0, 8, 0, 0);
        _status.Text = "Ready";

        grid.Controls.Add(_exportButton, 0, 0);
        grid.Controls.Add(_cancelButton, 1, 0);
        grid.Controls.Add(_progress, 2, 0);
        grid.Controls.Add(_status, 3, 0);
        return grid;
    }

    private Control BuildLog()
    {
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font(FontFamily.GenericMonospace, 8.5f);
        return _log;
    }

    // -- start-up ----------------------------------------------------------

    private void FirstRun()
    {
        Log($"foxanimrip {AppVersion.Current}");
        var settings = GuiSettings.Load();
        _theme = Theme.Parse(settings.Theme);
        _themeBox.SelectedIndex = _theme switch
        {
            ThemeMode.Light => 1,
            ThemeMode.System => 2,
            _ => 0,
        };
        Theme.Apply(this, _theme);

        _outBox.Text = settings.OutDir ?? "";
        Log(Paths.Describe());

        _fbPath = GameFinder.FindFoxBrowser(settings.FoxBrowserExe ?? "");
        if (_fbPath.Length == 0)
        {
            Log("FoxBrowser.exe was not found automatically.");
            Log("Click 'FoxBrowser: not found' below to point at it once.");
            _fbLink.Text = "FoxBrowser: not found - click to locate";
            UpdateEnabled();
            return;
        }
        UseFoxBrowser(_fbPath);
        DetectGames(settings.GameRoot);
    }

    private void OnThemeChanged()
    {
        _theme = _themeBox.SelectedIndex switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.System,
            _ => ThemeMode.Dark,
        };
        Theme.Apply(this, _theme);
        GuiSettings.Update(s => s.Theme = Theme.Name(_theme));
    }

    private void UseFoxBrowser(string path)
    {
        try
        {
            var dir = Bundle.Extract(path, false, Log);
            Bundle.Hook(dir);
            RipJob.UseDictionaries(Path.GetDirectoryName(Path.GetFullPath(path))!, Log);
            _fbPath = path;
            _fbLink.Text = "FoxBrowser: " + Path.GetFileName(
                Path.GetDirectoryName(Path.GetFullPath(path)) ?? path);
            GuiSettings.Update(s => s.FoxBrowserExe = path);
        }
        catch (Exception ex)
        {
            Log("! could not read FoxBrowser.exe: " + ex.Message);
            _fbPath = "";
        }
        UpdateEnabled();
    }

    private void DetectGames(string preferredRoot)
    {
        _gameBox.Items.Clear();
        var installs = GameFinder.Detect(GameFinder.FoxBrowserRoots());
        if (!string.IsNullOrEmpty(preferredRoot) &&
            installs.All(i => !string.Equals(i.Root, preferredRoot,
                StringComparison.OrdinalIgnoreCase)) &&
            Directory.Exists(preferredRoot))
        {
            var profile = GameFinder.Identify(preferredRoot);
            installs.Insert(0, new GameInstall(profile, preferredRoot,
                GameFinder.ArchivesIn(preferredRoot, profile).Count));
        }

        foreach (var install in installs) _gameBox.Items.Add(install);

        if (_gameBox.Items.Count == 0)
        {
            _gamePath.Text = "No game found. Use Browse... to pick the folder "
                           + "holding the .dat or .g0s files.";
            Log("No Fox Engine install detected automatically.");
        }
        else
        {
            var index = 0;
            if (!string.IsNullOrEmpty(preferredRoot))
                for (var i = 0; i < installs.Count; i++)
                    if (string.Equals(installs[i].Root, preferredRoot,
                        StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            _gameBox.SelectedIndex = index;
            Log($"Found {installs.Count} game folder(s).");
        }
        UpdateEnabled();
    }

    // -- steps -------------------------------------------------------------

    private void OnGameChanged()
    {
        _install = _gameBox.SelectedItem as GameInstall;
        _catalog = null;
        ResetSelection();
        _modelList.Items.Clear();
        if (_install is null) return;

        _gamePath.Text = _install.Root;
        if (!_install.Profile.Verified && _install.Profile.Notes.Length > 0)
            Log($"note: {_install.Profile.DisplayName} - {_install.Profile.Notes}");
        GuiSettings.Update(s => s.GameRoot = _install.Root);
        _modelInfo.Text = "Click 'Scan game files' to list the characters.";
        UpdateEnabled();
        _ = ScanAsync(force: false);
    }

    private void ResetSelection()
    {
        _chosenModels.Clear();
        _modelPath = "";
        _contexts.Clear();
        _sets.Clear();
        _setList.Items.Clear();
    }

    private async Task ScanAsync(bool force)
    {
        if (_install is null || _busy || _fbPath.Length == 0) return;
        SetBusy(true, "Indexing game files...");
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        var install = _install;

        var progress = new Progress<ScanProgress>(p =>
        {
            _status.Text = p.Archive == "done"
                ? $"{p.Models} models, {p.Mtars} animation sets"
                : $"{p.Archive}  ({p.ArchiveIndex + 1}/{p.ArchiveCount})";
            _progress.Maximum = Math.Max(1, p.ArchiveCount);
            _progress.Value = Math.Min(p.ArchiveIndex + 1, _progress.Maximum);
        });

        try
        {
            var catalog = await Task.Run(() => GameCatalog.Open(
                install.Root, install.Profile, progress, force, token), token);
            _catalog = catalog;
            Log($"index: {catalog.Models.Count} models, {catalog.Mtars.Count} "
                + $"animation sets, {catalog.Rigs.Count} rigs");
            RefreshModelList();
        }
        catch (OperationCanceledException) { Log("Scan stopped."); }
        catch (Exception ex) { Log("! scan failed: " + ex.Message); }
        finally { SetBusy(false, "Ready"); }
    }

    private void RefreshModelList()
    {
        if (_catalog is null) return;
        var needle = _modelSearch.Text.Trim();
        var source = _catalog.CharacterModels.ToList();
        if (source.Count == 0) source = _catalog.Models;

        _visibleModels = source
            .Where(m => needle.Length == 0
                        || m.Stem.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Stem, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.Stem, StringComparer.OrdinalIgnoreCase)
            .Take(4000)
            .ToList();

        _modelList.BeginUpdate();
        _modelList.Items.Clear();
        foreach (var entry in _visibleModels) _modelList.Items.Add(entry.Stem);
        _modelList.EndUpdate();

        // Anything already chosen and still on screen stays selected.
        if (_chosenModels.Count > 0)
        {
            var chosen = _chosenModels.Select(m => m.Stem)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _visibleModels.Count; i++)
                if (chosen.Contains(_visibleModels[i].Stem))
                    _modelList.SetSelected(i, true);
        }

        UpdateModelInfo();
        UpdateEnabled();
    }

    private void SelectAllShown(bool select)
    {
        _modelList.BeginUpdate();
        for (var i = 0; i < _modelList.Items.Count; i++) _modelList.SetSelected(i, select);
        _modelList.EndUpdate();
    }

    private void OnModelSelectionChanged()
    {
        _chosenModels.Clear();
        foreach (int index in _modelList.SelectedIndices)
            if (index >= 0 && index < _visibleModels.Count)
                _chosenModels.Add(_visibleModels[index]);

        if (_chosenModels.Count > 0) _modelPath = "";
        _contexts.Clear();
        _sets.Clear();
        _setList.Items.Clear();
        UpdateModelInfo();
        UpdateEnabled();
    }

    private void UpdateModelInfo()
    {
        if (_modelPath.Length > 0)
        {
            _modelInfo.Text = _modelPath;
            return;
        }
        if (_chosenModels.Count == 0)
        {
            _modelInfo.Text = _visibleModels.Count > 0
                ? $"{_visibleModels.Count} character(s) listed - pick one or more"
                : "Scan the game files to list characters.";
            return;
        }
        var names = string.Join(", ", _chosenModels.Take(4).Select(m => m.Stem));
        if (_chosenModels.Count > 4) names += $", +{_chosenModels.Count - 4} more";
        _modelInfo.Text = $"{_chosenModels.Count} selected:  {names}";
    }

    /// <summary>Load every chosen character and find its rig. Cached per run.</summary>
    private async Task<bool> LoadCharactersAsync(CancellationToken token)
    {
        if (_contexts.Count > 0) return true;

        // The model's own path inside the archives is worth carrying: a rig
        // filed in the same folder is very likely this character's.
        var jobs = new List<(string Name, string Path, Func<byte[]> Read)>();
        if (_modelPath.Length > 0)
        {
            var path = _modelPath;
            jobs.Add((Path.GetFileNameWithoutExtension(path), path,
                      () => File.ReadAllBytes(path)));
        }
        foreach (var entry in _chosenModels)
        {
            var captured = entry;
            jobs.Add((captured.Stem, captured.Path, () => GameCatalog.Read(captured)));
        }
        if (jobs.Count == 0) return false;

        var archives = GameFinder.ArchivesIn(_install.Root, _install.Profile);
        try
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var job = jobs[i];
                _status.Text = $"Reading {job.Name} ({i + 1}/{jobs.Count})...";
                var context = await Task.Run(() =>
                {
                    var bytes = job.Read();
                    var info = RipJob.Inspect(bytes);
                    var built = ModelContext.Create(job.Name, bytes);
                    var (choice, frdv) = RigCache.Resolve(
                        archives, GameCatalog.FingerprintOf(archives), job.Name,
                        info.BoneHashes, job.Path, LogThreadSafe, token);
                    built.Attach(choice, frdv);
                    return built;
                }, token);
                _contexts.Add(context);
                Log($"{context.Name}: {context.BoneCount} bones"
                    + (context.RigUnits > 0
                        ? $", rig {context.RigUnits} units / {context.RigSegments} segments"
                          + $" ({context.RigMatchedBones}/{context.BoneCount} bones matched)"
                        : ", no rig found"));
                if (!context.RigLooksRight)
                    Log($"! {context.Name}: only {context.RigPrecision:P0} of the chosen rig's "
                        + "bones are on this skeleton, so it may not be this character's rig. "
                        + "Preview a clip before exporting thousands.");
            }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log("! could not read a model: " + ex.Message);
            _contexts.Clear();
            return false;
        }
    }

    private async Task FindSetsAsync()
    {
        if (_busy || _catalog is null || !HasCharacters) return;
        SetBusy(true, "Checking animation sets...");
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        try
        {
            if (!await LoadCharactersAsync(token)) return;
            await FindSetsCore(token);

            foreach (var row in _sets) row.Ticked = row.Fitting;
            RefreshSetList();

            var fitting = _sets.Count(r => r.Fitting);
            var totalClips = _sets.Where(r => r.Fitting).Sum(r => r.Clips);
            Log($"{fitting} compatible animation set(s), about {totalClips} clips in total");
            if (fitting < _sets.Count)
                Log($"{_sets.Count - fitting} other set(s) in this game do not fit — "
                    + "tick \"Show every set\" to see them and why.");
            _setsPick.Checked = fitting > 0;
        }
        catch (OperationCanceledException) { Log("Stopped."); }
        catch (Exception ex) { Log("! " + ex.Message); }
        finally { SetBusy(false, "Ready"); }
    }

    /// <summary>
    /// One tick applies to everything selected.
    ///
    /// Selecting a range and then having to click twenty checkboxes one at a
    /// time is the sort of thing that makes a tool tiring to use. If the row
    /// being ticked is part of a selection, the whole selection follows it.
    /// </summary>
    private void OnSetItemChecked(object sender, ItemCheckedEventArgs e)
    {
        if (e.Item.Tag is SetRow row) row.Ticked = e.Item.Checked;
        if (_bulkTicking) return;
        if (_setList.SelectedItems.Count < 2 || !e.Item.Selected) return;

        _bulkTicking = true;
        try
        {
            foreach (ListViewItem item in _setList.SelectedItems)
            {
                if (ReferenceEquals(item, e.Item)) continue;
                item.Checked = e.Item.Checked;
                if (item.Tag is SetRow other) other.Ticked = e.Item.Checked;
            }
        }
        finally { _bulkTicking = false; }
    }

    private ContextMenuStrip BuildSetMenu()
    {
        var menu = new ContextMenuStrip();

        void Add(string text, Action action) =>
            menu.Items.Add(text, null, (_, _) => action());

        Add("Tick selected", () => TickItems(_setList.SelectedItems.Cast<ListViewItem>(), true));
        Add("Untick selected", () => TickItems(_setList.SelectedItems.Cast<ListViewItem>(), false));
        menu.Items.Add(new ToolStripSeparator());
        Add("Tick all shown", () => TickItems(_setList.Items.Cast<ListViewItem>(), true));
        Add("Untick all shown", () => TickItems(_setList.Items.Cast<ListViewItem>(), false));
        menu.Items.Add(new ToolStripSeparator());
        Add("Invert shown", () =>
        {
            _bulkTicking = true;
            try
            {
                foreach (ListViewItem item in _setList.Items)
                {
                    item.Checked = !item.Checked;
                    if (item.Tag is SetRow row) row.Ticked = item.Checked;
                }
            }
            finally { _bulkTicking = false; }
            AfterBulkTick();
        });
        Add("Select all", () =>
        {
            foreach (ListViewItem item in _setList.Items) item.Selected = true;
        });

        // "All shown" means what the search box and the show-all tick are
        // currently letting through, so say so rather than implying everything.
        menu.Opening += (_, _) =>
        {
            var selected = _setList.SelectedItems.Count;
            menu.Items[0].Text = $"Tick selected ({selected})";
            menu.Items[1].Text = $"Untick selected ({selected})";
            menu.Items[0].Enabled = menu.Items[1].Enabled = selected > 0;
            menu.Items[3].Text = $"Tick all shown ({_setList.Items.Count})";
            menu.Items[4].Text = $"Untick all shown ({_setList.Items.Count})";
        };
        return menu;
    }

    private void TickItems(IEnumerable<ListViewItem> items, bool ticked)
    {
        _bulkTicking = true;
        try
        {
            foreach (var item in items.ToList())
            {
                item.Checked = ticked;
                if (item.Tag is SetRow row) row.Ticked = ticked;
            }
        }
        finally { _bulkTicking = false; }
        AfterBulkTick();
    }

    private void AfterBulkTick()
    {
        var ticked = _sets.Count(r => r.Ticked);
        _status.Text = ticked == 0
            ? "No animation set ticked."
            : $"{ticked} animation set(s) ticked.";
        if (ticked > 0 && !_setsPick.Checked) _setsPick.Checked = true;
    }

    /// <summary>
    /// Rebuild the visible set list from the search box and the show-all tick.
    ///
    /// Ticks live on the rows rather than on the list control, so filtering the
    /// view can never quietly discard a choice that scrolled out of sight.
    /// </summary>
    private void RefreshSetList()
    {
        var needle = _setSearch.Text.Trim();
        var showAll = _setsShowAll.Checked;

        _shownSets.Clear();
        foreach (var row in _sets)
        {
            if (!showAll && !row.Fitting && !row.Ticked) continue;
            if (needle.Length > 0
                && row.Entry.Stem.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            _shownSets.Add(row);
        }

        _bulkTicking = true;                 // building the list is not a user tick
        _setList.BeginUpdate();
        _setList.Items.Clear();
        foreach (var row in _shownSets)
        {
            var item = new ListViewItem(row.Entry.Stem)
            {
                Checked = row.Ticked,
                Tag = row,
            };
            item.SubItems.Add(row.Error.Length > 0 ? "-" : row.Clips.ToString());
            item.SubItems.Add(row.Error.Length > 0 ? "-" : row.BestBones.ToString());
            item.SubItems.Add(row.Verdict(_contexts.Count));
            _setList.Items.Add(item);
        }
        _setList.EndUpdate();
        _bulkTicking = false;

        if (_shownSets.Count == 0 && needle.Length > 0)
            _status.Text = showAll
                ? $"No animation set is named like \"{needle}\"."
                : $"Nothing fitting is named like \"{needle}\" — try \"Show every set\".";
        UpdateEnabled();
    }

    /// <summary>Probe every animation set against every chosen character.</summary>
    private async Task FindSetsCore(CancellationToken token)
    {
        _status.Text = "Checking which animation sets fit...";
        var contexts = _contexts.ToList();
        var minMatch = (int)_minMatch.Value;
        var catalog = _catalog;

        // Every set is kept, fitting or not. A set that is quietly dropped is
        // indistinguishable from one that does not exist, and someone looking
        // for a specific archive has no way to tell which happened -- so the
        // list records the reason and the interface can show it on request.
        var found = await Task.Run(() =>
        {
            var rows = new Dictionary<string, SetRow>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in catalog.Mtars)
            {
                token.ThrowIfCancellationRequested();
                if (!seen.Add(entry.Name)) continue;

                if (!rows.TryGetValue(entry.Name, out var row))
                    rows[entry.Name] = row = new SetRow { Entry = entry };

                byte[] bytes;
                try { bytes = GameCatalog.Read(entry); }
                catch (Exception ex) { row.Error = ex.Message; continue; }

                foreach (var context in contexts)
                {
                    var match = context.Check(bytes, minMatch);
                    row.Clips = Math.Max(row.Clips, match.Clips);
                    row.BestBones = Math.Max(row.BestBones, match.MatchedBones);
                    if (match.Fits(minMatch)) row.Fits[context.Name] = match;
                }
            }
            return rows.Values.ToList();
        }, token);

        _sets.Clear();
        _sets.AddRange(found.OrderByDescending(r => r.Fitting)
                            .ThenByDescending(r => r.Clips)
                            .ThenBy(r => r.Entry.Stem, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Open the preview on the character that is selected and the sets that are
    /// ticked.
    ///
    /// Deliberately placed next to "Find animation sets" rather than beside the
    /// export button: looking at a clip is meant to happen *before* committing
    /// to writing thousands of files, and a control's position is most of what
    /// tells you when to press it.
    /// </summary>
    private async Task PreviewAsync()
    {
        if (_busy) return;
        SetBusy(true, "Opening the preview...");
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;

        try
        {
            if (!await LoadCharactersAsync(token)) return;
            if (_contexts.Count == 0 || _catalog is null) return;

            // No compatibility pass and no set selection first. The browser
            // lists every archive in the game and every character that was
            // picked, and switching between them is a click -- which is the
            // whole point, since deciding what fits is exactly the judgement
            // that turned out to be unreliable.
            SetBusy(false, "Ready");
            var ticked = _sets.Where(r => r.Ticked).Select(r => r.Entry.Name).ToList();
            using var preview = new PreviewForm(_catalog, _contexts.ToList(), Log, ticked);
            Theme.Apply(preview, _theme);
            preview.ShowDialog(this);
            return;
        }
        catch (OperationCanceledException) { Log("preview cancelled"); }
        catch (Exception ex) { Log("! preview: " + ex.Message); }
        finally { if (_busy) SetBusy(false, "Ready"); }
    }

    private async Task ExportAsync()
    {
        if (_busy) return;
        if (_outBox.Text.Trim().Length == 0)
        {
            MessageBox.Show(this, "Choose a folder to save the animations into.",
                "foxanimrip", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "Preparing...");
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;

        try
        {
            if (!await LoadCharactersAsync(token)) return;
            if (_sets.Count == 0) await FindSetsCore(token);
            if (_sets.Count == 0)
            {
                Log("! no animation set in this game drives the chosen character(s)");
                MessageBox.Show(this, "No animation set in this game fits the character(s) "
                    + "you picked.\n\nTry lowering \"Min. matching bones\".", "foxanimrip",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var pickOnly = _setsPick.Checked;
            var ticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _sets)
                if (row.Error.Length == 0 && (!pickOnly || row.Ticked))
                    ticked.Add(row.Entry.Name);

            var forced = _sets.Where(r => pickOnly && r.Ticked && !r.Fitting).ToList();
            foreach (var row in forced)
                Log($"! {row.Entry.Stem} does not fit the chosen character "
                    + $"({row.BestBones} bones matched). Exporting it anyway because you "
                    + "ticked it; if every clip is skipped, lower \"Min. matching bones\".");

            if (ticked.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one animation set, or choose "
                    + "\"Everything that fits each character\".", "foxanimrip",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var rows = _sets.ToList();
            List<MtarSource> SourcesFor(ModelContext context, CancellationToken _)
            {
                var sources = new List<MtarSource>();
                foreach (var row in rows)
                {
                    if (!ticked.Contains(row.Entry.Name)) continue;
                    // A set the user ticked by hand is exported even where the
                    // fit check says no; one gathered by "everything that fits"
                    // is not, or every character would get every other's clips.
                    if (!row.Ticked && !row.Fits.ContainsKey(context.Name)) continue;
                    var captured = row.Entry;
                    sources.Add(new MtarSource(captured.Name, () => GameCatalog.Read(captured)));
                }
                return sources;
            }

            var options = new RipOptions
            {
                OutDir = _outBox.Text.Trim(),
                Filter = _clipFilter.Text.Trim(),
                MinMatch = (int)_minMatch.Value,
                Step = (int)_step.Value,
                WithMesh = _withMesh.Checked,
                KeepStatic = _keepStatic.Checked,
                Dedupe = _dedupe.Checked,
                PackSize = (int)_pack.Value,
                Quiet = true,
            };
            GuiSettings.Update(s => s.OutDir = options.OutDir);

            var characters = _contexts.ToList();

            if (_exportModel.Checked)
            {
                _status.Text = "Exporting the character model...";
                var dictDir = Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(_fbPath))!, "dict");
                var archives = GameFinder.ArchivesIn(_install.Root, _install.Profile);
                var outDir = options.OutDir;
                await Task.Run(() =>
                {
                    foreach (var context in characters)
                    {
                        token.ThrowIfCancellationRequested();
                        var dir = characters.Count > 1
                            ? Path.Combine(outDir, RipJob.Safe(context.Name))
                            : outDir;
                        try
                        {
                            ModelExport.Run(context, dir, archives, dictDir,
                                            withTextures: true, withSource: true,
                                            LogThreadSafe, token);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            LogThreadSafe($"! {context.Name}: model export failed "
                                          + $"({ex.Message})");
                        }
                    }
                }, token);
            }

            var perCharacter = Math.Max(1, ticked.Count);
            _progress.Maximum = Math.Max(1, characters.Count * perCharacter);
            _progress.Value = 0;

            var progress = new Progress<BatchProgress>(p =>
            {
                var done = p.CharacterIndex * perCharacter + p.SetIndex;
                _progress.Value = Math.Max(0, Math.Min(done, _progress.Maximum));
                _status.Text = characters.Count > 1
                    ? $"{p.Character} ({p.CharacterIndex + 1}/{p.CharacterCount})  -  "
                      + $"{p.Exported} clip(s)"
                    : $"{p.Exported} clip(s) written   -   {p.Set}";
            });

            var result = await Task.Run(
                () => BatchJob.Run(characters, SourcesFor, options, LogThreadSafe,
                                   progress, token), token);

            if (result.Exported > 0)
            {
                _status.Text = $"Done - {result.Exported} clips";
                var where = characters.Count > 1
                    ? "\n\nEach character has its own folder inside the destination."
                    : "";
                var open = MessageBox.Show(this,
                    $"Exported {result.Exported} animation clip(s) for "
                    + $"{characters.Count} character(s).\n\n"
                    + $"Skipped {result.Skipped} that do not fit, {result.Static} "
                    + $"with no movement"
                    + (_dedupe.Checked
                        ? $", and {result.PerCharacter.Sum(p => p.Result.Duplicates)} "
                          + "duplicates" : "")
                    + $".{where}\n\nOpen the folder?",
                    "foxanimrip", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes) OpenFolder(options.OutDir);
            }
            else
            {
                _status.Text = "Nothing exported";
                MessageBox.Show(this, "No clips were written. See the log at the "
                    + "bottom of the window.", "foxanimrip",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (OperationCanceledException) { Log("Export stopped."); }
        catch (Exception ex) { Log("! " + ex.Message); }
        finally { SetBusy(false, "Ready"); }
    }

    // -- helpers -----------------------------------------------------------

    private bool HasCharacters => _chosenModels.Count > 0 || _modelPath.Length > 0;

    private void BrowseForGame()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Pick the game folder - the one holding the .dat or .g0s files",
            UseDescriptionForTitle = true,
        };
        if (_install is not null && Directory.Exists(_install.Root))
            dialog.SelectedPath = _install.Root;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var root = dialog.SelectedPath;
        var profile = GameFinder.Identify(root);
        var count = GameFinder.ArchivesIn(root, profile).Count;
        if (count == 0)
        {
            MessageBox.Show(this,
                "No .dat, .g0s or .qar archives in that folder.\n\n"
                + "Pick the folder the game is installed in.",
                "foxanimrip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _gameBox.Items.Insert(0, new GameInstall(profile, root, count));
        _gameBox.SelectedIndex = 0;
    }

    private void BrowseForModel()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Pick a .fmdl model",
            Filter = "Fox Engine model (*.fmdl)|*.fmdl|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ResetSelection();
        _modelPath = dialog.FileName;
        _modelList.ClearSelected();
        UpdateModelInfo();
        UpdateEnabled();
    }

    private void BrowseForOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Where should the animation files go?",
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(_outBox.Text)) dialog.SelectedPath = _outBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) _outBox.Text = dialog.SelectedPath;
    }

    private void BrowseForFoxBrowser()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Where is FoxBrowser.exe?",
            Filter = "FoxBrowser|FoxBrowser.exe|Programs (*.exe)|*.exe",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        UseFoxBrowser(dialog.FileName);
        if (_fbPath.Length > 0) DetectGames(GuiSettings.Load().GameRoot);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
        }
        catch { }
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _status.Text = status;
        _cancelButton.Enabled = busy;
        if (!busy) _progress.Value = 0;
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        var ready = !_busy && _fbPath.Length > 0;
        _gameBox.Enabled = ready;
        _gameBrowse.Enabled = ready;
        _scanButton.Enabled = ready && _install is not null;
        _modelSearch.Enabled = ready && _catalog is not null;
        _modelList.Enabled = ready && _catalog is not null;
        _modelAll.Enabled = _modelNone.Enabled = ready && _modelList.Items.Count > 0;
        _modelFile.Enabled = ready;
        _findSets.Enabled = ready && HasCharacters && _catalog is not null;
        // Previewing needs a character; it does not need an output folder,
        // because looking is exactly what you do before deciding to save.
        _previewButton.Enabled = ready && HasCharacters && _catalog is not null;
        _setList.Enabled = ready && _setsPick.Checked && _setList.Items.Count > 0;
        _setSearch.Enabled = _setsShowAll.Enabled = ready && _sets.Count > 0;
        _exportButton.Enabled = ready && HasCharacters && _outBox.Text.Trim().Length > 0;
    }

    private void LogThreadSafe(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action<string>(Log), message);
        else Log(message);
    }

    private void Log(string message)
    {
        if (IsDisposed) return;
        _log.AppendText(message.TrimEnd() + Environment.NewLine);
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (!_busy) return;
        var stop = MessageBox.Show(this, "Something is still running. Stop it and close?",
            "foxanimrip", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (stop == DialogResult.Yes) _cancel?.Cancel();
        else e.Cancel = true;
    }
}
