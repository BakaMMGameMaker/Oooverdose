using Godot;
using System;

namespace Oooverdose;

/// <summary>A shared, time-based highway for gameplay and chart authoring.</summary>
public partial class TrackView : Control
{
    public RhythmGame Game { get; set; } = null!;
    public bool Decorative { get; set; }
    private PanelContainer? idleHint;
    private double hintIdleSeconds;
    private bool pointerInside;
    public bool EditorHintVisible => idleHint?.Visible == true;
    public float JudgeY => Size.Y * (float)(Game.JudgeLineY / 100);
    public Transform2D JudgeTransform => new(0, new Vector2(0, JudgeY));
    // Use the complete visible track height, including the area below the hit line.
    public double PixelsPerSecond => Math.Max(1, Size.Y);
    private float Left => Math.Max(52, (Size.X - 520) / 2);
    private float Width => Math.Min(520, Size.X - Left - 24);
    private float LaneWidth => Width / 4;
    private Font Font => ThemeDB.FallbackFont;
    public int LaneAt(float x) => x < Left || x >= Left + Width ? -1 : (int)((x - Left) / LaneWidth);
    public double TimeAt(float y) => Game.Cursor - (JudgeTransform.AffineInverse() * new Vector2(0, y)).Y / PixelsPerSecond;
    public float Y(double time) => (JudgeTransform * new Vector2(0, (float)JudgeLineAnimation.LocalNoteY(time, Game.Cursor, PixelsPerSecond))).Y;

    public override void _Ready()
    {
        MouseFilter = Decorative ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        GuiInput += input => Game.TrackInput(input, this);
        MouseDefaultCursorShape = Decorative ? CursorShape.Arrow : CursorShape.Cross;
        if (!Decorative && !string.IsNullOrEmpty(TooltipText))
        {
            string hintText = TooltipText;
            // Native tooltips can remain open while moving inside a large Control.
            // This canvas hint explicitly dismisses on any motion and restarts idle time.
            TooltipText = "";
            idleHint = new PanelContainer { Name = "EditorIdleHint", Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 100 };
            idleHint.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("08111fee"),
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
                CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 });
            var hintLabel = new Label { Text = hintText, CustomMinimumSize = new Vector2(320, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
            hintLabel.AddThemeFontSizeOverride("font_size", 14);
            idleHint.AddChild(hintLabel); AddChild(idleHint);
            MouseEntered += () => { pointerInside = true; DismissEditorHint(); };
            MouseExited += () => { pointerInside = false; DismissEditorHint(); };
            VisibilityChanged += DismissEditorHint;
        }
    }

