using Godot;
using System;
using System.IO;

namespace Oooverdose;

/// <summary>One user profile shared by every chart, editor and gameplay session.</summary>
public sealed class PlayerSettings
{
    private double scrollRate = 1;
    private int hitSoundStyle;
    private float hitSoundVolume = .65f;
    public double ScrollRate
    {
        get => scrollRate;
        set => scrollRate = double.IsFinite(value) ? Math.Round(Math.Clamp(value, .2, 3) * 10, MidpointRounding.AwayFromZero) / 10 : 1;
    }
    public int HitSoundStyle { get => hitSoundStyle; set => hitSoundStyle = Math.Clamp(value, 0, 2); }
    public float HitSoundVolume { get => hitSoundVolume; set => hitSoundVolume = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : .65f; }

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
        settings.ScrollRate = Number("scroll_rate", 1);
        double style = Number("hit_sound_style", 0);
        settings.HitSoundStyle = double.IsFinite(style) ? (int)Math.Clamp(style, 0, 2) : 0;
        settings.HitSoundVolume = (float)Number("hit_sound_volume", .65);
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var config = new ConfigFile();
        config.SetValue("user", "scroll_rate", ScrollRate);
        config.SetValue("user", "hit_sound_style", HitSoundStyle);
        config.SetValue("user", "hit_sound_volume", HitSoundVolume);
        if (config.Save(path + ".tmp") != Error.Ok) throw new IOException("无法写入全局用户设置。");
        File.Move(path + ".tmp", path, true);
    }
}
