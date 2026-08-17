// SPDX-License-Identifier: MIT
using FoxAnimRip.Preview;

namespace FoxAnimRip.Gui;

/// <summary>
/// Browse a game's animations: pick a character, pick an archive, pick a clip,
/// watch it loop.
///
/// This deliberately shows **every** animation archive in the game, with no
/// compatibility filter in front of it. The filter was a mistake. Matching an
/// archive to a skeleton automatically is unreliable -- an .mtar only carries a
/// bone table when its header sets HAS_SKEL_LIST and many do not, so both this
/// tool and FoxBrowser's own dialog will happily report that nothing fits a
/// character whose animations play perfectly well the moment you name the
/// archive by hand. Hiding archives on the strength of that guess means the
/// thing you are looking for is missing and the interface cannot tell you why.
///
/// So the match count is shown as a column and never as a gate. A low number is
/// a hint, not a verdict, and pressing play settles it in a second -- which is
/// the whole reason the preview exists.
/// </summary>
public sealed class PreviewForm : Form
{
    private readonly GameCatalog _catalog;
    private readonly List<ModelContext> _contexts;
    private readonly Action<string> _log;

    private readonly ComboBox _character = new();
    private readonly TextBox _setSearch = new();
    private readonly TextBox _clipSearch = new();
    private readonly ListView _sets = new();
    private readonly ListView _clips = new();
    private readonly PreviewSurface _surface = new();
    private readonly Label _status = new();
    private readonly Label _detail = new();
    private readonly TrackBar _scrub = new();
    private readonly Button _play = new();
    private readonly CheckBox _showMesh = new();
    private readonly CheckBox _showSkeleton = new();
    private readonly CheckBox _showGrid = new();
    private readonly CheckBox _rootMotion = new();
    private readonly ComboBox _speed = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    private readonly List<SetRow> _allSets = new();
    private readonly List<SetRow> _shownSets = new();
    private readonly List<ClipRow> _allClips = new();
    private readonly List<ClipRow> _shownClips = new();

    private MtarAnimSetHandle _handle;
    private ModelContext _context;
    private CancellationTokenSource _work;
    private DateTime _lastTick = DateTime.UtcNow;
    private double _frameCursor;
    private bool _playing = true;
    private bool _scrubbing;

    private sealed class SetRow
    {
        public CatalogEntry Entry;
        public int Clips = -1;         // -1 until read
        public int Bones = -1;
        public int Matched = -1;
        public string Note = "";
    }

    private sealed class ClipRow
    {
        public int Index;
        public string Name = "";
        public int Frames = -1;
        public int Bones = -1;
        public string Path = "";
    }

    private readonly HashSet<string> _ticked;
    private readonly CheckBox _onlyTicked = new();

    public PreviewForm(GameCatalog catalog, List<ModelContext> contexts, Action<string> log,
                       IEnumerable<string> tickedSets = null)
    {
        _catalog = catalog;
        _contexts = contexts ?? new List<ModelContext>();
        _log = log ?? (_ => { });
        _context = _contexts.FirstOrDefault();
        _ticked = new HashSet<string>(tickedSets ?? Array.Empty<string>(),
                                      StringComparer.OrdinalIgnoreCase);

        Text = $"foxanimrip {AppVersion.Current} — animation browser";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1240, 740);
        MinimumSize = new Size(900, 520);
        KeyPreview = true;

