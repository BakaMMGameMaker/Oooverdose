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
        string[] migrationIds = { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        string settingsFixturePath = Path.Combine(ChartStore.Root, testId + ".cfg");
        byte[]? savedUserSettings = File.Exists(UserSettingsPath) ? File.ReadAllBytes(UserSettingsPath) : null;
        try
        {
            void Check(bool value, string message) { if (!value) throw new Exception(message); GD.Print("PASS: " + message); }
            var profile = new PlayerSettings();
            Check(profile.ScrollRate == 1, "global scroll rate defaults to 1x");
            profile.ScrollRate = .1; Check(profile.ScrollRate == .2, "scroll rate minimum is 0.2x");
            profile.ScrollRate = 4; Check(profile.ScrollRate == 3, "scroll rate maximum is 3x");
            profile.ScrollRate = 1.26; Check(profile.ScrollRate == 1.3, "scroll rate quantized in 0.1 steps");
            profile.ScrollRate = double.NaN; Check(profile.ScrollRate == 1, "invalid scroll rate resets safely");
            profile.ScrollRate = 1.7; profile.HitSoundStyle = 2; profile.HitSoundVolume = .37f;
            profile.Save(settingsFixturePath);
            var reloaded = PlayerSettings.Load(settingsFixturePath);
            Check(reloaded.ScrollRate == 1.7 && reloaded.HitSoundStyle == 2 && Math.Abs(reloaded.HitSoundVolume - .37) < .00001,
                "global speed and sound settings survive profile reload");
            int beforeSave = Directory.GetFiles(ChartStore.Root, "*.json").Length;
            OpenEditor(ChartStore.LoadAll().Single(c => c.Id == ChartStore.DemoId));
            saveButton.EmitSignal(Godot.Button.SignalName.Pressed);
            _Input(new InputEventKey { PhysicalKeycode = Key.S, CtrlPressed = true, Pressed = true });
            PreviewChart();
            OpenEditor(Chart);
            SaveChart();
            Check(Chart.Id == ChartStore.DemoId && Directory.GetFiles(ChartStore.Root, "*.json").Length == beforeSave,
                "manual save, Ctrl+S and preview return preserve demo ID and file count");
            foreach (string id in migrationIds)
            {
                var fixture = ChartStore.DemoChart(); fixture.Id = id; fixture.Title = "migration-" + testId;
                if (id == migrationIds[2]) fixture.Notes[0].Duration += .125;
                ChartStore.Save(fixture);
            }
            var migrated = ChartStore.LoadAll().Where(c => c.Title == "migration-" + testId).ToList();
            Check(migrated.Count == 2 && migrated.Any(c => c.Id == migrationIds[2]), "legacy identical copies merge but different edits survive");
            Check(Directory.GetFiles(Path.Combine(ChartStore.Root, "duplicate-backups"), "*.json")
                .Any(path => migrationIds.Any(id => Path.GetFileName(path).StartsWith(id + "-"))), "legacy duplicate original is backed up");
            Check(ChartStore.LoadAll().Count(c => c.Title == "migration-" + testId) == 2, "duplicate migration is idempotent");
            var chart = ChartStore.DemoChart(); chart.Id = testId; chart.Title = "self-test-" + testId;
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
            settings.ScrollRate = .5;
            float At(double time) => view.JudgeY - (float)((time - Cursor) * view.PixelsPerSecond);
            LongTool = true;
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(laneX, At(4.25)) }, view);
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(laneX, At(5)) }, view);
            Check(Chart.Notes.Count == 3 && Chart.Notes.Last().Duration == .75, "mouse drag creates hold");
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(laneX, At(4.5)) }, view);
            Check(Chart.Notes.Count == 2, "right click deletes hold body");
            Undo(); Undo(); Check(Chart.Notes.Count == 2, "undo restores gesture history");
            foreach (float height in new[] { 360f, 600f, 1000f })
            {
                view.Size = new Vector2(760, height);
                foreach (double rate in new[] { .2, 1.0, 2.0, 3.0 })
                {
                    settings.ScrollRate = rate;
                    double noteTimeAtTop = view.TimeAt(0);
                    Check(Math.Abs(view.Y(noteTimeAtTop)) < .001 && Math.Abs(view.Y(noteTimeAtTop - 1 / rate) - height) < .001,
                        $"top-to-bottom duration {1 / rate:0.###}s at height {height} and {rate}x");
                    Check(Math.Abs(view.TimeAt(view.Y(Cursor + .25)) - Cursor - .25) < .00001,
                        "editor time/pixel conversion remains invertible");
                }
            }
            settings.ScrollRate = 1;
            var rateControls = SpeedControls(); AddChild(rateControls);
            var rateInput = (SpinBox)rateControls.GetChild(1);
            Check(rateInput.MinValue == .2 && rateInput.MaxValue == 3 && rateInput.Step == .1, "global rate input bounds and step");
            rateInput.Value = 2;
            Check(ScrollRate == 2 && PlayerSettings.Load(UserSettingsPath).ScrollRate == 2 && ReferenceEquals(hitSounds.Settings, settings),
                "rate UI edits one persistent shared user profile");
            rateControls.QueueFree();
            settings.ScrollRate = 1;
            LongTool = false;
            Chart.Offset = .07;
            Check(Math.Abs(Snap(.31) - .32) < .0001, "audio offset used by snap");
            Chart.Offset = 0;
            Check(SaveChartCore(), "editor saves valid chart");
            int beforeRepeat = Directory.GetFiles(ChartStore.Root, "*.json").Length;
            Check(SaveChartCore() && Chart.Id == testId && Directory.GetFiles(ChartStore.Root, "*.json").Length == beforeRepeat,
                "repeated save of user chart updates existing file");
            var originalNotes = Chart.Notes;
            Chart.Notes = Chart.Notes.Select(n => n.Copy()).ToList();
            Chart.Notes.Add(new ChartNote { Lane = 3, Time = 0 });
            Chart.Notes.Add(new ChartNote { Lane = 2, Time = 1 });
            int editorHits = hitSounds.PlayedCount;
            PrepareEditorHits(0, true);
            PlayEditorHitsThrough(0);
            Check(hitSounds.PlayedCount == editorHits + 1, "editor includes note exactly at playback start");
            PlayEditorHitsThrough(.99);
            Check(hitSounds.PlayedCount == editorHits + 1, "editor does not play future notes early");
            PlayEditorHitsThrough(1); PlayEditorHitsThrough(1);
            Check(hitSounds.PlayedCount == editorHits + 3, "editor chord plays once per note without frame repeats");
            PlayEditorHitsThrough(3);
            Check(hitSounds.PlayedCount == editorHits + 5, "editor crossed hold head and tail play even across frame gap");
            PrepareEditorHits(1, false); PlayEditorHitsThrough(1);
            Check(hitSounds.PlayedCount == editorHits + 5, "editor resume excludes already played boundary");
            SeekEditor(2.5);
            Check(hitSounds.PlayedCount == editorHits + 5, "editor forward seek does not replay skipped notes");
            PlayEditorHitsThrough(3);
            Check(hitSounds.PlayedCount == editorHits + 6, "editor seek inside hold still plays upcoming tail");
            SeekEditor(1); PlayEditorHitsThrough(1);
            Check(hitSounds.PlayedCount == editorHits + 8, "editor backward seek rearms notes at destination");
            Chart.Notes = originalNotes;
            StartGame(Chart, true);
            Check(ScrollRate == 1 && ReferenceEquals(hitSounds.Settings, settings), "preview shares global rate and sound settings");
            int hitsBefore = hitSounds.PlayedCount;
            PressLane(3); PressLane(3);
            Check(hitSounds.PlayedCount == hitsBefore + 1, "empty press plays once and held key does not repeat");
            ReleaseLane(3); PressLane(3); ReleaseLane(3);
            Check(hitSounds.PlayedCount == hitsBefore + 2, "second empty press plays again after release");
            hitsBefore = hitSounds.PlayedCount;
            Cursor = 1; PressLane(0); ReleaseLane(0);
            Check(perfect == 1 && NoteStates[0] == 2, "perfect tap judgement");
            Check(hitSounds.PlayedCount == hitsBefore + 1, "tap success plays hit sound");
            Cursor = 2; PressLane(1); Cursor = 3; ReleaseLane(1);
            Check(perfect == 3 && Score == 1000000 && combo == 3, "hold head and tail full score");
            Check(hitSounds.PlayedCount == hitsBefore + 3, "hold head and tail play separate sounds");
            hitsBefore = hitSounds.PlayedCount;
            Judge(3); Check(hitSounds.PlayedCount == hitsBefore, "miss does not play success sound");
            hitSounds.Style = 1; hitSounds.Volume = 0;
            hitSounds.PlayHit(); Check(hitSounds.PlayedCount == hitsBefore, "hit volume zero mutes sound");
            hitSounds.Volume = .65f;
            for (int i = 0; i < 4; i++) hitSounds.PlayHit();
            Check(hitSounds.PlayedCount == hitsBefore + 4, "four simultaneous hits play with bright style");
            foreach (int style in new[] { 0, 1, 2 })
            {
                using var sample = HitSoundPlayer.Synthesize(style);
                Check(sample.GetLength() > .04 && sample.GetLength() < .13 && sample.Data.Any(b => b != 0), "hit sample has short non-silent PCM: " + style);
            }
            hitSounds.Style = 2; hitsBefore = hitSounds.PlayedCount; hitSounds.PlayHit();
            Check(hitSounds.PlayedCount == hitsBefore + 1, "deep Pu style plays through voice pool");
            using var deepSample = HitSoundPlayer.Synthesize(2);
            using var brightSample = HitSoundPlayer.Synthesize(1);
            var deepPcm = deepSample.Data;
            var brightPcm = brightSample.Data;
            double CrossingRate(byte[] pcm)
            {
                int crossings = 0;
                for (int i = 2; i < pcm.Length; i += 2)
                    if ((short)(pcm[i - 2] | pcm[i - 1] << 8) < 0 != ((short)(pcm[i] | pcm[i + 1] << 8) < 0)) crossings++;
                return (double)crossings / (pcm.Length / 2);
            }
            Check(CrossingRate(deepPcm) < CrossingRate(brightPcm) / 3, "deep Pu has substantially less high frequency oscillation than bright click");
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
            if (File.Exists(settingsFixturePath)) File.Delete(settingsFixturePath);
            if (savedUserSettings != null) File.WriteAllBytes(UserSettingsPath, savedUserSettings);
            else if (File.Exists(UserSettingsPath)) File.Delete(UserSettingsPath);
            foreach (string id in migrationIds)
            {
                string fixture = Path.Combine(ChartStore.Root, id + ".json");
                if (File.Exists(fixture)) File.Delete(fixture);
                string backups = Path.Combine(ChartStore.Root, "duplicate-backups");
                if (Directory.Exists(backups))
                    foreach (string backup in Directory.GetFiles(backups, id + "-*.json")) File.Delete(backup);
            }
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
