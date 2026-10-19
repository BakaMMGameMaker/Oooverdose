using Godot;
using System;

namespace Oooverdose;

/// <summary>Short original PCM hits. Pre-generated and pooled for low-latency chords.</summary>
public partial class HitSoundPlayer : Node
{
    public PlayerSettings Settings { get; set; } = new();
    public int Style { get => Settings.HitSoundStyle; set => Settings.HitSoundStyle = value; }
    public float Volume { get => Settings.HitSoundVolume; set => Settings.HitSoundVolume = value; }
    public int PlayedCount { get; private set; }
    private readonly AudioStreamPlayer[] voices = new AudioStreamPlayer[16];
    private readonly AudioStreamWav[] samples = new AudioStreamWav[3];
    private int nextVoice;

    public override void _Ready()
    {
        for (int i = 0; i < samples.Length; i++) samples[i] = Synthesize(i);
        for (int i = 0; i < voices.Length; i++)
        {
            voices[i] = new AudioStreamPlayer();
            AddChild(voices[i]);
        }
    }

    public void PlayHit()
    {
        if (Volume <= 0) return;
        var player = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        player.Stop();
        player.Stream = samples[Math.Clamp(Style, 0, samples.Length - 1)];
        player.VolumeDb = Mathf.LinearToDb(Math.Clamp(Volume, .0001f, 1)) - 3;
        player.Play();
        PlayedCount++;
    }

    public void StopAll()
    {
        foreach (var voice in voices) voice.Stop();
    }

    public static AudioStreamWav Synthesize(int style)
    {
        bool bright = style == 1, deep = style == 2;
        const int rate = 44100;
        int count = (int)(rate * (deep ? .12 : bright ? .065 : .085));
        var pcm = new byte[count * 2];
        var random = new Random(bright ? 302 : 701);
        double filteredNoise = 0;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / rate;
            double noise = random.NextDouble() * 2 - 1;
            filteredNoise += .28 * (noise - filteredNoise);
            double sound;
            if (deep)
            {
                // A soft "pu": rounded low body, falling pitch and very little treble.
                double phase = Math.Tau * (88 * t + .62 * (1 - Math.Exp(-t * 65)));
                sound = .85 * Math.Sin(phase) * Math.Exp(-t * 48)
                      + .13 * Math.Sin(phase * 2) * Math.Exp(-t * 85)
                      + .07 * filteredNoise * Math.Exp(-t * 100);
            }
            else if (bright)
            {
                // A bright, short rhythm-game tick with a tiny bell-like body.
                sound = .40 * Math.Sin(Math.Tau * 1800 * t) * Math.Exp(-t * 135)
                      + .22 * Math.Sin(Math.Tau * 3100 * t) * Math.Exp(-t * 210)
                      + .35 * noise * Math.Exp(-t * 500);
            }
            else
            {
                // Low wooden/stone body, gritty transient, rapidly falling pitch.
                double phase = Math.Tau * (185 * t + 2.5 * (1 - Math.Exp(-t * 95)));
                sound = .62 * Math.Sin(phase) * Math.Exp(-t * 80)
                      + .32 * filteredNoise * Math.Exp(-t * 120)
                      + .18 * noise * Math.Exp(-t * 650);
            }
            double envelope = Math.Min(1, t / (deep ? .0025 : .0005)) * Math.Min(1, (count - i - 1) / (rate * .004));
            short value = (short)(Math.Clamp(sound * envelope, -1, 1) * 29000);
            pcm[i * 2] = (byte)value;
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = pcm };
    }
}
