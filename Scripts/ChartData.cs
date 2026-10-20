using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Oooverdose;

public sealed class ChartNote
{
    public int Lane { get; set; }
    public int EditTrackId { get; set; } = 1;
    public double Time { get; set; }
    public double Duration { get; set; }
    public ChartNote Copy() => new() { Lane = Lane, EditTrackId = EditTrackId, Time = Time, Duration = Duration };
}

public sealed class EditTrack
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "轨道 1";
}

public sealed class ChartData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名谱面";
    public string Artist { get; set; } = "Unknown artist";
    public string MusicPath { get; set; } = "";
    public double Bpm { get; set; } = 120;
    // The time, in seconds, of beat zero in the audio file.
    public double Offset { get; set; }
    public List<ChartNote> Notes { get; set; } = new();
    public List<EditTrack> EditTracks { get; set; } = new() { new EditTrack() };
    public List<JudgeLineKey> JudgeLineKeys { get; set; } = new();
    public bool IsDemo { get; set; }
}

public static class ChartStore
{
    public const string DemoId = "00000000000000000000000000000001";
    public static readonly string Root = ProjectSettings.GlobalizePath(
        OS.GetCmdlineUserArgs().Any(arg => arg == "--self-test" || arg == "--visual-check")
            ? "res://.verification/charts" : "user://charts");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static void Save(ChartData chart)
    {
        Validate(chart);
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, chart.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(chart, Json));
        File.Move(path + ".tmp", path, true);
    }

    public static List<ChartData> LoadAll()
    {
        Directory.CreateDirectory(Root);
        var charts = new List<ChartData>();
        foreach (var file in Directory.GetFiles(Root, "*.json"))
        {
            try
            {
                var chart = JsonSerializer.Deserialize<ChartData>(File.ReadAllText(file));
                if (chart == null) continue;
                Validate(chart);
                charts.Add(chart);
            }
            catch (Exception ex) { GD.PushWarning($"无法读取谱面 {file}: {ex.Message}"); }
        }
        return charts.OrderBy(c => c.IsDemo ? 0 : 1).ThenBy(c => c.Title).ToList();
    }

    public static void Validate(ChartData chart)
    {
        if (chart.Id.Length != 32 || !chart.Id.All(Uri.IsHexDigit)) throw new InvalidDataException("谱面 ID 无效。");
        if (!double.IsFinite(chart.Bpm) || chart.Bpm < 20 || chart.Bpm > 400) throw new InvalidDataException("BPM 必须在 20–400 之间。");
        if (!double.IsFinite(chart.Offset) || Math.Abs(chart.Offset) > 3600) throw new InvalidDataException("偏移值无效。");
        if (chart.Notes == null || chart.Notes.Count > 100000) throw new InvalidDataException("谱面音符数据无效。");
        if (chart.EditTracks == null || chart.EditTracks.Count == 0 || chart.EditTracks.Count > 256 ||
            chart.EditTracks.Any(t => t == null || t.Id < 1 || string.IsNullOrWhiteSpace(t.Name)) ||
            chart.EditTracks.Select(t => t.Id).Distinct().Count() != chart.EditTracks.Count)
            throw new InvalidDataException("编辑轨道数据无效。");
        var editTrackIds = chart.EditTracks.Select(t => t.Id).ToHashSet();
        if (chart.JudgeLineKeys == null || chart.JudgeLineKeys.Count > 100000 || chart.JudgeLineKeys.Any(k => k == null ||
            !double.IsFinite(k.Time) || k.Time < 0 || k.Time > 86400 || !double.IsFinite(k.Y) || k.Y < 0 || k.Y > 100 || !Enum.IsDefined(k.Transition)) ||
            chart.JudgeLineKeys.Select(k => k.Time).Distinct().Count() != chart.JudgeLineKeys.Count)
            throw new InvalidDataException("判定线关键帧数据无效。");
        chart.JudgeLineKeys = chart.JudgeLineKeys.OrderBy(k => k.Time).ToList();
        if (chart.Notes.Any(n => n == null || !editTrackIds.Contains(n.EditTrackId) || n.Lane < 0 || n.Lane > 3 || !double.IsFinite(n.Time) || !double.IsFinite(n.Duration) || n.Time < 0 || n.Duration < 0 || n.Time + n.Duration > 86400))
            throw new InvalidDataException("谱面包含无效音符。");
        chart.Notes = chart.Notes.OrderBy(n => n.Time).ThenBy(n => n.Lane).ToList();
    }

    public static AudioStream LoadAudio(ChartData chart)
    {
        if (chart.IsDemo && string.IsNullOrEmpty(chart.MusicPath)) return DemoAudio();
        if (!File.Exists(chart.MusicPath)) throw new FileNotFoundException("找不到音乐文件，请在编辑器中重新导入音乐。");
        AudioStream? stream = Path.GetExtension(chart.MusicPath).ToLowerInvariant() switch
        {
            ".mp3" => AudioStreamMP3.LoadFromFile(chart.MusicPath),
            ".ogg" => AudioStreamOggVorbis.LoadFromFile(chart.MusicPath),
            ".wav" => AudioStreamWav.LoadFromFile(chart.MusicPath),
            _ => throw new InvalidDataException("支持 MP3、OGG Vorbis 和 WAV 音频。")
        };
        if (stream == null || stream.GetLength() <= 0) throw new InvalidDataException("无法解码音乐，请使用有效的 MP3、OGG 或 WAV 文件。");
        return stream;
    }

    public static ChartData DemoChart()
    {
        var chart = new ChartData { Id = DemoId, Title = "NEON FIRST LIGHT", Artist = "Oooverdose · 原创合成练习曲", Bpm = 120, IsDemo = true };
        for (int beat = 8; beat < 88; beat++)
        {
            int lane = (beat * 7 + beat / 8) % 4;
            chart.Notes.Add(new ChartNote { Lane = lane, Time = beat * .5, Duration = beat % 8 == 0 ? .75 : 0 });
            if (beat >= 24 && beat % 4 == 2) chart.Notes.Add(new ChartNote { Lane = (lane + 2) % 4, Time = beat * .5 + .25 });
        }
        return chart;
    }

    private static AudioStreamWav? demo;
    private static AudioStreamWav DemoAudio()
    {
        if (demo != null) return demo;
        const int rate = 22050;
        var data = new byte[rate * 48 * 2];
        int[] melody = { 0, 7, 12, 7, 3, 10, 15, 10, 5, 12, 17, 12, 7, 14, 19, 14 };
        var random = new Random(42);
        for (int i = 0; i < data.Length / 2; i++)
        {
            double t = (double)i / rate, beat = t * 2, b = beat % 1, eighth = beat * 2 % 1;
            double kick = Math.Sin(2 * Math.PI * (46 * b / 2 + 3 * (1 - Math.Exp(-b * 30)))) * Math.Exp(-b * 13) * .45;
            double hat = (random.NextDouble() * 2 - 1) * Math.Exp(-eighth * 32) * .08;
            double freq = 220 * Math.Pow(2, melody[(int)(beat * 2) % melody.Length] / 12.0);
            double lead = (Math.Sin(t * freq * Math.Tau) + .2 * Math.Sin(t * freq * 2 * Math.Tau)) * Math.Exp(-eighth * 4) * .12;
            double bass = Math.Sin(t * 55 * Math.Pow(2, ((int)(beat / 8) % 4) * 2 / 12.0) * Math.Tau) * .12;
            double fade = Math.Min(1, t / 2) * Math.Clamp((48 - t) / 3, 0, 1);
            short sample = (short)(Math.Clamp((kick + hat + lead + bass) * fade, -1, 1) * 26000);
            data[i * 2] = (byte)sample;
            data[i * 2 + 1] = (byte)(sample >> 8);
        }
        demo = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
        return demo;
    }
}
