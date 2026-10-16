namespace BoardCompanion;

// Starter soundboard pack, synthesised in code (no copyrighted audio). Written once into the SFX folder; never overwrites files.
public static class StarterSfx
{
    const int Rate = 44100;
    static readonly Random rng = new(7);
    delegate double Gen(double t, double len);
    static (string name, double len, Gen g)[] Pack => new (string, double, Gen)[]
    {
        ("Air Horn", 1.4, (t, l) => Env(t, l, .02, .25) * (Saw(t * 466) * .5 + Saw(t * 470) * .3 + Saw(t * 233) * .3)),
        ("Ding", 1.2, (t, l) => Math.Exp(-t * 4) * (Math.Sin(Tau * 1320 * t) * .7 + Math.Sin(Tau * 2640 * t) * .2)),
        ("Whoosh", 0.9, (t, l) => Env(t, l, .35, .4) * Noise() * (.4 + .6 * Math.Sin(Math.PI * t / l))),
        ("Laser", 0.5, (t, l) => Math.Exp(-t * 5) * Sq(Tau * (1800 * Math.Exp(-t * 6) + 200) * t)),
        ("Boing", 0.8, (t, l) => Math.Exp(-t * 3.5) * Math.Sin(Tau * (180 + 120 * Math.Sin(Tau * 9 * t)) * t)),
        ("Victory", 1.6, (t, l) => Notes(t, new[] { 523.3, 659.3, 784.0, 1046.5 }, .16, l)),
        ("Fail", 1.8, (t, l) => Notes(t, new[] { 392.0, 370.0, 349.2, 293.7 }, .4, l, true)),
        ("Bass Drop", 1.5, (t, l) => Env(t, l, .01, .4) * Math.Tanh(2.5 * Math.Sin(Tau * (110 * Math.Exp(-t * 1.6) + 30) * t))),
        ("Pop", 0.25, (t, l) => Math.Exp(-t * 30) * Math.Sin(Tau * (900 * Math.Exp(-t * 20) + 120) * t)),
        ("Alarm", 1.6, (t, l) => Env(t, l, .01, .1) * Sq(Tau * ((int)(t * 4) % 2 == 0 ? 880 : 660) * t) * .6),
        ("Drumroll", 1.8, (t, l) => Env(t, l, .4, .05) * Noise() * Math.Exp(-(t * 22 % 1) * 9) + (t > l - .25 ? Math.Exp(-(t - (l - .25)) * 9) * Noise() : 0)),
        ("Applause", 2.0, (t, l) => Env(t, l, .25, .6) * Noise() * (.5 + .5 * Math.Abs(Math.Sin(Tau * 13 * t + Math.Sin(t * 50))))),
    };
    const double Tau = Math.PI * 2;
    static double Saw(double ph) => 2 * (ph - Math.Floor(ph + .5));
    static double Sq(double ph) => Math.Sin(ph) >= 0 ? .5 : -.5;
    static double Noise() => rng.NextDouble() * 2 - 1;
    static double Env(double t, double len, double a, double r) => Math.Min(1, Math.Min(t / a, (len - t) / r));
    static double Notes(double t, double[] f, double step, double len, bool slide = false)
    {
        int i = Math.Min(f.Length - 1, (int)(t / step)); double lt = t - i * step; double fr = f[i] * (slide && i == f.Length - 1 ? (1 - .04 * Math.Sin(Tau * 6 * lt)) : 1);
        double e = (i == f.Length - 1 ? Math.Exp(-lt * 2) : Math.Exp(-lt * 6)) * Math.Min(1, lt / .01);
        return e * (Saw(fr * t) * .4 + Math.Sin(Tau * fr * t) * .5);
    }
    public static int WriteMissing(string folder)
    {
        int n = 0; Directory.CreateDirectory(folder);
        foreach (var (name, len, g) in Pack)
        {
            var path = Path.Combine(folder, name + ".wav"); if (File.Exists(path)) continue;
            int cnt = (int)(len * Rate); var s = new double[cnt]; double peak = 1e-9;
            for (int i = 0; i < cnt; i++) { s[i] = g(i / (double)Rate, len); peak = Math.Max(peak, Math.Abs(s[i])); }
            using var fs = File.Create(path); using var w = new BinaryWriter(fs);
            w.Write("RIFF"u8.ToArray()); w.Write(36 + cnt * 2); w.Write("WAVEfmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(cnt * 2);
            for (int i = 0; i < cnt; i++) w.Write((short)Math.Clamp(s[i] / peak * .89 * 32767, -32768, 32767));
            n++;
        }
        return n;
    }
}