    private void DismissEditorHint() { hintIdleSeconds = 0; idleHint?.Hide(); }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseMotion or InputEventMouseButton) DismissEditorHint();
    }

    public override void _Process(double delta)
    {
        if (idleHint == null) return;
        if (!pointerInside || !IsVisibleInTree() || Game.Current != RhythmGame.Screen.Editor ||
            Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right)) { DismissEditorHint(); return; }
        hintIdleSeconds += delta;
        if (hintIdleSeconds < .65) return;
        idleHint.Show(); idleHint.Size = idleHint.GetCombinedMinimumSize();
        var mouse = GetLocalMousePosition();
        idleHint.Position = new Vector2(Math.Clamp(mouse.X + 14, 8, Math.Max(8, Size.X - idleHint.Size.X - 8)),
            Math.Clamp(mouse.Y + 22, 8, Math.Max(8, Size.Y - idleHint.Size.Y - 8)));
    }

    public override void _Draw()
    {
        if (Size.X < 100 || Size.Y < 100) return;
        if (Decorative) { DrawDecoration(); return; }
        bool editor = Game.Current == RhythmGame.Screen.Editor;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0b1322"));
        for (int lane = 0; lane < 4; lane++)
        {
            float x = Left + lane * LaneWidth;
            var tint = RhythmGame.LaneColors[lane];
            DrawRect(new Rect2(x + 1, 0, LaneWidth - 2, Size.Y), lane % 2 == 0 ? new Color("121f32") : new Color("101b2c"));
            if (!editor && Game.KeysDown[lane])
            {
                DrawRect(new Rect2(x + 2, 0, LaneWidth - 4, JudgeY), new Color(tint, .11f));
                DrawRect(new Rect2(x + 2, JudgeY - 50, LaneWidth - 4, 50), new Color(tint, .2f));
            }
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), new Color("27364e"));
        }
        DrawLine(new Vector2(Left + Width, 0), new Vector2(Left + Width, Size.Y), new Color("27364e"));
        DrawGrid(editor);
        // Inactive edit tracks are painted first so they cannot cover the active notes.
        for (int pass = 0; pass < (editor ? 2 : 1); pass++)
        for (int i = Game.Chart.Notes.Count - 1; i >= 0; i--)
        {
            var note = Game.Chart.Notes[i];
            bool dim = editor && !Game.IsCurrentEditTrack(note);
            if (editor && dim != (pass == 0)) continue;
            int state = !editor && i < Game.NoteStates.Length ? Game.NoteStates[i] : 0;
            if (!editor && state >= 2) continue;
            float head = state == 1 ? JudgeY : Y(note.Time);
            float tail = Y(note.Time + note.Duration);
            if (tail > Size.Y + 30 || head < -30) continue;
            DrawNote(note.Lane, head, tail, note.Duration > 0, state == 1, dim);
        }
        if (editor && Game.DragLane >= 0)
        {
            double start = Math.Min(Game.DragStart, Game.DragEnd), end = Math.Max(Game.DragStart, Game.DragEnd);
            DrawNote(Game.DragLane, Y(start), Y(end), end > start, true);
        }
        DrawLine(new Vector2(Left - 8, JudgeY), new Vector2(Left + Width + 8, JudgeY), RhythmGame.Cyan, 3, true);
        DrawString(Font, new Vector2(Left - 39, Math.Clamp(JudgeY + 5, 14, Size.Y - 4)), editor ? "NOW" : "HIT", HorizontalAlignment.Left, -1, 12, RhythmGame.Cyan);
        if (!editor && Game.JudgementLife > 0)
        {
            Color color = Game.Judgement == "MISS" ? new Color("ff7f98") : RhythmGame.Cyan;
            DrawString(Font, new Vector2(Left, Math.Clamp(JudgeY - 122, 70, Size.Y - 65)), Game.Judgement, HorizontalAlignment.Center, Width, 34, color);
        }
        if (editor)
        {
            var mouse = GetLocalMousePosition();
            int lane = LaneAt(mouse.X);
            if (lane >= 0 && mouse.Y >= 30 && mouse.Y <= Size.Y)
            {
                float y = Y(Game.Snap(TimeAt(mouse.Y)));
                DrawRect(new Rect2(Left + lane * LaneWidth + 8, y - 6, LaneWidth - 16, 12), new Color(RhythmGame.LaneColors[lane], .3f));
                DrawString(Font, new Vector2(Left + 10, 46), $"{Game.Snap(TimeAt(mouse.Y)):0.000}s / LANE {lane + 1}", HorizontalAlignment.Left, -1, 13, RhythmGame.Cyan);
            }
        }
    }

    private void DrawGrid(bool editor)
    {
        double beatTime = 60 / Game.Chart.Bpm;
        int division = editor ? Game.SnapDivision : 4;
        double step = BeatGrid.Step(Game.Chart.Bpm, division);
        double start = Game.Cursor + (JudgeY - Size.Y) / PixelsPerSecond;
        double end = Game.Cursor + JudgeY / PixelsPerSecond;
        long first = (long)Math.Floor((start - Game.Chart.Offset) / step);
        long last = (long)Math.Ceiling((end - Game.Chart.Offset) / step);
        // Even the finest subdivision is bounded to a few thousand lines per frame.
        for (long tick = first; tick <= last; tick++)
        {
            double time = BeatGrid.Time(tick, Game.Chart.Bpm, Game.Chart.Offset, division);
            if (time < 0 || time > Game.SongLength) continue;
            double beat = (time - Game.Chart.Offset) / beatTime;
            bool fullBeat = Math.Abs(beat - Math.Round(beat)) < .00001;
            bool measure = fullBeat && ((long)Math.Round(beat) % 4 == 0);
            float y = Y(time);
            if (!fullBeat && step * PixelsPerSecond < 4) continue;
            DrawLine(new Vector2(Left, y), new Vector2(Left + Width, y), measure ? new Color("4b6d81") : fullBeat ? new Color("2c425b") : new Color("1d2c40"), measure ? 2 : 1);
            if (measure && editor) DrawString(Font, new Vector2(3, y - 4), $"{(int)Math.Round(beat) / 4 + 1:000}", HorizontalAlignment.Left, -1, 12, RhythmGame.Muted);
        }
    }

    private void DrawNote(int lane, float head, float tail, bool hold, bool active, bool dim = false)
    {
        float x = Left + LaneWidth * lane + 8, width = LaneWidth - 16;
        float opacity = dim ? .4f : 1;
        var color = new Color(dim ? RhythmGame.LaneColors[lane].Darkened(.4f) : RhythmGame.LaneColors[lane], opacity);
        if (hold)
        {
            float top = Math.Max(-12, tail), bottom = Math.Min(Size.Y + 12, head);
            if (bottom > top)
            {
                DrawRect(new Rect2(x + 8, top, width - 16, bottom - top), new Color(color, (active ? .55f : .27f) * opacity));
                DrawLine(new Vector2(x + width / 2, top), new Vector2(x + width / 2, bottom), new Color(color, .8f * opacity), 2);
            }
            DrawRect(new Rect2(x + 4, tail - 4, width - 8, 8), color);
        }
        DrawRect(new Rect2(x - 2, head - 9, width + 4, 18), new Color(color, .12f * opacity));
        DrawRect(new Rect2(x, head - 6, width, 12), color);
        DrawRect(new Rect2(x + 2, head - 6, width - 4, 3), new Color(dim ? color : Colors.White, .65f * opacity));
    }

    private void DrawDecoration()
    {
        float width = 340, x = (Size.X - width) / 2, bottom = Size.Y * .8f;
        for (int lane = 0; lane < 4; lane++)
        {
            float lx = x + lane * 85;
            var color = RhythmGame.LaneColors[lane];
            DrawRect(new Rect2(lx, Size.Y * .15f, 80, bottom - Size.Y * .15f), new Color(color, .035f));
            DrawLine(new Vector2(lx, Size.Y * .15f), new Vector2(lx, bottom), new Color(color, .17f));
            for (int j = 0; j < 3; j++)
            {
                float y = Size.Y * .22f + ((lane * 87 + j * 133) % Math.Max(1, (int)(Size.Y * .48f)));
                if (j == 1) DrawRect(new Rect2(lx + 12, y - 68, 56, 68), new Color(color, .16f));
                DrawRect(new Rect2(lx + 8, y, 64, 10), color);
            }
            DrawString(Font, new Vector2(lx, bottom + 42), new[] { "D", "F", "J", "K" }[lane], HorizontalAlignment.Center, 80, 25, color);
        }
        DrawLine(new Vector2(x - 14, bottom), new Vector2(x + width + 10, bottom), RhythmGame.Cyan, 3);
        DrawString(Font, new Vector2(x, bottom + 95), "FIND YOUR FLOW", HorizontalAlignment.Center, width, 16, RhythmGame.Muted);
    }
}
