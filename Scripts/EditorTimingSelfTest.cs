using Godot;
using System;
using System.Linq;

namespace Oooverdose;

public partial class RhythmGame
{
    private void TestEditorTiming(Action<bool, string> check, string testId)
    {
        var originalNotes = Chart.Notes.Select(n => n.Copy()).ToList();
        var originalKeys = Chart.JudgeLineKeys.Select(k => k.Copy()).ToList();
        double originalBpm = Chart.Bpm, originalOffset = Chart.Offset, originalCursor = Cursor;
        int originalDivision = SnapDivision;
        var view = track!;
        view.Size = new Vector2(760, 600);
        foreach (double offset in new[] { .025, -.025, .137 })
        {
            ChangeChartTiming(180, offset);
            foreach (int division in new[] { 4, 6, 8, 12, 16, 24, 32, 64, 128 })
            {
                SnapDivision = division;
                SeekEditor(Snap(1.337));
                double tick = Math.Round((Cursor - offset) / BeatGrid.Step(180, division));
                double gridTime = BeatGrid.Time((long)tick, 180, offset, division);
                check(Math.Abs(view.Y(gridTime) - view.JudgeY) < .001, $"NOW and snapped grid agree for offset {offset} and 1/{division}");
                SeekEditor(1.337); NavigateEditorBeat(1, false);
                check(Math.Abs(Cursor - Snap(Cursor)) < .000001, "coarse wheel reanchors off-grid cursor");
                NavigateEditorBeat(-1, true);
                check(Math.Abs(Cursor - Snap(Cursor)) < .000001, "fine wheel follows offset grid");
            }
        }
        check(Chart.Notes.Select(n => n.Time).SequenceEqual(originalNotes.Select(n => n.Time)), "offset adjustment never silently retimes existing notes");
        Chart.Notes.Clear(); Chart.JudgeLineKeys.Clear(); SnapDivision = 16;
        ChangeChartTiming(180, .025);
        double target = Snap(1.337);
        AddNote(3, 1.337, 1.337);
        WriteJudgeKey(1.337, 30, LineTransition.Step);
        check(Chart.Notes.Single().Time == Chart.JudgeLineKeys.Single().Time && Math.Abs(Cursor - target) < .000001,
            "note and animation share one offset-adjusted timestamp");
        check(JudgeLineY == 30 && Math.Abs(view.Y(target) - view.JudgeY) < .001, "note arrives exactly when offset animation key takes effect");
        int hits = hitSounds.PlayedCount;
        PrepareEditorHits(0, true); PlayEditorHitsThrough(target - .001);
        check(hitSounds.PlayedCount == hits, "offset note sound does not fire early");
        PlayEditorHitsThrough(target);
        check(hitSounds.PlayedCount == hits + 1, "offset note sound fires at animation key time");
        check(SaveChartCore(), "offset chart saves without timestamp conversion");
        var loaded = ChartStore.LoadAll().Single(c => c.Id == testId);
        check(loaded.Notes.Single().Time == loaded.JudgeLineKeys.Single().Time && Math.Abs(loaded.Notes.Single().Time - target) < .000001,
            "offset timestamps remain identical after reload");
        StartGame(Chart, true); Cursor = target; PressLane(3); ReleaseLane(3);
        check(perfect == 1 && JudgeLineY == 30, "nonzero offset gameplay judgement and animation happen at the same audio time");
        OpenEditor(Chart); view = track!; view.Size = new Vector2(760, 600);
        view.EmitSignal(Control.SignalName.MouseEntered);
        view._Input(new InputEventMouseMotion { Relative = Vector2.One });
        view._Process(.64); check(!view.EditorHintVisible, "canvas hint waits for stationary pointer");
        view._Process(.02); check(view.EditorHintVisible, "canvas hint appears after hover delay");
        view._Input(new InputEventMouseMotion { Relative = Vector2.One });
        check(!view.EditorHintVisible, "mouse motion immediately dismisses canvas hint");
        view._Process(.2); check(!view.EditorHintVisible, "canvas hint cannot immediately reappear while pointer moves");
        view.EmitSignal(Control.SignalName.MouseExited); view._Process(1);
        check(!view.EditorHintVisible && view.TooltipText == "", "canvas exit clears hint and native tooltip is disabled");
        Chart.Notes = originalNotes; Chart.JudgeLineKeys = originalKeys;
        Chart.Bpm = originalBpm; Chart.Offset = originalOffset; SnapDivision = originalDivision;
        undo.Clear(); redo.Clear(); SeekEditor(originalCursor);
    }
}
