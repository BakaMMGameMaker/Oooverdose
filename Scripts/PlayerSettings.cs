using Godot;
using System;
using System.IO;

namespace Oooverdose;

/// <summary>Global audio preferences; the editor uses a temporary copy.</summary>
public sealed class PlayerSettings
{
    private int hitSoundStyle;
    private float hitSoundVolume = .65f;
    public int HitSoundStyle { get => hitSoundStyle; set => hitSoundStyle = Math.Clamp(value, 0, 2); }
    public float HitSoundVolume { get => hitSoundVolume; set => hitSoundVolume = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : .65f; }
    public PlayerSettings Copy() => new() { HitSoundStyle = HitSoundStyle, HitSoundVolume = HitSoundVolume };

    public static PlayerSettings Load(string path)
    {
        var settings = new PlayerSettings();
        using var config = new ConfigFile();
        if (config.Load(path) != Error.Ok) return settings;
        double Number(string name, double fallback)
        {
            var value = config.GetValue("user", name, fallback);
            return value.VariantType is Variant.Type.Int or Variant.Type.Float ? value.AsDouble() : fallback;
        }
        double style = Number("hit_sound_style", 0);
        settings.HitSoundStyle = double.IsFinite(style) ? (int)Math.Clamp(style, 0, 2) : 0;
        settings.HitSoundVolume = (float)Number("hit_sound_volume", .65);
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var config = new ConfigFile();
        config.SetValue("user", "hit_sound_style", HitSoundStyle);
        config.SetValue("user", "hit_sound_volume", HitSoundVolume);
        if (config.Save(path + ".tmp") != Error.Ok) throw new IOException("无法写入全局用户设置。");
        File.Move(path + ".tmp", path, true);
    }
}
