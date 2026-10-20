using Godot;
using System;
using System.Linq;

namespace Oooverdose;

public partial class RhythmGame
{
    private void TestJudgeAnimation(Action<bool, string> check, string testId)
    {
        check(JudgeLineY == 90, "legacy/no-key judgement line starts at 90 percent");
        int notesBefore = Chart.Notes.Count;
        WriteJudgeKey(0, 90, LineTransition.Step);
        WriteJudgeKey(2, 10, LineTransition.Linear);
        check(JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, -1) == 90 && JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, 1.9) == 90 &&
            JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, 2) == 10, "step holds until exact next key time");
        WriteJudgeKey(0, 90, LineTransition.Linear);
        check(Chart.JudgeLineKeys.Count == 2 && JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, .5) == 70,
            "linear interpolation and key overwrite");
        WriteJudgeKey(0, 90, LineTransition.Cubic);
        check(Math.Abs(JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, .5) - 77.5) < .00001 && JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, 1) == 50,
            "cubic smoothstep interpolation");
        check(JudgeLineAnimation.Evaluate(Chart.JudgeLineKeys, 9) == 10, "last animation key holds its value");
        WriteJudgeKey(.31, 40, LineTransition.Step);
        check(Chart.JudgeLineKeys.Any(k => k.Time == .25), "animation keys use active 1/16 beat snap");
        Undo(); check(Chart.JudgeLineKeys.Count == 2 && Chart.Notes.Count == notesBefore, "undo key edit preserves notes");
        Redo(); check(Chart.JudgeLineKeys.Count == 3, "redo restores animation keys");
        SeekEditor(.25); judgeKeyTime!.Value = .51; judgeKeyY!.Value = 45; judgeKeyTransition!.Select(2); CommitJudgeKey();
        check(Chart.JudgeLineKeys.Count == 3 && Chart.JudgeLineKeys.Any(k => k.Time == .5 && k.Y == 45 && k.Transition == LineTransition.Cubic) &&
            Chart.JudgeLineKeys.All(k => k.Time != .25), "editing key time moves selected key with snapping");
        DeleteJudgeKey(); check(Chart.JudgeLineKeys.Count == 2, "selected animation key deletion");
        Undo(); check(Chart.JudgeLineKeys.Count == 3, "animation deletion can be undone");
        Redo(); check(Chart.JudgeLineKeys.Count == 2, "animation deletion can be redone");
        SeekEditor(0); judgeTimeline!.Size = new Vector2(800, 82);
        JudgeTimelineInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true,
            Position = new Vector2(judgeTimeline.X(.31), 50) }, judgeTimeline);
        check(Chart.JudgeLineKeys.Count == 3 && SelectedJudgeKey?.Time == .25, "double click timeline inserts and selects a snapped key");
        JudgeTimelineInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true,
            Position = new Vector2(judgeTimeline.X(.25), 50) }, judgeTimeline);
        check(Chart.JudgeLineKeys.Count == 2, "right click timeline deletes selected marker");
        Chart.Offset = .07; SnapDivision = 6;
        WriteJudgeKey(1.1, 30, LineTransition.Linear);
        check(Chart.JudgeLineKeys.Any(k => Math.Abs(k.Time - 1.07) < .000001), "animation keys respect triplet snap and first-beat offset");
        Chart.Offset = 0; SnapDivision = 16;
        Chart.JudgeLineKeys = new()
        {
            new() { Time = 0, Y = 90, Transition = LineTransition.Linear },
            new() { Time = 2, Y = 10, Transition = LineTransition.Cubic },
            new() { Time = 4, Y = 100, Transition = LineTransition.Step },
            new() { Time = 6, Y = 0, Transition = LineTransition.Cubic },
            new() { Time = 8, Y = 90 }
        };
        var view = track!; view.Size = new Vector2(760, 600);
        foreach (double time in new[] { 0, .25, 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            Cursor = time;
            check(Math.Abs(view.Y(time) - view.JudgeY) < .0001 && Math.Abs(view.Y(time + 1) - view.JudgeY + 600) < .0001,
                $"notes arrive on animated line exactly at Time; local speed stays fixed at t={time}");
            check(Math.Abs(view.Y(time + .2) - view.Y(time + .7) - 300) < .0001 &&
                Math.Abs(view.TimeAt(view.Y(time + .5)) - time - .5) < .00001, "hold length and editor inverse remain correct under animation");
        }
        check(SaveChartCore(), "animated chart saves");
        var reloaded = ChartStore.LoadAll().Single(c => c.Id == testId);
        check(reloaded.JudgeLineKeys.Count == 5 && reloaded.JudgeLineKeys[1].Transition == LineTransition.Cubic && reloaded.JudgeLineKeys[3].Y == 0,
            "animation keys and transition modes survive JSON reload");
        StartGame(Chart, true); Cursor = 1; PressLane(0); ReleaseLane(0);
        check(perfect == 1 && Math.Abs(track!.Y(Cursor) - track.JudgeY) < .0001, "animated gameplay judgement still uses note song time");
        OpenEditor(Chart); Chart.JudgeLineKeys.Clear(); SelectedJudgeKey = null; undo.Clear(); redo.Clear(); SeekEditor(0);
        check(JudgeLineY == 90, "deleting all keys restores default line height");
    }
}
