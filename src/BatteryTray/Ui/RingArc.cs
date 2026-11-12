using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BatteryTray.Ui;

/// <summary>
/// O arco do percentual no anel do widget (raio 13, traco 3, de cima no
/// sentido horario, como o StrokeDashArray de antes). Anima de
/// <see cref="From"/> ate <see cref="Fraction"/> com a curva das janelas de
/// telas, e enquanto enche leva a faisca amarela na ponta: e' o gesto do
/// Joule (pagina aprovada em 2026-11-10). Sem From, ou From igual, nao anima.
/// Brasa (2026-11-10): o arco some na ponta e o trilho (<see cref="Track"/>)
/// se apaga antes do fim, para o limite da carga nunca aparecer.
/// </summary>
public sealed class RingArc : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(RingArc),
        new PropertyMetadata(0.0, (d, _) => ((RingArc)d).Start()));
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(nameof(From), typeof(double?), typeof(RingArc),
        new PropertyMetadata(null, (d, _) => ((RingArc)d).Start()));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(RingArc),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(RingArc),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RoundCapProperty = DependencyProperty.Register(nameof(RoundCap), typeof(bool), typeof(RingArc),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(double), typeof(RingArc),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double? From { get => (double?)GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public bool RoundCap { get => (bool)GetValue(RoundCapProperty); set => SetValue(RoundCapProperty, value); }

    private bool _sparking;

    public RingArc()
    {
        Loaded += (_, _) => Start();
    }

    private void Start()
    {
        double to = Math.Clamp(Fraction, 0, 1);
        if (From is not double from || Math.Abs(from - to) < 0.001 || !IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(ShownProperty, null);
            SetValue(ShownProperty, to);
            _sparking = false;
            return;
        }
        _sparking = to > from; // a faisca so acompanha quando enche
        var anim = new DoubleAnimation(Math.Clamp(from, 0, 1), to, TimeSpan.FromMilliseconds(760)) { EasingFunction = Motion.Out };
        anim.Completed += (_, _) => { _sparking = false; InvalidateVisual(); };
        BeginAnimation(ShownProperty, anim);
    }

    /// <summary>Opacidade ao longo do arco (0 = comeco, 1 = ponta): plena ate 30%, depois some ate 15%.</summary>
    internal static double ArcAlpha(double t) => t < 0.3 ? 1 : 1 - 0.85 * (t - 0.3) / 0.7;

    /// <summary>Opacidade do trilho ao longo da volta: pleno ate 35%, zero antes de fechar.</summary>
    internal static double TrackAlpha(double t) => t < 0.35 ? 1 : Math.Max(0, 1 - (t - 0.35) / 0.6);

    protected override void OnRender(DrawingContext dc)
    {
        double shown = (double)GetValue(ShownProperty);
        double r = Math.Min(ActualWidth, ActualHeight) / 2 - 1.5;
        if (r <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        Point At(double f) => new(c.X + r * Math.Sin(f * 2 * Math.PI), c.Y - r * Math.Cos(f * 2 * Math.PI));

        // Gradiente ao longo do arco: o WPF nao tem, entao o arco vira trechos curtos de opacidade decrescente.
        void Faded(Color color, double upTo, Func<double, double> alpha, bool roundStart)
        {
            int n = Math.Max(2, (int)Math.Ceiling(upTo * 60));
            for (int i = 0; i < n; i++)
            {
                double a = alpha((i + 0.5) / n);
                if (a <= 0.01) continue;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(color.A * a), color.R, color.G, color.B)), 3)
                {
                    StartLineCap = i == 0 && roundStart ? PenLineCap.Round : PenLineCap.Flat,
                    EndLineCap = PenLineCap.Flat,
                };
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(At(upTo * i / n), false, false);
                    ctx.ArcTo(At(upTo * (i + 1) / n), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        }

        if (Track is SolidColorBrush track) Faded(track.Color, 1, TrackAlpha, false);

        if (shown > 0.001)
        {
            if (Stroke is SolidColorBrush solid) Faded(solid.Color, shown, ArcAlpha, RoundCap);
            else
            {
                var pen = new Pen(Stroke, 3) { StartLineCap = RoundCap ? PenLineCap.Round : PenLineCap.Flat, EndLineCap = RoundCap ? PenLineCap.Round : PenLineCap.Flat };
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(At(0), false, false);
                    ctx.ArcTo(At(Math.Min(shown, 0.9999)), new Size(r, r), 0, shown > 0.5, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        }

        if (_sparking)
        {
            var spark = (Brush)(TryFindResource("Brand.Dot") ?? Brushes.Gold);
            var tip = At(shown);
            var glow = spark.Clone();
            glow.Opacity = 0.35;
            dc.DrawEllipse(glow, null, tip, 4.5, 4.5);
            dc.DrawEllipse(spark, null, tip, 2.6, 2.6);
        }
    }
}
