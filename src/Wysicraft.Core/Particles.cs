using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>Everything a <see cref="ParticleEffect"/> does: its limits, the presets to start from, and the simulation.
/// The runtime's simulation in wysicraft-web.js is the same arithmetic in the same order; this one exists so the editor
/// can show an effect without a browser. Keep the two in step.</summary>
public static class Particles
{
    /// <summary>Alive at once across a screen, and named effects a project may keep.</summary>
    public const int MaxParticles = 2000, MaxPerProject = 64;
    public static readonly string[] Emissions = ["burst", "stream"];
    public static readonly string[] Shapes = ["square", "circle", "line", "texture"];
    public static readonly string[] Blends = ["normal", "add"];

    public static ParticleEffect Copy(this ParticleEffect e) => Json.Clone(e);

    public static void Check(this ParticleEffect e)
    {
        if (!Validation.Id(e.Id)) throw new InvalidDataException("Particle effect needs a name of letters, numbers and underscores.");
        if (!Emissions.Contains(e.Emission)) throw new InvalidDataException("Particle emission must be burst or stream.");
        if (!Shapes.Contains(e.Shape)) throw new InvalidDataException("Particle shape must be square, circle, line or texture.");
        if (!Blends.Contains(e.Blend)) throw new InvalidDataException("Particle blend must be normal or add.");
        double[] all = [e.Duration, e.Life, e.LifeVariance, e.Direction, e.Spread, e.Speed, e.SpeedVariance, e.Gravity, e.Drag, e.SizeStart, e.SizeEnd, e.SizeVariance, e.OpacityStart, e.OpacityEnd, e.Spin, e.Radius];
        if (all.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Particle settings must be numbers.");
        if (e.Count is < 1 or > MaxParticles) throw new InvalidDataException($"Count is 1–{MaxParticles}.");
        if (e.Life is < 0.02 or > 20 || e.LifeVariance is < 0 or > 1) throw new InvalidDataException("Life is 0.02–20 s and its variance 0–1.");
        if (e.Duration is < 0 or > 60) throw new InvalidDataException("Duration is 0–60 s (0 = until stopped).");
        if (e.Spread is < 0 or > 360) throw new InvalidDataException("Spread is 0–360°.");
        if (e.Speed is < 0 or > 4000 || e.SpeedVariance is < 0 or > 1) throw new InvalidDataException("Speed is 0–4000 px/s and its variance 0–1.");
        if (Math.Abs(e.Gravity) > 8000 || e.Drag is < 0 or > 20) throw new InvalidDataException("Gravity is -8000–8000 px/s² and drag 0–20.");
        if (e.SizeStart is < 0 or > 256 || e.SizeEnd is < 0 or > 256 || e.SizeVariance is < 0 or > 1) throw new InvalidDataException("Sizes are 0–256 px and their variance 0–1.");
        if (Math.Max(e.SizeStart, e.SizeEnd) < 0.1) throw new InvalidDataException("A particle needs some size to be seen.");
        if (e.OpacityStart is < 0 or > 1 || e.OpacityEnd is < 0 or > 1) throw new InvalidDataException("Opacity is 0–1.");
        if (Math.Abs(e.Spin) > 2000) throw new InvalidDataException("Spin is -2000–2000 °/s.");
        if (e.Radius is < 0 or > 512) throw new InvalidDataException("Start radius is 0–512 px.");
        if (!Colour(e.ColorStart, out _) || !Colour(e.ColorEnd, out _)) throw new InvalidDataException("Particle colours must be #RRGGBB.");
        if (e.Colors.Count > MaxStops) throw new InvalidDataException($"A colour ramp has at most {MaxStops} stops.");
        foreach (var stop in e.Colors)
        {
            if (!double.IsFinite(stop.At) || stop.At < 0 || stop.At > 1) throw new InvalidDataException("A colour stop sits between 0 and 1 of a particle's life.");
            if (!Colour(stop.Color, out _)) throw new InvalidDataException("Particle colours must be #RRGGBB.");
        }
        if (e.Shape == "texture" && e.Texture.Length == 0) throw new InvalidDataException("A texture particle needs a texture.");
        // A stream that emits faster than its particles die would fill the pool on its own; a bounded one cannot.
        if (e.Emission == "stream" && e.Count * e.Life > MaxParticles) throw new InvalidDataException($"A stream of {e.Count}/s living {e.Life:0.00} s needs more than {MaxParticles} particles: lower the rate or the life.");
    }

    public const int MaxStops = 8;
    /// <summary>The colour ramp, sorted and always at least two stops. An effect with no ramp of its own uses the
    /// ColorStart/ColorEnd pair it was saved with, so nothing made before ramps existed changes appearance.</summary>
    public static List<ParticleStop> Ramp(this ParticleEffect e)
    {
        if (e.Colors.Count >= 2) return [.. e.Colors.OrderBy(s => s.At)];
        if (e.Colors.Count == 1) return [e.Colors[0], new ParticleStop { At = 1, Color = e.Colors[0].Color }];
        return [new ParticleStop { At = 0, Color = e.ColorStart }, new ParticleStop { At = 1, Color = e.ColorEnd }];
    }
    /// <summary>The colour at a point through a particle's life, 0 to 1, walking the ramp.</summary>
    public static (int R, int G, int B) ColourAt(this ParticleEffect e, double t)
    {
        var ramp = e.Ramp();
        if (t <= ramp[0].At) { Colour(ramp[0].Color, out var first); return first; }
        for (int i = 1; i < ramp.Count; i++)
        {
            if (t > ramp[i].At) continue;
            double span = ramp[i].At - ramp[i - 1].At;
            double k = span <= 0 ? 1 : (t - ramp[i - 1].At) / span;
            Colour(ramp[i - 1].Color, out var a); Colour(ramp[i].Color, out var b);
            return ((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
        }
        Colour(ramp[^1].Color, out var last); return last;
    }
    public static bool Colour(string value, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (value.Length != 7 || value[0] != '#') return false;
        if (!int.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out int n)) return false;
        rgb = ((n >> 16) & 255, (n >> 8) & 255, n & 255); return true;
    }

    // ---- Presets ----
    public static readonly string[] Presets = ["sparks", "explosion", "smoke", "fountain", "sparkle", "blood", "rain", "magic", "trail", "confetti", "random"];
    public static ParticleEffect Preset(string preset, Random? random = null)
    {
        var r = random ?? new Random(); double R(double a, double b) => a + r.NextDouble() * (b - a);
        var p = new ParticleEffect { Id = preset };
        switch (preset)
        {
            case "sparks":
                p.Count = (int)R(14, 28); p.Life = R(0.3, 0.6); p.Speed = R(150, 260); p.SpeedVariance = 0.6; p.Drag = R(2, 4);
                p.SizeStart = R(2, 3); p.SizeEnd = 0; p.ColorStart = "#FFF2C0"; p.ColorEnd = "#E8683A"; p.Blend = "add"; p.Shape = "line"; p.Gravity = R(120, 300); break;
            case "explosion":
                p.Count = (int)R(40, 70); p.Life = R(0.5, 0.9); p.LifeVariance = 0.5; p.Speed = R(200, 340); p.SpeedVariance = 0.7; p.Drag = R(2.5, 4.5);
                p.SizeStart = R(5, 9); p.SizeEnd = R(0, 2); p.ColorStart = "#F2E27A"; p.ColorEnd = "#8A2A3A"; p.Blend = "add"; p.Shape = "circle"; p.Radius = R(0, 8); break;
            case "smoke":
                p.Emission = "stream"; p.Count = (int)R(14, 26); p.Life = R(1.2, 2.2); p.Direction = 270; p.Spread = R(25, 50); p.Speed = R(20, 45); p.Drag = R(0.4, 1);
                p.SizeStart = R(4, 7); p.SizeEnd = R(14, 22); p.ColorStart = "#8A8A8A"; p.ColorEnd = "#2A2A2A"; p.OpacityStart = R(0.35, 0.6); p.Gravity = -R(5, 20); p.Radius = R(2, 6); break;
            case "fountain":
                p.Emission = "stream"; p.Count = (int)R(50, 90); p.Life = R(0.8, 1.4); p.Direction = 270; p.Spread = R(25, 55); p.Speed = R(150, 240); p.SpeedVariance = 0.3;
                p.Gravity = R(350, 550); p.Drag = 0.2; p.SizeStart = R(2, 4); p.SizeEnd = R(1, 2); p.ColorStart = "#8AD8F2"; p.ColorEnd = "#2A6AC8"; break;
            case "sparkle":
                p.Count = (int)R(10, 18); p.Life = R(0.6, 1.1); p.LifeVariance = 0.6; p.Speed = R(20, 50); p.Drag = R(1, 2); p.Radius = R(8, 20);
                p.SizeStart = R(1.5, 3); p.SizeEnd = 0; p.ColorStart = "#FFFFFF"; p.ColorEnd = "#F2C240"; p.Blend = "add"; p.Gravity = -R(10, 40); break;
            case "blood":
                p.Count = (int)R(16, 30); p.Life = R(0.4, 0.8); p.Speed = R(90, 190); p.SpeedVariance = 0.7; p.Drag = R(1.5, 3);
                p.Gravity = R(500, 800); p.SizeStart = R(2, 4); p.SizeEnd = R(1, 2); p.ColorStart = "#C2413A"; p.ColorEnd = "#5A1A1A"; break;
            case "rain":
                p.Emission = "stream"; p.Count = (int)R(60, 120); p.Life = R(1, 1.6); p.Direction = R(80, 100); p.Spread = R(2, 10); p.Speed = R(380, 560); p.SpeedVariance = 0.15;
                p.Gravity = R(100, 300); p.Drag = 0; p.SizeStart = R(1, 2); p.SizeEnd = R(1, 2); p.Shape = "line"; p.ColorStart = "#9AC2E8"; p.ColorEnd = "#5A7A9A"; p.OpacityStart = 0.7; p.Radius = R(150, 300); break;
            case "magic":
                p.Count = (int)R(20, 36); p.Life = R(0.7, 1.2); p.Speed = R(60, 130); p.SpeedVariance = 0.5; p.Drag = R(1.5, 3); p.Radius = R(4, 14);
                p.SizeStart = R(2, 4); p.SizeEnd = 0; p.ColorStart = "#C8A0E8"; p.ColorEnd = "#3A6AC8"; p.Blend = "add"; p.Spin = R(-200, 200); p.Gravity = -R(20, 60); break;
            case "trail":
                p.Emission = "stream"; p.Count = (int)R(24, 44); p.Life = R(0.25, 0.5); p.Speed = R(5, 25); p.Spread = 360; p.Drag = R(2, 4);
                p.SizeStart = R(3, 5); p.SizeEnd = 0; p.ColorStart = "#F2A03A"; p.ColorEnd = "#C2413A"; p.Blend = "add"; break;
            case "confetti":
                p.Count = (int)R(30, 60); p.Life = R(1.2, 2.2); p.LifeVariance = 0.4; p.Direction = 270; p.Spread = R(60, 120); p.Speed = R(180, 300); p.SpeedVariance = 0.5;
                p.Gravity = R(250, 420); p.Drag = R(0.8, 1.6); p.SizeStart = R(3, 5); p.SizeEnd = R(3, 5); p.Shape = "square"; p.Spin = R(200, 600);
                p.ColorStart = "#E8558A"; p.ColorEnd = "#F2C240"; break;
            default:
                p.Id = "random";
                p.Emission = r.Next(3) == 0 ? "stream" : "burst";
                p.Count = (int)R(10, p.Emission == "stream" ? 60 : 80); p.Life = R(0.3, 1.6); p.LifeVariance = R(0, 0.7);
                p.Direction = R(0, 360); p.Spread = r.Next(3) == 0 ? R(15, 90) : 360; p.Speed = R(40, 320); p.SpeedVariance = R(0.1, 0.8);
                p.Gravity = r.Next(2) == 0 ? R(-200, 700) : 0; p.Drag = R(0, 4); p.Radius = r.Next(3) == 0 ? R(4, 40) : 0;
                p.SizeStart = R(1.5, 8); p.SizeEnd = r.Next(2) == 0 ? 0 : R(0.5, 10); p.Spin = r.Next(3) == 0 ? R(-400, 400) : 0;
                p.Shape = Shapes[r.Next(3)]; p.Blend = r.Next(2) == 0 ? "add" : "normal";
                p.ColorStart = Wheel(r); p.ColorEnd = Wheel(r); p.OpacityStart = R(0.6, 1); p.OpacityEnd = r.Next(3) == 0 ? R(0, 0.4) : 0;
                break;
        }
        // A stream that would overrun the pool is pulled back rather than refused.
        if (p.Emission == "stream" && p.Count * p.Life > MaxParticles) p.Count = Math.Max(1, (int)(MaxParticles / p.Life));
        return p;
    }
    static string Wheel(Random r) => $"#{r.Next(48, 256):X2}{r.Next(48, 256):X2}{r.Next(48, 256):X2}";

    /// <summary>A close variation: every setting nudged a little.</summary>
    public static ParticleEffect Mutate(this ParticleEffect e, Random? random = null)
    {
        var r = random ?? new Random(); var m = e.Copy();
        double N(double v, double spread, double lo, double hi) => Math.Clamp(v + (r.NextDouble() * 2 - 1) * spread, lo, hi);
        m.Count = (int)Math.Clamp(e.Count + (r.NextDouble() * 2 - 1) * Math.Max(2, e.Count * 0.3), 1, MaxParticles);
        m.Life = N(e.Life, e.Life * 0.3, 0.02, 20); m.Speed = N(e.Speed, e.Speed * 0.3 + 5, 0, 4000); m.SpeedVariance = N(e.SpeedVariance, 0.15, 0, 1);
        m.Direction = (e.Direction + (r.NextDouble() * 2 - 1) * 25 + 360) % 360; m.Spread = N(e.Spread, 30, 0, 360);
        m.Gravity = N(e.Gravity, Math.Abs(e.Gravity) * 0.3 + 20, -8000, 8000); m.Drag = N(e.Drag, 0.5, 0, 20);
        m.SizeStart = N(e.SizeStart, e.SizeStart * 0.3 + 0.4, 0, 256); m.SizeEnd = N(e.SizeEnd, e.SizeEnd * 0.3 + 0.4, 0, 256);
        m.Radius = N(e.Radius, e.Radius * 0.3 + 1, 0, 512); m.Spin = N(e.Spin, Math.Abs(e.Spin) * 0.3 + 10, -2000, 2000);
        // The ramp keeps its colours but the moments they arrive move, which is what makes a mutation feel different.
        m.Colors = e.Ramp().Select((stop, i) => new ParticleStop {
            At = i == 0 ? 0 : i == e.Ramp().Count - 1 ? 1 : Math.Clamp(stop.At + (r.NextDouble() * 2 - 1) * 0.18, 0.02, 0.98),
            Color = stop.Color }).ToList();
        if (Math.Max(m.SizeStart, m.SizeEnd) < 0.5) m.SizeStart = 2;
        if (m.Emission == "stream" && m.Count * m.Life > MaxParticles) m.Count = Math.Max(1, (int)(MaxParticles / m.Life));
        return m;
    }

    // ---- Simulation, mirrored from the runtime ----
    public sealed class Particle { public double X, Y, VX, VY, Age, Life, Size, Angle, Spin; }

    /// <summary>One particle, laid out the way the runtime lays it out.</summary>
    public static Particle Spawn(this ParticleEffect e, Random r, double x, double y)
    {
        double half = e.Spread / 2 * Math.PI / 180, mid = e.Direction * Math.PI / 180;
        double angle = mid + (r.NextDouble() * 2 - 1) * half;
        double speed = e.Speed * (1 + (r.NextDouble() * 2 - 1) * e.SpeedVariance);
        double from = e.Radius > 0 ? Math.Sqrt(r.NextDouble()) * e.Radius : 0, around = r.NextDouble() * Math.PI * 2;
        return new Particle
        {
            X = x + Math.Cos(around) * from, Y = y + Math.Sin(around) * from,
            VX = Math.Cos(angle) * speed, VY = Math.Sin(angle) * speed,
            Age = 0, Life = Math.Max(0.02, e.Life * (1 + (r.NextDouble() * 2 - 1) * e.LifeVariance)),
            Size = 1 + (r.NextDouble() * 2 - 1) * e.SizeVariance, Angle = r.NextDouble() * Math.PI * 2, Spin = e.Spin * Math.PI / 180
        };
    }
    /// <summary>Moves one particle on; false once it has died.</summary>
    public static bool Step(this ParticleEffect e, Particle p, double dt)
    {
        p.Age += dt; if (p.Age >= p.Life) return false;
        double keep = Math.Max(0, 1 - e.Drag * dt);
        p.VX *= keep; p.VY = p.VY * keep + e.Gravity * dt;
        p.X += p.VX * dt; p.Y += p.VY * dt; p.Angle += p.Spin * dt;
        return true;
    }
    /// <summary>Size, colour and opacity at a particle's age, as the runtime draws it.</summary>
    public static (double Size, (int R, int G, int B) Rgb, double Alpha) At(this ParticleEffect e, Particle p)
    {
        double t = Math.Clamp(p.Age / p.Life, 0, 1);
        return ((e.SizeStart + (e.SizeEnd - e.SizeStart) * t) * p.Size, e.ColourAt(t),
            e.OpacityStart + (e.OpacityEnd - e.OpacityStart) * t);
    }
}
