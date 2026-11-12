using System.Windows.Markup;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>Texto nas duas linguas no XAML: Text="{ui:Tr 'Voltar', 'Back'}". Ver Core.L.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class Tr : MarkupExtension
{
    public string Pt { get; set; }
    public string En { get; set; }

    public Tr(string pt, string en) { Pt = pt; En = en; }

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Pt, En);
}
