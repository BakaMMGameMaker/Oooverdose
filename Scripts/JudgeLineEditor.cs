using Godot;
using System;
using System.Linq;

namespace Oooverdose;

public partial class RhythmGame
{
    public double JudgeLineY => JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, Cursor);
    public JudgeLineKey? SelectedJudgeKey { get; private set; }
    private SpinBox? judgeKeyTime, judgeKeyY;
    private OptionButton? judgeKeyTransition;
    private Label? judgeAnimationLabel;
    private JudgeLineTimeline? judgeTimeline;

    private void BuildJudgeAnimator(VBoxContainer parent)
    {
        parent.AddChild(Text("判定线动画", 17, Cyan));
        parent.TooltipText = "曲线：双击加帧、右键删帧。写入时使用顶部的节拍吸附。";
        judgeAnimationLabel = Text("", 14, Cyan);
        parent.AddChild(judgeAnimationLabel);
        judgeTimeline = new JudgeLineTimeline { Game = this, CustomMinimumSize = new Vector2(0, 136), ClipContents = true };
        parent.AddChild(judgeTimeline);
        parent.AddChild(Text("时间 / s", 14, Muted));
        judgeKeyTime = new SpinBox { MinValue = 0, MaxValue = SongLength, Step = .001, CustomMinimumSize = new Vector2(0, 38), TooltipText = "写入时按当前 BPM、首拍偏移和节拍吸附量化" };
        parent.AddChild(judgeKeyTime);
        parent.AddChild(Text("Y / %", 14, Muted));
        judgeKeyY = new SpinBox { MinValue = 0, MaxValue = 100, Step = .1, Value = 90, CustomMinimumSize = new Vector2(0, 38), TooltipText = "0 为轨道顶部，100 为轨道底部" };
        parent.AddChild(judgeKeyY);
        parent.AddChild(Text("过渡", 14, Muted));
        judgeKeyTransition = new OptionButton { CustomMinimumSize = new Vector2(118, 36), FocusMode = FocusModeEnum.None, TooltipText = "从此关键帧到下一帧的过渡方式" };
        judgeKeyTransition.AddItem("跳变"); judgeKeyTransition.AddItem("线性"); judgeKeyTransition.AddItem("三次");
        parent.AddChild(judgeKeyTransition);
        var row = new HBoxContainer();
        row.AddChild(Button("K 写入", CommitJudgeKey, true));
        row.AddChild(Button("删除", DeleteJudgeKey));
        parent.AddChild(row);
        var navigation = new HBoxContainer();
        navigation.AddChild(Button("‹ 上一帧", () => GoToJudgeKey(false)));
        navigation.AddChild(Button("下一帧 ›", () => GoToJudgeKey(true)));
        parent.AddChild(navigation);
        SyncJudgeAnimationFields();
    }

    private void SyncJudgeAnimationFields()
    {
        if (judgeKeyTime == null || !GodotObject.IsInstanceValid(judgeKeyTime)) return;
        SelectedJudgeKey = Chart.JudgeLineKeys.FirstOrDefault(k => Math.Abs(k.Time - Cursor) < .000001);
        judgeKeyTime.MaxValue = SongLength;
        judgeKeyTime.SetValueNoSignal(Cursor);
        judgeKeyY!.SetValueNoSignal(SelectedJudgeKey?.Y ?? JudgeLineY);
        judgeKeyTransition!.Select((int)(SelectedJudgeKey?.Transition ?? LineTransition.Linear));
    }

    private void CommitJudgeKey() => WriteJudgeKey(judgeKeyTime!.Value, judgeKeyY!.Value, (LineTransition)judgeKeyTransition!.Selected, true);

    private void WriteJudgeKey(double time, double y, LineTransition transition, bool moveSelected = false)
    {
        if (editorPlaying) { Toast("请先暂停播放，再编辑判定线关键帧。"); return; }
        time = Snap(time);
        History();
        if (moveSelected && SelectedJudgeKey != null) Chart.JudgeLineKeys.Remove(SelectedJudgeKey);
        var key = Chart.JudgeLineKeys.FirstOrDefault(k => Math.Abs(k.Time - time) < .000001);
        if (key == null) { key = new JudgeLineKey { Time = time }; Chart.JudgeLineKeys.Add(key); }
        key.Y = Math.Clamp(y, 0, 100); key.Transition = transition;
        Chart.JudgeLineKeys = Chart.JudgeLineKeys.OrderBy(k => k.Time).ToList();
        MarkDirty(); SeekEditor(time);
    }

    private void DeleteJudgeKey()
    {
        if (editorPlaying) { Toast("请先暂停播放，再删除判定线关键帧。"); return; }
        if (SelectedJudgeKey == null || !Chart.JudgeLineKeys.Contains(SelectedJudgeKey)) return;
        History(); Chart.JudgeLineKeys.Remove(SelectedJudgeKey); SelectedJudgeKey = null;
        MarkDirty(); SyncJudgeAnimationFields();
    }

    private void GoToJudgeKey(bool next)
    {
        var key = next ? Chart.JudgeLineKeys.FirstOrDefault(k => k.Time > Cursor + .000001)
            : Chart.JudgeLineKeys.LastOrDefault(k => k.Time < Cursor - .000001);
        if (key != null) SeekEditor(key.Time);
    }

    public void JudgeTimelineInput(InputEvent input, JudgeLineTimeline timelineView)
    {
        if (Current != Screen.Editor || input is not InputEventMouseButton mouse || !mouse.Pressed) return;
        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            NavigateEditorBeat(mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1, mouse.ShiftPressed);
        }
        else
        {
            var nearby = Chart.JudgeLineKeys.Where(k => Math.Abs(timelineView.X(k.Time) - mouse.Position.X) <= 9)
                .OrderBy(k => Math.Abs(timelineView.X(k.Time) - mouse.Position.X)).FirstOrDefault();
            double time = nearby?.Time ?? Snap(timelineView.TimeAt(mouse.Position.X));
            if (mouse.ButtonIndex == MouseButton.Right)
            {
                if (nearby != null && !editorPlaying) { SeekEditor(nearby.Time); DeleteJudgeKey(); }
            }
            else if (mouse.ButtonIndex == MouseButton.Left)
            {
                SeekEditor(time);
                if (mouse.DoubleClick && nearby == null) WriteJudgeKey(time, JudgeLineY, LineTransition.Linear);
            }
        }
        timelineView.AcceptEvent();
    }

    private void UpdateJudgeAnimator()
    {
        if (judgeAnimationLabel != null)
            judgeAnimationLabel.Text = $"Y {JudgeLineY:0.0}% · {Chart.JudgeLineKeys.Count} 帧";
        judgeTimeline?.QueueRedraw();
    }
}

