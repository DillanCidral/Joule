using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryTray.Ui;

/// <summary>
/// Icone da bandeja "Brasa" (pagina aprovada em 2026-11-10,
/// https://claude.ai/artifact/EcDQAWtAJaq6skqmhzCrLp), desenhado em tempo de
/// execucao na grade de 32. Nao mostra porcentagem (ninguem le o numero ali;
/// ele fica na dica e no painel), so o estado, e cada estado muda de forma:
/// tudo bem = a barra de luz da logo, acesa e sumindo na ponta; bateria baixa
/// = pilula apagada com um trecho vermelho curto; lendo = tres pontos; sem
/// valor = so o contorno. O contorno escuro (halo) segura a leitura sobre
/// qualquer papel de parede com a barra transparente.
/// </summary>
public static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly Color Low = Color.FromArgb(0xF0, 0x45, 0x3A);
    private static readonly Color Ember = Color.FromArgb(0xFF, 0xC2, 0x1F); // amarelo do icone final
    private static readonly Color EmberLight = Color.FromArgb(0xFF, 0xE3, 0x8A); // miolo

    /// <param name="percent">O mais fraco entre os que tem numero; null = sem valor.</param>
    /// <param name="reading">Leitura em curso: tres pontos.</param>
    public static Icon Create(int? percent, int lowThreshold, bool reading = false)
    {
        bool lightBar = ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme") == 1;
        Color glyph = lightBar ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;
        Color halo = lightBar ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(140, 0, 0, 0);
        Color box = lightBar ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(56, 255, 255, 255);

        // Desenha no tamanho que a bandeja usa (16 a 100%, 20 a 125%...), a
        // partir da grade de 32: sem reescala borrada do Windows.
        int size = Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / 32f, size / 32f);

            if (reading)
            {
                Fill(g, box, Pill(2, 9.5f, 28, 13));
                foreach (var (x, alpha) in new[] { (9f, 255), (16f, 191), (23f, 128) })
                    using (var dot = new SolidBrush(Color.FromArgb(alpha, Ember))) g.FillEllipse(dot, x - 2.2f, 13.8f, 4.4f, 4.4f);
            }
            else if (percent is null)
            {
                using var outline = Pill(3, 10.5f, 26, 11);
                using var pen = new Pen(glyph, 2);
                g.DrawPath(pen, outline);
            }
            else if (percent < lowThreshold)
            {
                Fill(g, box, Pill(2, 9.5f, 28, 13));
                Fill(g, Low, Pill(4.5f, 12, 11, 8));
            }
            else
            {
                Fill(g, halo, Pill(0.75f, 8.25f, 30.5f, 15.5f));
                using var pill = Pill(2, 9.5f, 28, 13);
                // A ponta some como na logo: clara no comeco, amarela a 30%, quase transparente no fim.
                using var brush = new LinearGradientBrush(new RectangleF(2, 9.5f, 28, 13), Ember, Ember, LinearGradientMode.Horizontal)
                {
                    InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { EmberLight, Ember, Color.FromArgb(51, Ember) },
                        Positions = new[] { 0f, 0.3f, 1f },
                    },
                };
                g.FillPath(brush, pill);
            }

            if (AppInfo.IsTest)
            {
                // Canal de teste: ponto violeta no canto, com o halo da barra.
                using var haloDot = new SolidBrush(lightBar ? Color.FromArgb(204, 255, 255, 255) : Color.FromArgb(153, 0, 0, 0));
                using var testDot = new SolidBrush(Color.FromArgb(0xB5, 0x7B, 0xFF));
                g.FillEllipse(haloDot, 20, 0, 12, 12);
                g.FillEllipse(testDot, 22, 2, 8, 8);
            }
        }

        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void Fill(Graphics g, Color color, GraphicsPath path)
    {
        using (path)
        using (var brush = new SolidBrush(color))
            g.FillPath(brush, path);
    }

    /// <summary>Pilula deitada: as pontas sao meias-voltas da altura.</summary>
    private static GraphicsPath Pill(float x, float y, float w, float h)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, h, h, 90, 180);
        path.AddArc(x + w - h, y, h, h, 270, 180);
        path.CloseFigure();
        return path;
    }

    private static int ReadDword(string path, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) is int v ? v : 0;
        }
        catch { return 0; }
    }
}
