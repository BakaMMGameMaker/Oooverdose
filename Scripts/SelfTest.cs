using Godot;
using System;
using System.IO;
using System.Linq;

namespace Oooverdose;

public partial class RhythmGame
{
    // Run with Godot --headless --path . -- --self-test after building the C# assembly.
    private void SelfTest()
    {
        string testId = Guid.NewGuid().ToString("N");
        try
        {
            void Check(bool value, string message) { if (!value) throw new Exception(message); GD.Print("PASS: " + message); }
            var chart = ChartStore.DemoChart(); chart.Id = testId;
            ChartStore.Save(chart);
            var loaded = ChartStore.LoadAll().Single(c => c.Id == testId);
            Check(loaded.Notes.Count == chart.Notes.Count && loaded.Bpm == 120, "chart JSON round trip");
            Check(Math.Abs(ChartStore.LoadAudio(chart).GetLength() - 48) < .01, "demo audio generated with correct duration");
            string wavPath = Path.Combine(ChartStore.Root, testId + ".wav");
            try
            {
                Check(((AudioStreamWav)ChartStore.LoadAudio(chart)).SaveToWav(wavPath) == Error.Ok, "write WAV fixture");
                Check(Math.Abs(ChartStore.LoadAudio(new ChartData { MusicPath = wavPath }).GetLength() - 48) < .01, "external WAV decoder");
            }
            finally { if (File.Exists(wavPath)) File.Delete(wavPath); }
            OpenEditor(loaded);
            Chart.Notes.Clear();
            Check(Math.Abs(Snap(.31) - .25) < .0001, "1/16 snap at 120 BPM");
            foreach (int division in new[] { 4, 6, 8, 12, 16, 24, 32, 64, 128 })
            {
                SnapDivision = division;
                double step = .5 * 4 / division;
                Check(Math.Abs(Snap(step * 3.1) - step * 3) < .0001, "snap division 1/" + division);
            }
            SnapDivision = 16;
            Check(AddNote(0, 1, 1), "tap placement");
            Check(AddNote(1, 2, 3), "hold placement");
            Check(!AddNote(1, 2.5, 2.5), "overlapping hold rejected");
            Undo(); Check(Chart.Notes.Count == 1, "undo placement");
            Redo(); Check(Chart.Notes.Count == 2 && Chart.Notes[1].Duration == 1, "redo hold");
            var view = track!; view.Size = new Vector2(760, 600); Cursor = 4;
            int laneX = Enumerable.Range(0, 760).First(x => view.LaneAt(x) == 3) + 10;
            float At(double time) => view.JudgeY - (float)((time - Cursor) * PixelsPerSecond);
            LongTool = true;
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(laneX, At(4.25)) }, view);
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(laneX, At(5)) }, view);
            Check(Chart.Notes.Count == 3 && Chart.Notes.Last().Duration == .75, "mouse drag creates hold");
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(laneX, At(4.5)) }, view);
            Check(Chart.Notes.Count == 2, "right click deletes hold body");
            Undo(); Undo(); Check(Chart.Notes.Count == 2, "undo restores gesture history");
            LongTool = false;
            Chart.Offset = .07;
            Check(Math.Abs(Snap(.31) - .32) < .0001, "audio offset used by snap");
            Chart.Offset = 0;
            Check(SaveChartCore(), "editor saves valid chart");
            StartGame(Chart, true);
            Cursor = 1; PressLane(0); ReleaseLane(0);
            Check(perfect == 1 && NoteStates[0] == 2, "perfect tap judgement");
            Cursor = 2; PressLane(1); Cursor = 3; ReleaseLane(1);
            Check(perfect == 3 && Score == 1000000 && combo == 3, "hold head and tail full score");
            StartGame(Chart, true);
            Cursor = 2; PressLane(1); Cursor = 2.4; ReleaseLane(1);
            Check(miss == 1 && combo == 0, "early hold release misses tail");
            StartGame(Chart, true);
            Cursor = 5; audioStarted = true; audioEnded = true; _Process(.01);
            Check(miss == 3 && judgedUnits == 3, "unplayed taps and holds expire once");
            audioEnded = false; audio.Play();
            TogglePause(); Check(paused && audio.StreamPaused, "pause transport");
            TogglePause(); Check(!paused, "resume transport");
            audio.Stop(); audioEnded = true; Cursor = 49; _Process(.01);
            Check(Current == Screen.Results, "song completion opens results");
            ShowLibrary(false); Check(Current == Screen.Library, "library navigation");
            ShowHome(); Check(Current == Screen.Home, "home navigation");
            GD.Print("SELF_TEST_OK");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError("SELF_TEST_FAILED: " + ex); GetTree().Quit(1); }
        finally
        {
            string path = Path.Combine(ChartStore.Root, testId + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private async void VisualCheck()
    {
        try
        {
            async System.Threading.Tasks.Task Capture(string name)
            {
                for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng("res://.verification/" + name + ".png");
                GD.Print("CAPTURED: " + name);
            }
            audio.VolumeDb = -80;
            await Capture("home");
            ShowLibrary(false); await Capture("library");
            OpenEditor(ChartStore.DemoChart()); SeekEditor(12); await Capture("editor");
            StartGame(ChartStore.DemoChart()); Cursor = 12; paused = true;
            statsLabel.Text = "SCORE\n0256400\n\nACCURACY\n98.45%\n\nCOMBO\n32";
            await Capture("gameplay");
            paused = false; TogglePause(); await Capture("pause"); ClearPause();
            ShowResults(); await Capture("results");
            GD.Print("VISUAL_CHECK_OK"); GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
}