public partial class JudgeLineTimeline : Control
{
    public RhythmGame Game { get; set; } = null!;
    private double Span => Math.Max(.01, Math.Min(Game.SongLength, 8 * 60 / Game.Chart.Bpm));
    private double Start => Math.Clamp(Game.Cursor - Span * .25, 0, Math.Max(0, Game.SongLength - Span));
    private float GraphTop => 23;
    private float GraphHeight => Size.Y - 33;
    public float X(double time) => 12 + (float)((time - Start) / Span * Math.Max(1, Size.X - 24));
    public double TimeAt(float x) => Math.Clamp(Start + (x - 12) / Math.Max(1, Size.X - 24) * Span, 0, Game.SongLength);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Cross;
        GuiInput += input => Game.JudgeTimelineInput(input, this);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("101c2d"));
        double beat = 60 / Game.Chart.Bpm, step = BeatGrid.Step(Game.Chart.Bpm, Game.SnapDivision);
        long first = (long)Math.Ceiling((Start - Game.Chart.Offset) / step);
        long last = (long)Math.Floor((Start + Span - Game.Chart.Offset) / step);
        for (long tick = first; tick <= last; tick++)
        {
            double time = BeatGrid.Time(tick, Game.Chart.Bpm, Game.Chart.Offset, Game.SnapDivision);
            double beatIndex = (time - Game.Chart.Offset) / beat;
            bool full = Math.Abs(beatIndex - Math.Round(beatIndex)) < .00001;
            if (!full && (Size.X - 24) * step / Span < 4) continue;
            float x = X(time);
            DrawLine(new Vector2(x, 19), new Vector2(x, Size.Y), full ? new Color("3a526c") : new Color("23364c"));
            int labelEvery = Math.Max(1, (int)Math.Ceiling(48 / Math.Max(1, (Size.X - 24) * beat / Span)));
            if (full && x < Size.X - 40 && (long)Math.Round(beatIndex) % labelEvery == 0)
                DrawString(ThemeDB.FallbackFont, new Vector2(x + 3, 14), $"{time:0.##}s", HorizontalAlignment.Left, -1, 11, RhythmGame.Muted);
        }
        Vector2 previous = Vector2.Zero;
        for (int x = 12; x <= Size.X - 12; x += 2)
        {
            var point = new Vector2(x, GraphTop + (float)(JudgeLineAnimation.Evaluate(Game.Chart.JudgeLineKeys, TimeAt(x)) / 100) * GraphHeight);
            if (x > 12) DrawLine(previous, point, new Color("af9aff"), 2, true);
            previous = point;
        }
        foreach (var key in Game.Chart.JudgeLineKeys)
        {
            float x = X(key.Time);
            if (x < 6 || x > Size.X - 6) continue;
            var point = new Vector2(x, GraphTop + (float)(key.Y / 100) * GraphHeight);
            DrawCircle(point, key == Game.SelectedJudgeKey ? 6 : 4, key == Game.SelectedJudgeKey ? new Color("ffdd8c") : RhythmGame.Cyan);
        }
        float cursor = X(Game.Cursor);
        DrawLine(new Vector2(cursor, 19), new Vector2(cursor, Size.Y), RhythmGame.Cyan, 2);
    }
}
