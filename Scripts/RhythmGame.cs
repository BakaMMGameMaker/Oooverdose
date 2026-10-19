using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Oooverdose;

public partial class RhythmGame : Control
{
    public static readonly Color Cyan = new("54ead4"), Violet = new("af9aff"), Muted = new("8d9bb5"), Ink = new("101a2a");
    public static readonly Color[] LaneColors = { Cyan, new("78b9ff"), Violet, new("ff9fc7") };
    public enum Screen { Home, Library, Editor, Playing, Results }
    public Screen Current { get; private set; }
    public ChartData Chart { get; private set; } = new();
    public double Cursor { get; private set; }
    public double SongLength { get; private set; } = 60;
    public double PixelsPerSecond { get; private set; } = 230;
    public int SnapDivision { get; private set; } = 16;
    public bool LongTool { get; private set; }
    public bool[] KeysDown { get; } = new bool[4];
    public int[] NoteStates { get; private set; } = Array.Empty<int>();
    public string Judgement { get; private set; } = "";
    public float JudgementLife { get; private set; }
    private readonly Key[] laneKeys = { Key.D, Key.F, Key.J, Key.K };
    private AudioStreamPlayer audio = null!;
    private VBoxContainer page = null!;
    private Label status = null!, clockLabel = null!, statsLabel = null!;
    private HSlider timeline = null!;
    private TrackView? track;
    private Button playButton = null!, saveButton = null!;
    private bool libraryEdit, dirty, sliderUpdate, paused, audioStarted, audioEnded;
    private double playbackRate = 1, scoreValue;
    private int combo, maxCombo, perfect, great, good, miss, judgedUnits;
    private bool editorPlaying;
    private readonly Stack<List<ChartNote>> undo = new(), redo = new();
    private double dragStart = -1;
    private int dragLane = -1;
    public double DragStart => dragStart;
    public int DragLane => dragLane;
    public double DragEnd { get; private set; }
    private string toast = "";
    private double toastLife;
    private ConfirmationDialog confirm = null!;
    private Action? confirmedAction;
    private AcceptDialog error = null!;

    public override void _Ready()
    {
        Theme = MakeTheme();
        audio = new AudioStreamPlayer { VolumeDb = -5 };
        AddChild(audio);
        audio.Finished += () => { audioEnded = true; editorPlaying = false; };
        confirm = new ConfirmationDialog { Title = "尚有未保存的修改", DialogText = "离开将丢失未保存的谱面修改。", OkButtonText = "放弃修改并离开", CancelButtonText = "继续编辑" };
        confirm.Confirmed += () => { dirty = false; confirmedAction?.Invoke(); confirmedAction = null; };
        AddChild(confirm);
        error = new AcceptDialog { Title = "提示", OkButtonText = "知道了" };
        AddChild(error);
        GetTree().AutoAcceptQuit = false;
        GetWindow().CloseRequested += () => Guard(() => GetTree().Quit());
        GetWindow().FocusExited += () => { if (Current == Screen.Playing && !paused) TogglePause(); };
        try
        {
            if (!File.Exists(Path.Combine(ChartStore.Root, "00000000000000000000000000000001.json"))) ChartStore.Save(ChartStore.DemoChart());
        }
        catch (Exception ex) { ShowError(ex.Message); }
        ShowHome();
        if (OS.GetCmdlineUserArgs().Contains("--self-test")) CallDeferred(nameof(SelfTest));
        if (OS.GetCmdlineUserArgs().Contains("--visual-check")) CallDeferred(nameof(VisualCheck));
    }

