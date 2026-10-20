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
        string settingsFixturePath = Path.Combine(ChartStore.Root, testId + ".cfg");
        byte[]? savedUserSettings = File.Exists(UserSettingsPath) ? File.ReadAllBytes(UserSettingsPath) : null;
        try
        {
            void Check(bool value, string message) { if (!value) throw new Exception(message); GD.Print("PASS: " + message); }
            var legacyChart = System.Text.Json.JsonSerializer.Deserialize<ChartData>("{\"Notes\":[{\"Lane\":0,\"Time\":1,\"Duration\":0}]}")!;
            ChartStore.Validate(legacyChart);
            Check(legacyChart.EditTracks.Count == 1 && legacyChart.EditTracks[0].Id == 1 && legacyChart.Notes[0].EditTrackId == 1,
                "legacy charts and notes default to edit track 1");
            var profile = new PlayerSettings();
            profile.HitSoundStyle = 2; profile.HitSoundVolume = .37f;
            profile.Save(settingsFixturePath);
            Check(!File.ReadAllText(settingsFixturePath).Contains("scroll_rate"), "fixed speed is not stored as a user preference");
            var reloaded = PlayerSettings.Load(settingsFixturePath);
            Check(reloaded.HitSoundStyle == 2 && Math.Abs(reloaded.HitSoundVolume - .37) < .00001,
                "global audio settings survive profile reload");
            File.AppendAllText(settingsFixturePath, "\nscroll_rate=3.0\n");
            var legacyProfile = PlayerSettings.Load(settingsFixturePath);
            legacyProfile.Save(settingsFixturePath);
            Check(!File.ReadAllText(settingsFixturePath).Contains("scroll_rate") && legacyProfile.HitSoundStyle == 2 &&
                Math.Abs(legacyProfile.HitSoundVolume - .37f) < .00001, "legacy rate drops on settings save while audio preferences survive");
            settings.HitSoundStyle = 1; settings.HitSoundVolume = .61f; SaveUserSettings();
            byte[] globalAudioFile = File.ReadAllBytes(UserSettingsPath);
            int beforeSave = Directory.GetFiles(ChartStore.Root, "*.json").Length;
            OpenEditor(ChartStore.LoadAll().Single(c => c.Id == ChartStore.DemoId));
            Check(editorToolToggle!.GetParent() == page.GetChild(0), "editor opens directly with compact toolbar and no title header");
            Check(editorLeftSidebar!.IsAncestorOf(editTrackSelector!) && editorRightSidebar!.IsAncestorOf(judgeTimeline!),
                "edit tracks and animation controls live in their respective sidebars");
            bool initialTool = LongTool;
            editorToolToggle!.EmitSignal(Godot.Button.SignalName.Pressed);
            Check(LongTool != initialTool && editorToolToggle.Text == (LongTool ? "长条" : "单点"), "compact toolbar toggles tap and hold tools");
            editorToolToggle.EmitSignal(Godot.Button.SignalName.Pressed);
            editorSnapSelector!.EmitSignal(OptionButton.SignalName.ItemSelected, 4L);
            Check(SnapDivision == 16 && !editorSnapSelector.GetItemText(0).Contains("拍"), "toolbar snap selector retains subdivisions without explanatory text");
            SetEditorSidebar(true, false); SetEditorSidebar(false, true);
            Check(!editorLeftSidebar!.Visible && editorRightSidebar!.Visible, "editor sidebars toggle independently");
            SetEditorSidebar(false, false);
            Check(!editorLeftSidebar!.Visible && !editorRightSidebar!.Visible, "both editor sidebars collapse for focused charting");
            SetEditorSidebar(true, true);
            Check(!ReferenceEquals(hitSounds.Settings, settings) && hitSounds.Style == 1 && Math.Abs(hitSounds.Volume - .61f) < .00001,
                "entering editor copies global audio preferences");
            var audioControls = SoundControls(true); AddChild(audioControls);
            var styleControl = (OptionButton)audioControls.GetChild(1);
            styleControl.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            ((HSlider)audioControls.GetChild(2)).Value = 23;
            Check(hitSounds.Style == 2 && Math.Abs(hitSounds.Volume - .23f) < .00001 && settings.HitSoundStyle == 1 && settings.HitSoundVolume == .61f,
                "editor audio controls modify only temporary preferences");
            Check(globalAudioFile.SequenceEqual(File.ReadAllBytes(UserSettingsPath)), "editor audio edits leave saved global settings untouched");
            Check(!dirty, "temporary audio preferences do not dirty the chart");
            audioControls.QueueFree();
            saveButton.EmitSignal(Godot.Button.SignalName.Pressed);
            _Input(new InputEventKey { PhysicalKeycode = Key.S, CtrlPressed = true, Pressed = true });
            PreviewChart();
            Check(ReferenceEquals(hitSounds.Settings, settings) && hitSounds.Style == 1 && hitSounds.Volume == .61f,
                "preview uses global audio instead of editor temporary settings");
            OpenEditor(Chart);
            Check(hitSounds.Style == 1 && hitSounds.Volume == .61f, "returning to editor takes a fresh global audio copy");
            hitSounds.Style = 0; hitSounds.Volume = .1f;
            ShowHome();
            Check(ReferenceEquals(hitSounds.Settings, settings) && hitSounds.Style == 1 && hitSounds.Volume == .61f,
                "leaving editor for home discards temporary audio preferences");
            var homeAudioControls = SoundControls(); AddChild(homeAudioControls);
            ((OptionButton)homeAudioControls.GetChild(1)).EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            ((HSlider)homeAudioControls.GetChild(2)).Value = 45;
            var persistedAudio = PlayerSettings.Load(UserSettingsPath);
            Check(persistedAudio.HitSoundStyle == 2 && Math.Abs(persistedAudio.HitSoundVolume - .45f) < .00001,
                "home audio controls still save global preferences");
            homeAudioControls.QueueFree();
            OpenEditor(Chart);
            Check(hitSounds.Style == 2 && Math.Abs(hitSounds.Volume - .45f) < .00001,
                "new editor session inherits newly changed global audio");
            SaveChart();
            Check(Chart.Id == ChartStore.DemoId && Directory.GetFiles(ChartStore.Root, "*.json").Length == beforeSave,
                "manual save, Ctrl+S and preview return preserve demo ID and file count");
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
            float At(double time) => view.JudgeY - (float)((time - Cursor) * view.PixelsPerSecond);
            LongTool = true;
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(laneX, At(4.125)) }, view);
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(laneX, At(4.75)) }, view);
            Check(Chart.Notes.Count == 3 && Chart.Notes.Last().Duration == .625, "mouse drag creates hold");
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(laneX, At(4.5)) }, view);
            Check(Chart.Notes.Count == 2, "right click deletes hold body");
            Undo(); Undo(); Check(Chart.Notes.Count == 2, "undo restores gesture history");
            foreach (float height in new[] { 360f, 600f, 1000f })
            {
                view.Size = new Vector2(760, height);
                    double noteTimeAtTop = view.TimeAt(0);
                    Check(Math.Abs(view.Y(noteTimeAtTop)) < .001 && Math.Abs(view.Y(noteTimeAtTop - 1) - height) < .001,
                        $"fixed 1s top-to-bottom duration at height {height}");
                    Check(Math.Abs(view.TimeAt(view.Y(Cursor + .25)) - Cursor - .25) < .00001,
                        "editor time/pixel conversion remains invertible");
            }
            LongTool = false;
            TestEditorTiming(Check, testId);
            var baseNotes = Chart.Notes.Select(n => n.Copy()).ToList();
            CreateEditTrack();
            Check(CurrentEditTrackId == 2 && Chart.EditTracks.Count == 2, "new edit track is created and selected");
            RenameEditTrack("钢琴");
            Check(Chart.EditTracks[1].Name == "钢琴", "instrument edit track can be named");
            Check(AddNote(2, 1.5, 1.5) && AddNote(3, 2.5, 2.75) && Chart.Notes.Count(n => n.EditTrackId == 2) == 2,
                "new taps and holds belong to active edit track");
            Check(!AddNote(0, 1, 1), "cross-track overlaps still protect four-key playability");
            SeekEditor(2.25);
            int laneOneX = Enumerable.Range(0, 760).First(x => view.LaneAt(x) == 1) + 10;
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(laneOneX, At(2.5)) }, view);
            Check(Chart.Notes.Count == 4, "inactive edit track hold cannot be deleted");
            SeekEditor(1.25);
            int laneTwoX = Enumerable.Range(0, 760).First(x => view.LaneAt(x) == 2) + 10;
            TrackInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(laneTwoX, At(1.5)) }, view);
            Check(Chart.Notes.Count == 3, "active edit track note can be deleted");
            Undo(); Check(Chart.Notes.Count == 4 && Chart.Notes.Count(n => n.EditTrackId == 2) == 2, "undo retains edit track membership");
            Check(IsCurrentEditTrack(Chart.Notes.First(n => n.EditTrackId == 2)) && !IsCurrentEditTrack(Chart.Notes.First(n => n.EditTrackId == 1)),
                "inactive notes are distinguished for dim rendering");
            int layerHits = hitSounds.PlayedCount;
            PrepareEditorHits(0, true); PlayEditorHitsThrough(48);
            Check(hitSounds.PlayedCount == layerHits + 3, "only active track tap and hold head/tail play editor sounds");
            SelectEditTrack(1); layerHits = hitSounds.PlayedCount;
            PrepareEditorHits(0, true); PlayEditorHitsThrough(48);
            Check(hitSounds.PlayedCount == layerHits + 3, "switching track changes audible notes");
            SelectEditTrack(2); layerHits = hitSounds.PlayedCount;
            audio.Play(2.3f); editorPlaying = true;
            SelectEditTrack(1);
            Check(hitSounds.PlayedCount == layerHits, "switching during playback does not replay elapsed notes");
            PlayEditorHitsThrough(3);
            Check(hitSounds.PlayedCount == layerHits + 1, "switching inside hold preserves upcoming active tail sound");
            audio.Stop(); editorPlaying = false; audioEnded = false;
            Check(SaveChartCore(), "chart saves edit track metadata");
            var layeredReload = ChartStore.LoadAll().Single(c => c.Id == testId);
            Check(layeredReload.EditTracks.Count == 2 && layeredReload.EditTracks[1].Name == "钢琴" && layeredReload.Notes.Count(n => n.EditTrackId == 2) == 2,
                "track names and note membership survive JSON reload");
            SelectEditTrack(2); StartGame(Chart, true);
            Check(NoteStates.Length == 4 && TotalUnits == 6, "preview merges all edit tracks for gameplay");
            Cursor = 1; PressLane(0); ReleaseLane(0); Cursor = 1.5; PressLane(2); ReleaseLane(2);
            Check(perfect == 2, "gameplay judges notes from both edit tracks");
            OpenEditor(Chart);
            Check(CurrentEditTrackId == 2, "preview return keeps current editing track");
            Chart.Notes = baseNotes; Chart.EditTracks = new() { new EditTrack() }; SelectEditTrack(1);
            undo.Clear(); redo.Clear();
            TestJudgeAnimation(Check, testId);
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
            Check(track!.PixelsPerSecond == Math.Max(1, track.Size.Y) && ReferenceEquals(hitSounds.Settings, settings), "preview uses fixed track-height speed and global audio");
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
            var layeredChart = ChartStore.DemoChart();
            layeredChart.EditTracks.Add(new EditTrack { Id = 2, Name = "鼓组" });
            for (int i = 0; i < layeredChart.Notes.Count; i++) if (i % 2 == 0) layeredChart.Notes[i].EditTrackId = 2;
            OpenEditor(layeredChart); SetEditorSidebar(true, true); SetEditorSidebar(false, false);
            SeekEditor(12); await Capture("editor");
            SelectEditTrack(2); await Capture("editor-track2");
            var visualOffsetInput = (SpinBox)editorLeftSidebar!.FindChildren("*", "SpinBox", true, false).Last();
            visualOffsetInput.Value = .025; await Capture("editor-offset-grid");
            visualOffsetInput.Value = 0;
            layeredChart.JudgeLineKeys = new()
            {
                new() { Time = 0, Y = 90 }, new() { Time = 12, Y = 90 },
                new() { Time = 14, Y = 30, Transition = LineTransition.Cubic },
                new() { Time = 16, Y = 90, Transition = LineTransition.Step },
                new() { Time = 18, Y = 0, Transition = LineTransition.Step }, new() { Time = 20, Y = 100 }
            };
            SetEditorSidebar(false, true);
            SeekEditor(13); await Capture("editor-animation");
            SeekEditor(14); await Capture("editor-animation-key");
            SetEditorSidebar(true, false); SetEditorSidebar(false, false); await Capture("editor-focus");
            SetEditorSidebar(true, true); SetEditorSidebar(false, true);
            GetWindow().Size = new Vector2I(1100, 720); await Capture("editor-small");
            GetWindow().Size = new Vector2I(1280, 800);
            StartGame(layeredChart); Cursor = 13; paused = true;
            statsLabel.Text = "SCORE\n0256400\n\nACCURACY\n98.45%\n\nCOMBO\n32";
            await Capture("gameplay");
            Cursor = 18; await Capture("gameplay-line-top");
            Cursor = 20; await Capture("gameplay-line-bottom");
            paused = false; TogglePause(); await Capture("pause"); ClearPause();
            ShowResults(); await Capture("results");
            GD.Print("VISUAL_CHECK_OK"); GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
}