        BuildLayout();
        WireEvents();
    }

    // -- layout ------------------------------------------------------------

    private void BuildLayout()
    {
        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 460,
            FixedPanel = FixedPanel.Panel1,
        };

        // Left: archives above, their clips below.
        var lists = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
        };

        Column(_sets, ("Animation set", 190), ("Clips", 55), ("Bones", 55),
                      ("Matched", 65), ("Where", 400));
        Column(_clips, ("#", 46), ("Clip", 250), ("Frames", 60), ("Bones", 55), ("Path", 300));

        // Ticked sets are a decision already made, so honour it by default --
        // but keep it one click to widen, because the whole reason this window
        // shows everything is that the automatic judgement cannot be trusted.
        _onlyTicked.Text = "Only the sets ticked in the main window";
        _onlyTicked.AutoSize = true;
        _onlyTicked.Dock = DockStyle.Top;
        _onlyTicked.Checked = _ticked.Count > 0;
        _onlyTicked.Enabled = _ticked.Count > 0;

        var setPanel = new Panel { Dock = DockStyle.Fill };
        setPanel.Controls.Add(_sets);
        setPanel.Controls.Add(Labelled(_setSearch, "Search every animation set…"));
        setPanel.Controls.Add(_onlyTicked);
        lists.Panel1.Controls.Add(setPanel);

        var clipPanel = new Panel { Dock = DockStyle.Fill };
        clipPanel.Controls.Add(_clips);
        clipPanel.Controls.Add(Labelled(_clipSearch, "Search clips in this set…"));
        lists.Panel2.Controls.Add(clipPanel);

        outer.Panel1.Controls.Add(lists);

        var right = new Panel { Dock = DockStyle.Fill };
        _surface.Dock = DockStyle.Fill;
        right.Controls.Add(_surface);
        right.Controls.Add(BuildTransport());
        right.Controls.Add(BuildToggles());
        outer.Panel2.Controls.Add(right);

        _character.DropDownStyle = ComboBoxStyle.DropDownList;
        _character.Dock = DockStyle.Fill;
        foreach (var context in _contexts)
            _character.Items.Add($"{context.Name}   ({context.BoneCount} bones)");
        if (_character.Items.Count > 0) _character.SelectedIndex = 0;

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 30, ColumnCount = 2, RowCount = 1,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(new Label { Text = "Character", AutoSize = true,
                                     Margin = new Padding(4, 6, 6, 0) }, 0, 0);
        top.Controls.Add(_character, 1, 0);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 22;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(8, 0, 8, 0);

        Controls.Add(outer);
        Controls.Add(top);
        Controls.Add(_status);
    }

    private static void Column(ListView view, params (string Text, int Width)[] columns)
    {
        view.Dock = DockStyle.Fill;
        view.View = View.Details;
        view.FullRowSelect = true;
        view.MultiSelect = false;
        view.HideSelection = false;
        view.VirtualMode = false;
        foreach (var (text, width) in columns) view.Columns.Add(text, width);
    }

    private static Control Labelled(TextBox box, string placeholder)
    {
        box.Dock = DockStyle.Top;
        box.PlaceholderText = placeholder;
        return box;
    }

    private Control BuildTransport()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        _play.Text = "Pause";
        _play.Width = 76;
        _play.Dock = DockStyle.Left;
        _scrub.Dock = DockStyle.Fill;
        _scrub.TickStyle = TickStyle.None;
        _scrub.Minimum = 0;
        _scrub.Maximum = 1;
        _detail.Dock = DockStyle.Right;
        _detail.Width = 270;
        _detail.TextAlign = ContentAlignment.MiddleRight;
        _detail.Padding = new Padding(0, 0, 8, 0);
        bar.Controls.Add(_scrub);
        bar.Controls.Add(_play);
        bar.Controls.Add(_detail);
        return bar;
    }

    private Control BuildToggles()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 32,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 4, 4, 0),
        };
        _showMesh.Text = "Mesh"; _showMesh.Checked = true; _showMesh.AutoSize = true;
        _showSkeleton.Text = "Skeleton"; _showSkeleton.AutoSize = true;
        _showGrid.Text = "Ground"; _showGrid.Checked = true; _showGrid.AutoSize = true;
        // Off by default to match the export default, so what you watch is what
        // you get. On, the character walks away from the grid instead of on it.
        _rootMotion.Text = "Root motion"; _rootMotion.AutoSize = true;
        _speed.DropDownStyle = ComboBoxStyle.DropDownList;
        _speed.Width = 76;
        _speed.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "2x" });
        _speed.SelectedIndex = 2;
        bar.Controls.Add(_showMesh);
        bar.Controls.Add(_showSkeleton);
        bar.Controls.Add(_showGrid);
        bar.Controls.Add(_rootMotion);
        bar.Controls.Add(new Label { Text = "Speed", AutoSize = true,
                                     Padding = new Padding(12, 4, 2, 0) });
        bar.Controls.Add(_speed);
        return bar;
    }

    private void WireEvents()
    {
        Load += async (_, _) => await StartAsync();
        FormClosed += (_, _) => { _work?.Cancel(); _timer.Stop(); };

        _character.SelectedIndexChanged += (_, _) => _ = SwitchCharacterAsync();
        _setSearch.TextChanged += (_, _) => RefreshSets();
        _onlyTicked.CheckedChanged += (_, _) => RefreshSets();
        _clipSearch.TextChanged += (_, _) => RefreshClips();
        _sets.SelectedIndexChanged += (_, _) => _ = OpenSelectedSetAsync();
        _clips.SelectedIndexChanged += (_, _) => PlaySelectedClip();

        _play.Click += (_, _) => SetPlaying(!_playing);
        _scrub.Scroll += (_, _) =>
        {
            _scrubbing = true;
            _frameCursor = _scrub.Value;
            _surface.Frame = _scrub.Value;
            _surface.Invalidate();
            UpdateDetail();
            _scrubbing = false;
        };

        _showMesh.CheckedChanged += (_, _) =>
            Toggle(() => _surface.Options.ShowMesh = _showMesh.Checked);
        _showSkeleton.CheckedChanged += (_, _) =>
            Toggle(() => _surface.Options.ShowSkeleton = _showSkeleton.Checked);
        _showGrid.CheckedChanged += (_, _) =>
            Toggle(() => _surface.Options.ShowGround = _showGrid.Checked);
        _rootMotion.CheckedChanged += (_, _) =>
        {
            if (_surface.Clip is not null) _surface.Clip.RootMotion = _rootMotion.Checked;
            _surface.Invalidate();
            UpdateDetail();
        };

        _timer.Interval = 16;
        _timer.Tick += (_, _) => Advance();
        KeyDown += OnShortcut;
    }

    private void Toggle(Action change)
    {
        change();
        if (!_surface.Options.ShowMesh && !_surface.Options.ShowSkeleton)
        {
            _showSkeleton.Checked = true;
            return;
        }
        _surface.Invalidate();
    }

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        // KeyPreview routes every keystroke here first, so single-letter
        // shortcuts were eating half the alphabet out of the search boxes --
        // s, m and r vanished, and space with them. Typing wins over shortcuts
        // wherever text can be typed.
        if (ActiveControl is TextBoxBase or ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList })
            return;

        switch (e.KeyCode)
        {
            case Keys.Space: SetPlaying(!_playing); break;
            case Keys.R: _surface.ResetView(); break;
            case Keys.S: _showSkeleton.Checked = !_showSkeleton.Checked; break;
            case Keys.M: _showMesh.Checked = !_showMesh.Checked; break;
            case Keys.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    // -- loading -----------------------------------------------------------

    private async Task StartAsync()
    {
        // Every archive, listed straight away. Reading their clip and bone
        // counts happens afterwards in the background, so the list is usable
        // immediately instead of after a minute of probing.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _catalog.Mtars)
            if (seen.Add(entry.Name))
                _allSets.Add(new SetRow { Entry = entry });

        RefreshSets();
        _status.Text = _onlyTicked.Checked
            ? $"{_shownSets.Count} ticked set(s). Untick the box above to see all "
              + $"{_allSets.Count} in the game."
            : $"{_allSets.Count} animation set(s) in this game. Pick one, then a clip. "
              + "Nothing is hidden — Matched is a hint, not a filter.";

        await SwitchCharacterAsync();
        _timer.Start();
        if (_sets.Items.Count > 0) _sets.Items[0].Selected = true;
    }

    private async Task SwitchCharacterAsync()
    {
        var index = _character.SelectedIndex;
        if (index < 0 || index >= _contexts.Count) return;
        _context = _contexts[index];

        try
        {
            var model = await Task.Run(() => _context.BuildPreviewModel());
            _surface.SetModel(model);
            _status.Text = $"{_context.Name}: {model.VertexCount:N0} vertices, "
                         + $"{model.Triangles.Length / 3:N0} triangles, "
                         + $"{model.BoneCount} bones";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not build " + _context.Name + ": " + ex.Message;
            _log("! preview: " + ex.Message);
            return;
        }

        // The match counts are per character, so they go stale on a switch.
        foreach (var row in _allSets) row.Matched = -1;
        RefreshSets();
        StartBackgroundProbe();
        PlaySelectedClip();
    }

    /// <summary>
    /// Fill in the clip, bone and match counts without blocking the list.
    ///
    /// Reading every archive in Phantom Pain takes a while, and none of it is
    /// needed to start watching something -- so the numbers arrive as they are
    /// found, and the visible rows are done first.
    /// </summary>
    private void StartBackgroundProbe()
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        var context = _context;
        var pending = _shownSets.ToList();
        pending.AddRange(_allSets.Where(r => !pending.Contains(r)));

        _ = Task.Run(() =>
        {
            foreach (var row in pending)
            {
                if (token.IsCancellationRequested) return;
                if (row.Clips >= 0 && row.Matched >= 0) continue;
                try
                {
                    var bytes = GameCatalog.Read(row.Entry);
                    var info = SetSurvey.Describe(bytes, row.Entry.Name, row.Entry.Path,
                                                  row.Entry.ArchiveName);
                    row.Clips = info.Clips;
                    row.Bones = info.BoneHashes.Count;

                    // Must go through Check, not a bone-hash intersection. The
                    // player's archives are rig-driven: their tracks are indexed
                    // by rig channel, not named after bones, so comparing hashes
                    // directly reports zero for archives that play perfectly.
                    row.Matched = context is null
                        ? 0
                        : context.Check(bytes, 1).MatchedBones;
                    row.Note = info.HasSkeletonList ? "" : "no skeleton list";
                }
                catch (Exception ex)
                {
                    row.Clips = 0;
                    row.Bones = 0;
                    row.Matched = 0;
                    row.Note = ex.Message;
                }

                if (token.IsCancellationRequested || IsDisposed) return;
                try { BeginInvoke(new Action(() => UpdateSetRow(row))); }
                catch (InvalidOperationException) { return; }
            }
        }, token);
    }

    // -- the two lists -----------------------------------------------------

    private void RefreshSets()
    {
        var needle = _setSearch.Text.Trim();
        var chosen = SelectedSet();

        var onlyTicked = _onlyTicked.Checked && _ticked.Count > 0;

        _shownSets.Clear();
        foreach (var row in _allSets)
        {
            if (onlyTicked && !_ticked.Contains(row.Entry.Name)
                && !_ticked.Contains(row.Entry.Stem))
                continue;
            if (needle.Length > 0
                && row.Entry.Stem.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                && row.Entry.Path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            _shownSets.Add(row);
        }

        _sets.BeginUpdate();
        _sets.Items.Clear();
        foreach (var row in _shownSets) _sets.Items.Add(MakeItem(row));
        _sets.EndUpdate();

        if (chosen is null) return;
        var index = _shownSets.IndexOf(chosen);
        if (index >= 0) _sets.Items[index].Selected = true;
    }

    private static ListViewItem MakeItem(SetRow row)
    {
        var item = new ListViewItem(row.Entry.Stem) { Tag = row };
        item.SubItems.Add(row.Clips < 0 ? "…" : row.Clips.ToString());
        item.SubItems.Add(row.Bones < 0 ? "…" : row.Bones.ToString());
        item.SubItems.Add(row.Matched < 0 ? "…" : row.Matched.ToString());
        item.SubItems.Add(row.Note.Length > 0
            ? $"{row.Entry.ArchiveName} — {row.Note}"
            : $"{row.Entry.ArchiveName}:{row.Entry.Path}");
        return item;
    }

    private void UpdateSetRow(SetRow row)
    {
        var index = _shownSets.IndexOf(row);
        if (index < 0 || index >= _sets.Items.Count) return;
        var item = _sets.Items[index];
        item.SubItems[1].Text = row.Clips.ToString();
        item.SubItems[2].Text = row.Bones.ToString();
        item.SubItems[3].Text = row.Matched.ToString();
        item.SubItems[4].Text = row.Note.Length > 0
            ? $"{row.Entry.ArchiveName} — {row.Note}"
            : $"{row.Entry.ArchiveName}:{row.Entry.Path}";
    }

    private SetRow SelectedSet() => _sets.SelectedItems.Count > 0
        ? _sets.SelectedItems[0].Tag as SetRow : null;

    private async Task OpenSelectedSetAsync()
    {
        var row = SelectedSet();
        if (row is null) return;

        _allClips.Clear();
        _shownClips.Clear();
        _clips.Items.Clear();
        _status.Text = $"Opening {row.Entry.Stem}…";

        try
        {
            var entry = row.Entry;
            var handle = await Task.Run(() =>
                MtarAnimSetHandle.Open(new MtarSource(entry.Name,
                                                      () => GameCatalog.Read(entry))));
            _handle = handle;
            for (var i = 0; i < handle.ClipNames.Count; i++)
                _allClips.Add(new ClipRow
                {
                    Index = i,
                    Name = handle.ClipNames[i],
                    Path = i < handle.ClipPaths.Count ? handle.ClipPaths[i] : "",
                });
        }
        catch (Exception ex)
        {
            _handle = null;
            _status.Text = $"{row.Entry.Stem} could not be opened: {ex.Message}";
            _log($"! preview: {row.Entry.Stem} ({ex.Message})");
            return;
        }

        RefreshClips();
        _status.Text = $"{row.Entry.Stem}: {_allClips.Count} clip(s) — "
                     + $"{row.Entry.ArchiveName}:{row.Entry.Path}";
        if (_clips.Items.Count > 0) _clips.Items[0].Selected = true;
    }

    private void RefreshClips()
    {
        var needle = _clipSearch.Text.Trim();
        _shownClips.Clear();
        foreach (var clip in _allClips)
        {
            if (needle.Length > 0
                && clip.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            _shownClips.Add(clip);
        }

        _clips.BeginUpdate();
        _clips.Items.Clear();
        foreach (var clip in _shownClips)
        {
            // The index is the clip's packing order in the archive, which is the
            // key the community's written description lists use.
            var item = new ListViewItem($"{clip.Index:0000}") { Tag = clip };
            item.SubItems.Add(clip.Name);
            item.SubItems.Add(clip.Frames < 0 ? "" : clip.Frames.ToString());
            item.SubItems.Add(clip.Bones < 0 ? "" : clip.Bones.ToString());
            item.SubItems.Add(clip.Path);
            _clips.Items.Add(item);
        }
        _clips.EndUpdate();
    }

    private void PlaySelectedClip()
    {
        if (_handle is null || _context is null) return;
        if (_clips.SelectedItems.Count == 0) return;
        if (_clips.SelectedItems[0].Tag is not ClipRow chosen) return;

        try
        {
            var clip = _handle.Clip(chosen.Index, _context);
            clip.RootMotion = _rootMotion.Checked;
            _surface.SetClip(clip);
            _frameCursor = 0;
            _scrub.Maximum = Math.Max(1, clip.FrameCount - 1);
            _scrub.Value = 0;

            chosen.Frames = clip.FrameCount;
            chosen.Bones = clip.MatchedBones;
            var index = _shownClips.IndexOf(chosen);
            if (index >= 0 && index < _clips.Items.Count)
            {
                _clips.Items[index].SubItems[2].Text = chosen.Frames.ToString();
                _clips.Items[index].SubItems[3].Text = chosen.Bones.ToString();
            }

            UpdateDetail();
            _surface.Invalidate();
        }
        catch (Exception ex)
        {
            _detail.Text = "could not decode";
            _log($"! preview: {chosen.Name} ({ex.Message})");
        }
    }

    // -- playback ----------------------------------------------------------

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _play.Text = playing ? "Pause" : "Play";
        _lastTick = DateTime.UtcNow;
    }

    private void Advance()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        var clip = _surface.Clip;
        if (clip is null || !_playing || _scrubbing) return;

        var multiplier = _speed.SelectedIndex switch
        {
            0 => 0.25, 1 => 0.5, 3 => 2.0, _ => 1.0,
        };
        _frameCursor += elapsed * clip.Fps * multiplier;
        if (clip.FrameCount > 0) _frameCursor %= clip.FrameCount;

        var frame = (int)_frameCursor;
        if (frame == _surface.Frame) return;
        _surface.Frame = frame;
        if (_scrub.Maximum >= frame) _scrub.Value = frame;
        _surface.Invalidate();
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        var clip = _surface.Clip;
        if (clip is null) { _detail.Text = ""; return; }
        _detail.Text = $"frame {_surface.Frame + 1}/{clip.FrameCount}   "
                     + $"{clip.MatchedBones} bones   {_surface.LastFrameMs:0.0} ms";
    }
}