    private Theme MakeTheme()
    {
        var theme = new Theme { DefaultFontSize = 18 };
        theme.DefaultFont = new SystemFont { FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" } };
        theme.SetColor("font_color", "Label", new Color("e8edf7"));
        theme.SetColor("font_color", "Button", new Color("e8edf7"));
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetStylebox("normal", "Button", Box(new Color("1b2940"), 8, new Color("30415d")));
        theme.SetStylebox("hover", "Button", Box(new Color("2b4058"), 8, Cyan));
        theme.SetStylebox("pressed", "Button", Box(new Color("30505d"), 8, Cyan));
        theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), 8, Cyan));
        theme.SetStylebox("normal", "LineEdit", Box(new Color("0b1424"), 6, new Color("30415d")));
        theme.SetStylebox("focus", "LineEdit", Box(new Color("0b1424"), 6, Cyan));
        theme.SetConstant("separation", "VBoxContainer", 14);
        theme.SetConstant("separation", "HBoxContainer", 14);
        return theme;
    }

    private static StyleBoxFlat Box(Color color, int radius = 12, Color? border = null)
    {
        var box = new StyleBoxFlat { BgColor = color, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        if (border.HasValue) { box.BorderColor = border.Value; box.SetBorderWidthAll(1); }
        return box;
    }

    private void ResetPage(Screen screen, string section)
    {
        audio.Stop();
        audio.StreamPaused = false;
        editorPlaying = false;
        Current = screen;
        toastLife = 0;
        dragStart = -1;
        dragLane = -1;
        if (page != null) { RemoveChild(page); page.QueueFree(); }
        track = null;
        page = new VBoxContainer { OffsetLeft = 36, OffsetTop = 24, OffsetRight = -36, OffsetBottom = -18 };
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
        MoveChild(page, 0);
        var header = new HBoxContainer();
        var brand = Text("◈  OOOVERDOSE", 25, Cyan);
        header.AddChild(brand);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        header.AddChild(Text(section, 15, Muted));
        page.AddChild(header);
        page.AddChild(new HSeparator());
        status = Text("", 14, Muted);
    }

    private void Footer(string message)
    {
        status.Text = message;
        page.AddChild(status);
    }

    private static Label Text(string text, int size = 18, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) label.AddThemeColorOverride("font_color", color.Value);
        return label;
    }

    private static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44), MouseDefaultCursorShape = CursorShape.PointingHand, FocusMode = FocusModeEnum.None };
        if (primary)
        {
            button.AddThemeStyleboxOverride("normal", Box(new Color("214f51"), 8, Cyan));
            button.AddThemeColorOverride("font_color", Cyan);
        }
        button.Pressed += action;
        return button;
    }

    private static PanelContainer Card(Control child)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(Ink, 12, new Color("223249")));
        panel.AddChild(child);
        return panel;
    }

    private void ShowHome()
    {
        ResetPage(Screen.Home, "FOUR KEYS / INFINITE RHYTHM");
        var content = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddChild(content);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        content.AddChild(left);
        left.AddChild(Text("4 LANES. ONE FLOW.", 16, Cyan));
        left.AddChild(Text("让节奏\n落在指尖。", 66));
        left.AddChild(Text("四轨下落式音乐游戏  /  内置谱面创作工具", 19, Muted));
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        var actions = new HBoxContainer();
        var start = Button("开始游戏  →", () => ShowLibrary(false), true);
        start.CustomMinimumSize = new Vector2(210, 62);
        var edit = Button("编辑谱面  ↗", () => ShowLibrary(true));
        edit.CustomMinimumSize = new Vector2(210, 62);
        actions.AddChild(start); actions.AddChild(edit); left.AddChild(actions);
        left.AddChild(Text("D / F / J / K   ·   长条请持续按住   ·   Esc 暂停", 16, Muted));
        var art = new TrackView { Game = this, Decorative = true, CustomMinimumSize = new Vector2(480, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddChild(art);
        Footer("LOCAL MUSIC, YOUR RHYTHM.                                      v1.0  /  GODOT + C#");
    }

    private void ShowLibrary(bool edit)
    {
        libraryEdit = edit;
        ResetPage(Screen.Library, edit ? "CREATE / CHART EDITOR" : "PLAY / SONG SELECT");
        var title = new HBoxContainer();
        var labels = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        labels.AddChild(Text(edit ? "谱面工作室" : "选择你的节奏", 38));
        labels.AddChild(Text(edit ? "新建创作，或继续编辑已有谱面。" : "选择一首曲目，按下 D / F / J / K。", 17, Muted));
        title.AddChild(labels);
        if (edit) title.AddChild(Button("＋ 新建谱面", () => OpenEditor(new ChartData()), true));
        title.AddChild(Button("返回首页", ShowHome));
        page.AddChild(title);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        page.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        var charts = ChartStore.LoadAll();
        foreach (var chart in charts)
        {
            var row = new HBoxContainer();
            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddChild(Text(chart.Title, 25));
            info.AddChild(Text($"{chart.Artist}   /   {chart.Bpm:0.##} BPM   /   {chart.Notes.Count} NOTES   /   {chart.Notes.Count(n => n.Duration > 0)} HOLDS", 15, Muted));
            row.AddChild(Text(chart.IsDemo && chart.MusicPath == "" ? "DEMO\n  01" : "4K\nCHART", 18, Cyan));
            row.AddChild(info);
            row.AddChild(Button(edit ? "编辑谱面  ↗" : "开始游玩  →", () => { if (edit) OpenEditor(chart); else StartGame(chart); }, true));
            list.AddChild(Card(row));
        }
        if (charts.Count == 0) list.AddChild(Text("暂无谱面，请先在编辑器中创建并保存。", 22, Muted));
        Footer("谱面与导入音乐保存在本机用户目录。编辑器支持 MP3 / OGG / WAV。");
    }

    private bool SetAudio(ChartData chart)
    {
        try { audio.Stream = ChartStore.LoadAudio(chart); SongLength = audio.Stream.GetLength(); return true; }
        catch (Exception ex) { audio.Stream = null; SongLength = 60; ShowError(ex.Message); return false; }
    }

    private void OpenEditor(ChartData chart)
    {
        Chart = chart;
        ResetPage(Screen.Editor, "STUDIO / FOUR LANE CHART EDITOR");
        audio.PitchScale = 1;
        audioEnded = false;
        playbackRate = 1;
        Cursor = 0;
        dirty = false; undo.Clear(); redo.Clear();
        if (!string.IsNullOrEmpty(chart.MusicPath) || chart.IsDemo) SetAudio(chart);
        else { audio.Stream = null; SongLength = 60; }
        var toolbar = new HBoxContainer();
        toolbar.AddChild(Text("谱面编辑器", 28));
        toolbar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        toolbar.AddChild(Button("导入音乐", ImportMusic, true));
        saveButton = Button("保存谱面  Ctrl+S", SaveChart, true);
        toolbar.AddChild(saveButton);
        toolbar.AddChild(Button("试玩", () => { if (SaveChartCore()) StartGame(Chart, true); }));
        toolbar.AddChild(Button("返回", () => Guard(() => ShowLibrary(true))));
        page.AddChild(toolbar);
        var main = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddChild(main);
        var inspector = new VBoxContainer { CustomMinimumSize = new Vector2(292, 0) };
        var sideScroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        sideScroll.AddChild(inspector); main.AddChild(sideScroll);
        inspector.AddChild(Text("01 / 曲目信息", 16, Cyan));
        AddField(inspector, "曲名", Chart.Title, value => { Chart.Title = value; MarkDirty(); });
        AddField(inspector, "作者 / Artist", Chart.Artist, value => { Chart.Artist = value; MarkDirty(); });
        AddNumber(inspector, "BPM", 20, 400, .1, Chart.Bpm, v => { Chart.Bpm = v; MarkDirty(); });
        AddNumber(inspector, "首拍偏移（秒，可为负数）", -3600, 3600, .001, Chart.Offset, v => { Chart.Offset = v; MarkDirty(); });
        inspector.AddChild(Text("02 / 编辑工具", 16, Cyan));
        var tools = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None };
        tools.AddItem("单点 NOTE  ·  左键放置"); tools.AddItem("长条 HOLD  ·  左键拖动");
        tools.Selected = LongTool ? 1 : 0;
        tools.ItemSelected += index => LongTool = index == 1;
        inspector.AddChild(tools);
        inspector.AddChild(Text("节拍吸附（全音符分母）", 15, Muted));
        var snap = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None };
        int[] divisions = { 4, 6, 8, 12, 16, 24, 32, 64, 128 };
        foreach (var div in divisions) snap.AddItem($"1/{div}  ·  {4.0 / div:0.###} 拍");
        snap.Selected = Array.IndexOf(divisions, SnapDivision);
        snap.ItemSelected += index => SnapDivision = divisions[(int)index]; inspector.AddChild(snap);
        AddNumber(inspector, "视图速度（像素 / 秒）", 80, 900, 10, PixelsPerSecond, v => PixelsPerSecond = v);
        var history = new HBoxContainer();
        history.AddChild(Button("撤销", Undo)); history.AddChild(Button("重做", Redo)); inspector.AddChild(history);
        inspector.AddChild(Text("右键：删除音符或长条\n滚轮：移动时间 · Shift：精细移动\nSpace：播放 / 暂停\nCtrl+Z / Ctrl+Y：撤销 / 重做\n编辑时暂停播放，放置更准确。", 14, Muted));
        var trackPanel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddChild(trackPanel);
        track = new TrackView { Game = this, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        trackPanel.AddChild(track);
        var transport = new HBoxContainer();
        playButton = Button("▶ 播放", ToggleEditorPlayback); transport.AddChild(playButton);
        transport.AddChild(Button("↤", () => SeekEditor(0)));
        var speed = new OptionButton { CustomMinimumSize = new Vector2(120, 40), FocusMode = FocusModeEnum.None };
        foreach (var rate in new[] { .25, .5, .75, 1.0 }) speed.AddItem($"{rate:0.##} ×");
        speed.Selected = 3;
        speed.ItemSelected += index => { playbackRate = new[] { .25, .5, .75, 1.0 }[(int)index]; audio.PitchScale = (float)playbackRate; };
        transport.AddChild(speed);
        clockLabel = Text("", 16, Cyan); transport.AddChild(clockLabel);
        trackPanel.AddChild(transport);
        timeline = new HSlider { MinValue = 0, MaxValue = SongLength, Step = .001, CustomMinimumSize = new Vector2(0, 28), FocusMode = FocusModeEnum.None };
        timeline.ValueChanged += value => { if (!sliderUpdate) SeekEditor(value); };
        trackPanel.AddChild(timeline);
        Footer("从底部的当前时间线向上摆放未来音符。拖动下方时间轴可定位整首歌曲。");
        UpdateEditorLabels();
    }

    private void AddField(VBoxContainer parent, string label, string value, Action<string> change)
    {
        parent.AddChild(Text(label, 14, Muted));
        var field = new LineEdit { Text = value, CustomMinimumSize = new Vector2(0, 38) };
        field.TextChanged += text => change(text); parent.AddChild(field);
    }

    private void AddNumber(VBoxContainer parent, string label, double min, double max, double step, double value, Action<double> change)
    {
        parent.AddChild(Text(label, 14, Muted));
        var field = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(0, 38) };
        field.ValueChanged += number => change(number); parent.AddChild(field);
    }

    private void ImportMusic()
    {
        var picker = new FileDialog { Title = "导入音乐 · MP3 / OGG / WAV", Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile, UseNativeDialog = true, Filters = new[] { "*.mp3,*.ogg,*.wav ; 音频文件" } };
        AddChild(picker);
        picker.FileSelected += path =>
        {
            try
            {
                var testChart = new ChartData { MusicPath = path };
                var stream = ChartStore.LoadAudio(testChart);
                if (Chart.Notes.Any(n => n.Time + n.Duration > stream.GetLength()))
                    throw new InvalidDataException("新音乐短于现有谱面的最后一个音符。请先删除超出范围的音符。");
                Directory.CreateDirectory(Path.Combine(ChartStore.Root, "audio"));
                string target = Path.Combine(ChartStore.Root, "audio", Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant());
                File.Copy(path, target);
                audio.Stop(); editorPlaying = false; Chart.MusicPath = target; Chart.IsDemo = false;
                audio.Stream = stream; SongLength = stream.GetLength(); Cursor = 0;
                timeline.MaxValue = SongLength; MarkDirty(); UpdateEditorLabels();
                Toast("音乐已导入并复制到谱面库。");
            }
            catch (Exception ex) { ShowError("导入失败：" + ex.Message); }
            picker.QueueFree();
        };
        picker.Canceled += () => picker.QueueFree();
        picker.PopupCenteredRatio(.75f);
    }

    private void MarkDirty() { dirty = true; if (GodotObject.IsInstanceValid(saveButton)) saveButton.Text = "● 保存谱面  Ctrl+S"; }
    private void SaveChart() => SaveChartCore();
    private bool SaveChartCore()
    {
        try
        {
            if (audio.Stream == null) throw new InvalidOperationException("请先导入音乐，再保存谱面。");
            if (string.IsNullOrWhiteSpace(Chart.Title)) throw new InvalidOperationException("请填写曲名。");
            if (Chart.Notes.Any(n => n.Time + n.Duration > SongLength + .001)) throw new InvalidOperationException("音符超出了音乐长度。");
            // Keep the bundled example available even after it is edited.
            if (Chart.Id == "00000000000000000000000000000001") Chart.Id = Guid.NewGuid().ToString("N");
            ChartStore.Save(Chart); dirty = false; saveButton.Text = "保存谱面  Ctrl+S"; Toast("谱面已保存，可在「开始游戏」中选择。"); return true;
        }
        catch (Exception ex) { ShowError("保存失败：" + ex.Message); return false; }
    }

    public double Snap(double time)
    {
        double step = 60 / Chart.Bpm * 4 / SnapDivision;
        double beat = Math.Round((time - Chart.Offset) / step, MidpointRounding.AwayFromZero);
        double snapped = Chart.Offset + beat * step;
        // Choose a grid point inside the audio instead of creating an off-grid endpoint.
        if (snapped < 0) snapped += Math.Ceiling(-snapped / step) * step;
        if (snapped > SongLength) snapped -= Math.Ceiling((snapped - SongLength) / step) * step;
        return Math.Clamp(snapped, 0, SongLength);
    }

    private void History() { undo.Push(Chart.Notes.Select(n => n.Copy()).ToList()); redo.Clear(); }
    private void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(Chart.Notes.Select(n => n.Copy()).ToList()); Chart.Notes = undo.Pop(); MarkDirty();
    }
    private void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(Chart.Notes.Select(n => n.Copy()).ToList()); Chart.Notes = redo.Pop(); MarkDirty();
    }

    private bool AddNote(int lane, double start, double end)
    {
        start = Snap(start); end = Snap(end);
        double time = Math.Min(start, end), duration = Math.Abs(end - start);
        bool overlaps = Chart.Notes.Any(n => n.Lane == lane && time <= n.Time + n.Duration + .001 && time + duration >= n.Time - .001);
        if (overlaps) { Toast("同一轨道的音符不能重叠。"); return false; }
        History(); Chart.Notes.Add(new ChartNote { Lane = lane, Time = time, Duration = duration });
        Chart.Notes = Chart.Notes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList(); MarkDirty(); return true;
    }

    public void TrackInput(InputEvent input, TrackView view)
    {
        if (Current != Screen.Editor) return;
        if (input is InputEventMouseButton wheel && wheel.Pressed && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown))
        {
            double step = 60 / Chart.Bpm * (wheel.ShiftPressed ? 4.0 / SnapDivision : 1);
            SeekEditor(Cursor + (wheel.ButtonIndex == MouseButton.WheelUp ? step : -step));
            view.AcceptEvent(); return;
        }
        if (input is InputEventMouseMotion motion && dragLane >= 0) DragEnd = Snap(view.TimeAt(motion.Position.Y));
        if (input is not InputEventMouseButton mouse) return;
        if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed && dragLane >= 0)
        {
            AddNote(dragLane, dragStart, Snap(view.TimeAt(mouse.Position.Y)));
            dragStart = -1; dragLane = -1; view.AcceptEvent(); return;
        }
        int lane = view.LaneAt(mouse.Position.X);
        if (lane < 0 || mouse.Position.Y > view.JudgeY || mouse.Position.Y < 30) return;
        double time = Snap(view.TimeAt(mouse.Position.Y));
        if (!mouse.Pressed) return;
        if (editorPlaying) { Toast("请先暂停播放，再编辑音符。"); return; }
        if (mouse.ButtonIndex == MouseButton.Right)
        {
            var note = Chart.Notes.Where(n => n.Lane == lane && view.TimeAt(mouse.Position.Y) >= n.Time - 12 / PixelsPerSecond && view.TimeAt(mouse.Position.Y) <= n.Time + n.Duration + 12 / PixelsPerSecond)
                .OrderBy(n => Math.Abs(n.Time - time)).FirstOrDefault();
            if (note != null) { History(); Chart.Notes.Remove(note); MarkDirty(); }
        }
        if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (LongTool) { dragStart = time; dragLane = lane; DragEnd = time; }
            else AddNote(lane, time, time);
        }
        view.AcceptEvent();
    }

    private void ToggleEditorPlayback()
    {
        if (audio.Stream == null) { ShowError("请先导入音乐。"); return; }
        if (editorPlaying) { Cursor = AudioTime(); audio.Stop(); editorPlaying = false; }
        else { if (Cursor >= SongLength - .02) Cursor = 0; audio.Play((float)Cursor); audioEnded = false; editorPlaying = true; }
        dragStart = -1; dragLane = -1; UpdateEditorLabels();
    }

    private double AudioTime() => Math.Clamp(audio.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() * playbackRate - AudioServer.GetOutputLatency() * playbackRate, 0, SongLength);

    private void SeekEditor(double time)
    {
        Cursor = Math.Clamp(time, 0, SongLength);
        dragStart = -1; dragLane = -1;
        if (editorPlaying) { if (Cursor >= SongLength) { audio.Stop(); editorPlaying = false; } else audio.Play((float)Cursor); }
        UpdateEditorLabels();
    }

    private void UpdateEditorLabels()
    {
        playButton.Text = editorPlaying ? "Ⅱ 暂停" : "▶ 播放";
        clockLabel.Text = $"{FormatTime(Cursor)} / {FormatTime(SongLength)}   ·   {Chart.Notes.Count} NOTES";
        sliderUpdate = true; timeline.Value = Cursor; sliderUpdate = false;
    }

    private bool fromEditor;
    private PanelContainer? pauseOverlay;
    private void StartGame(ChartData chart, bool preview = false)
    {
        if (!SetAudio(chart)) return;
        if (chart.Notes.Count == 0) { ShowError("这个谱面还没有音符，请先在编辑器中放置音符。"); return; }
        Chart = chart; fromEditor = preview;
        ResetPage(Screen.Playing, "PLAY / D · F · J · K");
        audio.PitchScale = 1; playbackRate = 1;
        Cursor = -2.5; combo = maxCombo = perfect = great = good = miss = judgedUnits = 0; scoreValue = 0;
        NoteStates = new int[chart.Notes.Count]; Array.Fill(KeysDown, false);
        audioStarted = audioEnded = paused = false; Judgement = "READY"; JudgementLife = 2;
        var top = new HBoxContainer();
        var song = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        song.AddChild(Text(Chart.Title, 28)); song.AddChild(Text($"{Chart.Artist}  /  {Chart.Bpm:0.##} BPM", 15, Muted));
        top.AddChild(song); top.AddChild(Button("Ⅱ 暂停  Esc", TogglePause)); page.AddChild(top);
        var gameBody = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddChild(gameBody);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(245, 0), Alignment = BoxContainer.AlignmentMode.Center };
        left.AddChild(Text("HOW TO PLAY", 16, Cyan));
        left.AddChild(Text("D / F / J / K", 29));
        left.AddChild(Text("音符到达底部判定线时按键\n长条持续按住直到尾部\nEsc 暂停 / 继续", 16, Muted));
        left.AddChild(Text("PERFECT   ±50 ms\nGREAT     ±100 ms\nGOOD      ±160 ms", 16, Muted));
        gameBody.AddChild(left);
        track = new TrackView { Game = this, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        gameBody.AddChild(track);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(245, 0), Alignment = BoxContainer.AlignmentMode.Center };
        right.AddChild(Text("LIVE PERFORMANCE", 16, Cyan));
        statsLabel = Text("", 22); right.AddChild(statsLabel); gameBody.AddChild(right);
        Footer("长条头部与尾部分别计分。提前松开会丢失尾部判定。暂停后按「继续」恢复。");
        pauseOverlay = null;
    }

    private int TotalUnits => Chart.Notes.Count + Chart.Notes.Count(n => n.Duration > 0);
    private int Score => TotalUnits == 0 ? 0 : (int)Math.Round(scoreValue / TotalUnits * 1000000);
    private double Accuracy => judgedUnits == 0 ? 100 : scoreValue / judgedUnits * 100;
    private void Judge(int level)
    {
        judgedUnits++;
        if (level == 0) { perfect++; scoreValue += 1; Judgement = "PERFECT"; }
        else if (level == 1) { great++; scoreValue += .75; Judgement = "GREAT"; }
        else if (level == 2) { good++; scoreValue += .4; Judgement = "GOOD"; }
        else { miss++; combo = 0; Judgement = "MISS"; }
        if (level < 3) { combo++; maxCombo = Math.Max(combo, maxCombo); }
        JudgementLife = .7f;
    }

    private void PressLane(int lane)
    {
        if (KeysDown[lane]) return;
        KeysDown[lane] = true;
        int candidate = -1; double best = .160001;
        for (int i = 0; i < Chart.Notes.Count; i++)
        {
            var note = Chart.Notes[i];
            if (note.Time > Cursor + .161) break;
            if (NoteStates[i] != 0 || note.Lane != lane) continue;
            double diff = Math.Abs(note.Time - Cursor);
            if (diff < best) { candidate = i; best = diff; }
        }
        if (candidate < 0) return;
        Judge(best <= .05 ? 0 : best <= .1 ? 1 : 2);
        NoteStates[candidate] = Chart.Notes[candidate].Duration > 0 ? 1 : 2;
    }

    private void ReleaseLane(int lane)
    {
        KeysDown[lane] = false;
        for (int i = 0; i < Chart.Notes.Count; i++)
        {
            if (NoteStates[i] != 1 || Chart.Notes[i].Lane != lane) continue;
            bool completed = Cursor >= Chart.Notes[i].Time + Chart.Notes[i].Duration - .13;
            NoteStates[i] = completed ? 2 : 3; Judge(completed ? 0 : 3);
        }
    }

    private void TogglePause()
    {
        if (Current != Screen.Playing) return;
        paused = !paused; audio.StreamPaused = paused;
        if (!paused)
        {
            if (pauseOverlay != null) { RemoveChild(pauseOverlay); pauseOverlay.QueueFree(); pauseOverlay = null; }
            // A held long note must still be physically held when resuming.
            for (int lane = 0; lane < 4; lane++) { if (!Input.IsPhysicalKeyPressed(laneKeys[lane])) ReleaseLane(lane); }
            return;
        }
        var items = new VBoxContainer();
        items.AddChild(Text("PAUSED / 已暂停", 30, Cyan));
        items.AddChild(Button("继续游戏  Esc", TogglePause, true));
        items.AddChild(Button("重新开始", () => { ClearPause(); StartGame(Chart, fromEditor); }));
        items.AddChild(Button(fromEditor ? "返回编辑器" : "返回选曲", () => { ClearPause(); if (fromEditor) OpenEditor(Chart); else ShowLibrary(false); }));
        pauseOverlay = Card(items); pauseOverlay.CustomMinimumSize = new Vector2(400, 290);
        AddChild(pauseOverlay); pauseOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.KeepSize);
    }

    private void ClearPause()
    {
        if (pauseOverlay != null) { RemoveChild(pauseOverlay); pauseOverlay.QueueFree(); pauseOverlay = null; }
    }

    private void ShowResults()
    {
        ResetPage(Screen.Results, "RESULT / SESSION COMPLETE");
        var center = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; page.AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(700, 0) }; center.AddChild(column);
        column.AddChild(Text("TRACK COMPLETE", 16, Cyan));
        column.AddChild(Text(Chart.Title, 34));
        string rank = Accuracy >= 99 ? "S" : Accuracy >= 90 ? "A" : Accuracy >= 80 ? "B" : Accuracy >= 65 ? "C" : "D";
        var row = new HBoxContainer();
        row.AddChild(Text(rank, 110, Cyan));
        var numbers = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        numbers.AddChild(Text(Score.ToString("D7"), 60)); numbers.AddChild(Text($"ACCURACY {Accuracy:0.00}%   /   MAX COMBO {maxCombo}", 20, Muted));
        row.AddChild(numbers); column.AddChild(Card(row));
        column.AddChild(Text($"PERFECT  {perfect}      GREAT  {great}      GOOD  {good}      MISS  {miss}", 21));
        var actions = new HBoxContainer();
        actions.AddChild(Button("再来一次", () => StartGame(Chart, fromEditor), true));
        actions.AddChild(Button(fromEditor ? "返回编辑器" : "返回选曲", () => { if (fromEditor) OpenEditor(Chart); else ShowLibrary(false); }));
        actions.AddChild(Button("首页", ShowHome)); column.AddChild(actions);
        Footer("每次练习，都更接近节奏。");
    }

    public override void _Process(double delta)
    {
        if (toastLife > 0) { toastLife -= delta; if (toastLife <= 0 && GodotObject.IsInstanceValid(status)) status.Text = toast; }
        JudgementLife = Math.Max(0, JudgementLife - (float)delta);
        if (Current == Screen.Editor)
        {
            if (editorPlaying) Cursor = AudioTime();
            else if (audioEnded) { Cursor = SongLength; audioEnded = false; }
            UpdateEditorLabels();
        }
        if (Current == Screen.Playing && !paused)
        {
            if (!audioStarted)
            {
                Cursor += delta;
                if (Cursor >= 0) { Cursor = 0; audio.Play(); audioStarted = true; }
                else { Judgement = Math.Ceiling(-Cursor).ToString(); JudgementLife = .1f; }
            }
            else if (audioEnded) Cursor += delta;
            else Cursor = AudioTime();
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                var n = Chart.Notes[i];
                if (NoteStates[i] == 0 && Cursor > n.Time + .16)
                {
                    NoteStates[i] = 3; Judge(3); if (n.Duration > 0) Judge(3);
                }
                else if (NoteStates[i] == 1 && Cursor >= n.Time + n.Duration) { NoteStates[i] = 2; Judge(0); }
            }
            statsLabel.Text = $"SCORE\n{Score:D7}\n\nACCURACY\n{Accuracy:0.00}%\n\nCOMBO\n{combo}\n\n{FormatTime(Math.Max(0, Cursor))} / {FormatTime(SongLength)}";
            if (Cursor >= Math.Max(SongLength, Chart.Notes.Max(n => n.Time + n.Duration)) + .5) ShowResults();
        }
        track?.QueueRedraw();
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key || key.Echo || confirm.Visible || error.Visible) return;
        var code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        bool textFocused = GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;
        if (Current == Screen.Editor && key.Pressed && key.CtrlPressed)
        {
            if (code == Key.S) { SaveChart(); GetViewport().SetInputAsHandled(); return; }
            if (!textFocused && code == Key.Z) { Undo(); GetViewport().SetInputAsHandled(); return; }
            if (!textFocused && code == Key.Y) { Redo(); GetViewport().SetInputAsHandled(); return; }
        }
        if (textFocused) return;
        if (Current == Screen.Editor && key.Pressed && code == Key.Space) { ToggleEditorPlayback(); GetViewport().SetInputAsHandled(); }
        if (Current == Screen.Playing)
        {
            if (key.Pressed && code == Key.Escape) { TogglePause(); GetViewport().SetInputAsHandled(); return; }
            if (paused) return;
            int lane = Array.IndexOf(laneKeys, code);
            if (lane < 0) return;
            if (key.Pressed) PressLane(lane); else ReleaseLane(lane);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Guard(Action action)
    {
        if (Current != Screen.Editor || !dirty) { action(); return; }
        confirmedAction = action; confirm.PopupCentered(new Vector2I(500, 190));
    }
    private void ShowError(string message) { error.DialogText = message; error.PopupCentered(new Vector2I(620, 200)); }
    private void Toast(string message) { if (toastLife <= 0) toast = status.Text; status.Text = message; toastLife = 4; }
    public static string FormatTime(double seconds) => $"{(int)seconds / 60:00}:{seconds % 60:00.000}";
}
