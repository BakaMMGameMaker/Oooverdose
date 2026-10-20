using Godot;
using System;

namespace Oooverdose;

public partial class RhythmGame
{
    private bool editorLeftOpen = true, editorRightOpen;
    private Control? editorLeftSidebar, editorRightSidebar;
    private Button? editorLeftToggle, editorRightToggle, editorToolToggle;
    private OptionButton? editorSnapSelector;

    private void BuildEditorLayout()
    {
        var toolbar = new HBoxContainer();
        editorToolToggle = Button(LongTool ? "长条" : "单点", () =>
        {
            LongTool = !LongTool; dragStart = -1; dragLane = -1;
            editorToolToggle!.Text = LongTool ? "长条" : "单点";
        }, true);
        editorToolToggle.CustomMinimumSize = new Vector2(100, 44);
        editorToolToggle.TooltipText = "单点：左键放置；长条：左键拖动";
        toolbar.AddChild(editorToolToggle);
        editorSnapSelector = new OptionButton { CustomMinimumSize = new Vector2(140, 44), FocusMode = FocusModeEnum.None, TooltipText = "节拍吸附" };
        int[] divisions = { 4, 6, 8, 12, 16, 24, 32, 64, 128 };
        foreach (int division in divisions) editorSnapSelector.AddItem($"吸附 1/{division}");
        editorSnapSelector.Select(Array.IndexOf(divisions, SnapDivision));
        editorSnapSelector.ItemSelected += index => { SnapDivision = divisions[(int)index]; if (!editorPlaying) SeekEditor(Snap(Cursor)); };
        toolbar.AddChild(editorSnapSelector);
        toolbar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        editorLeftToggle = Button("", () => SetEditorSidebar(true, !editorLeftOpen));
        editorRightToggle = Button("", () => SetEditorSidebar(false, !editorRightOpen));
        toolbar.AddChild(editorLeftToggle); toolbar.AddChild(editorRightToggle);
        saveButton = Button("保存", SaveChart, true); saveButton.TooltipText = "Ctrl+S";
        toolbar.AddChild(saveButton);
        toolbar.AddChild(Button("试玩", PreviewChart));
        toolbar.AddChild(Button("返回", () => Guard(() => ShowLibrary(true))));
        page.AddChild(toolbar);

        var workspace = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        workspace.AddThemeConstantOverride("separation", 10);
        page.AddChild(workspace);
        var leftContent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        editorLeftSidebar = MakeEditorSidebar(leftContent, 264);
        workspace.AddChild(editorLeftSidebar);
        leftContent.AddChild(Text("编辑轨道", 17, Cyan));
        editTrackSelector = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FitToLongestItem = false, ClipText = true, FocusMode = FocusModeEnum.None };
        editTrackSelector.ItemSelected += index => SelectEditTrack(Chart.EditTracks[(int)index].Id);
        leftContent.AddChild(editTrackSelector);
        editTrackName = new LineEdit { MaxLength = 40, CustomMinimumSize = new Vector2(0, 38), TooltipText = "轨道名称" };
        editTrackName.TextChanged += RenameEditTrack;
        leftContent.AddChild(editTrackName);
        leftContent.AddChild(Button("＋ 新建轨道", CreateEditTrack));
        RefreshEditTrackUi();
        leftContent.AddChild(new HSeparator());
        leftContent.AddChild(Text("曲目信息", 17, Cyan));
        leftContent.AddChild(Button("导入音乐", ImportMusic));
        AddField(leftContent, "曲名", Chart.Title, value => { Chart.Title = value; MarkDirty(); });
        AddField(leftContent, "作者", Chart.Artist, value => { Chart.Artist = value; MarkDirty(); });
        AddNumber(leftContent, "BPM", 20, 400, .1, Chart.Bpm, value => ChangeChartTiming(value, Chart.Offset));
        AddNumber(leftContent, "首拍偏移 / s", -3600, 3600, .001, Chart.Offset, value => ChangeChartTiming(Chart.Bpm, value));
        leftContent.AddChild(new HSeparator());
        var history = new HBoxContainer();
        history.AddChild(Button("撤销", Undo)); history.AddChild(Button("重做", Redo)); leftContent.AddChild(history);
        leftContent.AddChild(SoundControls(true));

        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        workspace.AddChild(center);
        track = new TrackView { Game = this, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true,
            TooltipText = "左键放置 · 右键删除 · 滚轮定位 · Shift+滚轮精细定位" };
        center.AddChild(track);
        var transport = new HBoxContainer();
        playButton = Button("▶ 播放", ToggleEditorPlayback); playButton.TooltipText = "Space";
        transport.AddChild(playButton); transport.AddChild(Button("↤", () => SeekEditor(Snap(0))));
        var speed = new OptionButton { CustomMinimumSize = new Vector2(95, 40), FocusMode = FocusModeEnum.None, TooltipText = "预听速度" };
        foreach (double rate in new[] { .25, .5, .75, 1.0 }) speed.AddItem($"{rate:0.##}×");
        speed.Select(3);
        speed.ItemSelected += index => { playbackRate = new[] { .25, .5, .75, 1.0 }[(int)index]; audio.PitchScale = (float)playbackRate; };
        transport.AddChild(speed); center.AddChild(transport);
        clockLabel = Text("", 14, Cyan); center.AddChild(clockLabel);
        timeline = new HSlider { MinValue = 0, MaxValue = SongLength, Step = .001, CustomMinimumSize = new Vector2(0, 24), FocusMode = FocusModeEnum.None };
        timeline.ValueChanged += value => { if (!sliderUpdate) SeekEditor(editorPlaying ? value : Snap(value)); };
        center.AddChild(timeline);

        var rightContent = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        editorRightSidebar = MakeEditorSidebar(rightContent, 284);
        workspace.AddChild(editorRightSidebar);
        BuildJudgeAnimator(rightContent);
        SetEditorSidebar(true, editorLeftOpen); SetEditorSidebar(false, editorRightOpen);
    }

    private static Control MakeEditorSidebar(Control content, float width)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        var style = Box(Ink, 8, new Color("223249"));
        style.ContentMarginLeft = style.ContentMarginRight = 12;
        panel.AddThemeStyleboxOverride("panel", style);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(content); panel.AddChild(scroll);
        return panel;
    }

    private void SetEditorSidebar(bool left, bool open)
    {
        var panel = left ? editorLeftSidebar : editorRightSidebar;
        if (panel == null || !GodotObject.IsInstanceValid(panel)) return;
        if (!open)
        {
            var focus = GetViewport().GuiGetFocusOwner();
            if (focus != null && panel.IsAncestorOf(focus)) focus.ReleaseFocus();
        }
        panel.Visible = open;
        if (left) { editorLeftOpen = open; editorLeftToggle!.Text = open ? "收起左栏" : "展开左栏"; }
        else { editorRightOpen = open; editorRightToggle!.Text = open ? "收起动画" : "判定线动画"; }
    }
}
