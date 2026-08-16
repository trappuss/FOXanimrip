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

        public string Label(int characterCount)
        {
            var clips = $"{Clips} clip{(Clips == 1 ? "" : "s")}";
            if (characterCount <= 1)
            {
                var bones = Fits.Values.FirstOrDefault()?.MatchedBones ?? 0;
                return $"{Entry.Stem}   -   {clips}, {bones} bones matched";
            }
            return $"{Entry.Stem}   -   {clips}, fits {Fits.Count} of {characterCount}";
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
    private readonly CheckedListBox _setList = new();
    private readonly TextBox _clipFilter = new();

    private readonly TextBox _outBox = new();
    private readonly Button _outBrowse = new();

    private readonly Button _exportButton = new();
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
        Text = "foxanimrip - bulk animation export for Fox Engine games";
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
        _setList.CheckOnClick = true;
        _setList.IntegralHeight = false;

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

        grid.Controls.Add(_setsAll, 0, 0);
        grid.Controls.Add(_setsPick, 1, 0);
        grid.Controls.Add(_findSets, 2, 0);
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

        var jobs = new List<(string Name, Func<byte[]> Read)>();
        if (_modelPath.Length > 0)
        {
            var path = _modelPath;
            jobs.Add((Path.GetFileNameWithoutExtension(path), () => File.ReadAllBytes(path)));
        }
        foreach (var entry in _chosenModels)
        {
            var captured = entry;
            jobs.Add((captured.Stem, () => GameCatalog.Read(captured)));
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
                    built.Attach(Sources.FindFrig(archives, info.BoneHashes, null),
                                 Sources.FindFrdv(archives, job.Name, null));
                    return built;
                }, token);
                _contexts.Add(context);
                Log($"{context.Name}: {context.BoneCount} bones"
                    + (context.RigUnits > 0
                        ? $", rig {context.RigUnits} units / {context.RigSegments} segments"
                        : ", no rig found"));
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

            _setList.BeginUpdate();
            _setList.Items.Clear();
            var totalClips = 0;
            foreach (var row in _sets)
            {
                totalClips += row.Clips;
                _setList.Items.Add(row.Label(_contexts.Count), true);
            }
            _setList.EndUpdate();
            Log($"{_sets.Count} compatible animation set(s), about {totalClips} clips in total");
            _setsPick.Checked = _sets.Count > 0;
        }
        catch (OperationCanceledException) { Log("Stopped."); }
        catch (Exception ex) { Log("! " + ex.Message); }
        finally { SetBusy(false, "Ready"); }
    }

    /// <summary>Probe every animation set against every chosen character.</summary>
    private async Task FindSetsCore(CancellationToken token)
    {
        _status.Text = "Checking which animation sets fit...";
        var contexts = _contexts.ToList();
        var minMatch = (int)_minMatch.Value;
        var catalog = _catalog;

        var found = await Task.Run(() =>
        {
            var rows = new Dictionary<string, SetRow>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in catalog.Mtars)
            {
                token.ThrowIfCancellationRequested();
                if (!seen.Add(entry.Name)) continue;
                byte[] bytes;
                try { bytes = GameCatalog.Read(entry); } catch { continue; }

                foreach (var context in contexts)
                {
                    var match = context.Check(bytes, minMatch);
                    if (!match.Fits(minMatch)) continue;
                    if (!rows.TryGetValue(entry.Name, out var row))
                        rows[entry.Name] = row = new SetRow { Entry = entry, Clips = match.Clips };
                    row.Clips = Math.Max(row.Clips, match.Clips);
                    row.Fits[context.Name] = match;
                }
            }
            return rows.Values.ToList();
        }, token);

        _sets.Clear();
        _sets.AddRange(found.OrderByDescending(r => r.Clips)
                            .ThenBy(r => r.Entry.Stem, StringComparer.OrdinalIgnoreCase));
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
            for (var i = 0; i < _sets.Count; i++)
                if (!pickOnly || (i < _setList.Items.Count && _setList.GetItemChecked(i)))
                    ticked.Add(_sets[i].Entry.Name);

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
                    if (!row.Fits.ContainsKey(context.Name)) continue;   // not for this one
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
        _setList.Enabled = ready && _setsPick.Checked && _setList.Items.Count > 0;
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
