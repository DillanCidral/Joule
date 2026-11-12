using System.IO;
using System.Media;

namespace BatteryTray.Ui;

public enum Chime { Found, Low }

/// <summary>
/// Os toques dos avisos, sintetizados na hora (sem arquivo de audio), com as
/// notas da pagina de design de 2026-11-10
/// (https://claude.ai/artifact/2N5aV1awJY2sNRJsrQ6JtN). Eram cinco; no teste
/// de fogo ficaram so os dois de bateria lida (o Windows ja toca ao plugar e
/// tirar). Bateria baixa usa onda triangular para se destacar.
/// </summary>
public static class Sounds
{
    private const int Rate = 44100;

    private sealed record Spec(bool Triangle, double Gain, double Length, (double Hz, double At)[] Notes);

    private static readonly Dictionary<Chime, Spec> Specs = new()
    {
        [Chime.Found] = new(false, .1, .34, new[] { (660.0, 0.0), (990.0, .09), (1320.0, .2), (1760.0, .27) }), // "conectou" + "achei" numa frase
        [Chime.Low] = new(true, .11, .30, new[] { (784.0, 0.0), (622.0, .12), (784.0, .34), (622.0, .46) }),
    };

    private static readonly Dictionary<Chime, SoundPlayer> Players = new();

    public static void Play(Chime chime)
    {
        try
        {
            if (!Players.TryGetValue(chime, out var player))
            {
                player = new SoundPlayer(new MemoryStream(Wav(Specs[chime])));
                player.Load();
                Players[chime] = player;
            }
            player.Play(); // assincrono; um toque novo corta o anterior, o que e' o certo aqui
        }
        catch (Exception ex) { Core.Log.Error("Som", ex); }
    }

    /// <summary>PCM 16 bits mono: ataque linear de 12 ms e queda exponencial ate o fim da nota (como o WebAudio da pagina).</summary>
    private static byte[] Wav(Spec s)
    {
        double total = s.Notes.Max(n => n.At) + s.Length + .02;
        int count = (int)(total * Rate);
        var samples = new double[count];
        foreach (var (hz, at) in s.Notes)
        {
            int start = (int)(at * Rate), len = (int)(s.Length * Rate);
            for (int i = 0; i < len && start + i < count; i++)
            {
                double t = (double)i / Rate;
                double env = t < .012 ? t / .012 : Math.Exp(Math.Log(.0001 / 1) * (t - .012) / (s.Length - .012));
                double phase = (hz * t) % 1;
                double wave = s.Triangle ? 4 * Math.Abs(phase - .5) - 1 : Math.Sin(2 * Math.PI * phase);
                samples[start + i] += wave * env * s.Gain * 2.5; // ganho do WebAudio .12 ~ 30% da escala
            }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + count * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(count * 2);
        foreach (double v in samples) w.Write((short)(Math.Clamp(v, -1, 1) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
