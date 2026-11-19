using System.Buffers.Binary;
using BatteryTray.Core;
using BatteryTray.Interop;
using BatteryTray.Providers;

namespace BatteryTray.Diag;

internal static partial class SelfTest
{
    private static void RunAtkTests()
    {
        // Cabecalhos das capturas locais, com report ID zero acrescentado pelo Windows.
        var mouse = AtkMouseReport(0x1F);
        Check("atk pixart 31%", AtkMouseProvider.Parse(mouse) == (31, false));
        Check("atk pixart 31% carregando", AtkMouseProvider.Parse(AtkMouseReport(0x9F)) == (31, true));
        Check("atk pixart 51% carregando, resposta 82 08", AtkMouseProvider.Parse(AtkMouseReport(0xB3, battery: true)) == (51, true));
        Check("atk pixart zero valido", AtkMouseProvider.Parse(AtkMouseReport(0)) == (0, false));
        Check("atk pixart 100%", AtkMouseProvider.Parse(AtkMouseReport(100)) == (100, false));
        Check("atk pixart acima de 100 rejeitado", AtkMouseProvider.Parse(AtkMouseReport(101)) is null);
        Check("atk pixart bit de carga nao mascara valor invalido", AtkMouseProvider.Parse(AtkMouseReport(0xFF)) is null);
        Check("atk pixart pacote USB sem prefixo Windows rejeitado", AtkMouseProvider.Parse(mouse[1..]) is null);
        Check("atk pixart curto rejeitado", AtkMouseProvider.Parse(mouse[..10]) is null);
        Check("atk pixart vazio rejeitado", AtkMouseProvider.Parse(Array.Empty<byte>()) is null);
        var badMouse = (byte[])mouse.Clone(); badMouse[0] = 1;
        Check("atk pixart outro report ID rejeitado", AtkMouseProvider.Parse(badMouse) is null);
        badMouse = (byte[])mouse.Clone(); badMouse[1] = 0xFA; badMouse[2] = 0x0F;
        Check("atk pixart sensor rejeitado", AtkMouseProvider.Parse(badMouse) is null);
        badMouse = (byte[])mouse.Clone(); badMouse[2] = 0x08;
        Check("atk pixart comandos cruzados rejeitados", AtkMouseProvider.Parse(badMouse) is null);
        badMouse = (byte[])mouse.Clone(); badMouse[10] = 0;
        Check("atk pixart assinatura incompleta rejeitada", AtkMouseProvider.Parse(badMouse) is null);

        var expectedMouseRequest = new byte[9]; expectedMouseRequest[1] = 0x82;
        Check("atk pixart consulta capturada de nove bytes", AtkMouseProvider.BatteryRequest().SequenceEqual(expectedMouseRequest));
        CheckAtkFeatureGate(expectedMouseRequest);

        var version = new byte[64]; version[0] = 0xAA; version[1] = 1; version[5] = 7;
        var pointer = MercuryMemoryReply(0x0100FE5C);
        var requests = new List<byte[]>();
        foreach (uint capacity in new uint[] { 100, 93, 89, 0 })
        {
            requests.Clear();
            var replies = new Queue<byte[]?>(new[] { version, pointer, MercuryMemoryReply(capacity) });
            var reading = MercuryProvider.ReadBattery(request => { requests.Add(request); return replies.Dequeue(); });
            Check($"mercury capacidade {capacity}", reading.Status == ReadingStatus.Ok && reading.Percent == capacity && reading.ChargeText is null);
            var expectedVersion = new byte[64]; expectedVersion[0] = 0xAA; expectedVersion[1] = 1;
            var expectedPointer = new byte[64];
            new byte[] { 0xAA, 3, 0, 1, 4, 1, 0, 0xFE, 0x94 }.CopyTo(expectedPointer, 0);
            var expectedCapacity = (byte[])expectedPointer.Clone(); expectedCapacity[8] = 0x78;
            Check("mercury somente tres pedidos fixos", requests.Count == 3
                && requests[0].SequenceEqual(expectedVersion) && requests[1].SequenceEqual(expectedPointer)
                && requests[2].SequenceEqual(expectedCapacity));
        }

        var wrongVersion = (byte[])version.Clone(); wrongVersion[5] = 8;
        CheckMercuryStops("sem resposta de versao", new byte[]?[] { null }, 1);
        CheckMercuryStops("versao diferente", new[] { wrongVersion }, 1);
        CheckMercuryStops("versao truncada", new[] { version[..6] }, 1);
        var wrongHeader = (byte[])version.Clone(); wrongHeader[1] = 3;
        CheckMercuryStops("resposta a outro comando", new[] { wrongHeader }, 1);
        CheckMercuryStops("ponteiro diferente", new[] { version, MercuryMemoryReply(0x0100FE60) }, 2);
        CheckMercuryStops("sem ponteiro", new byte[]?[] { version, null }, 2);
        CheckMercuryStops("sem capacidade", new byte[]?[] { version, pointer, null }, 3);
        CheckMercuryStops("capacidade acima de 100", new[] { version, pointer, MercuryMemoryReply(101) }, 3);
        CheckMercuryStops("capacidade usa os quatro bytes", new[] { version, pointer, MercuryMemoryReply(0x1000059) }, 3);
        var wrongCount = (byte[])pointer.Clone(); wrongCount[2] = 3;
        CheckMercuryStops("contagem incorreta", new[] { version, wrongCount }, 2);
        Check("mercury pacote curto rejeitado", MercuryProvider.ParseMemory(pointer[..7]) is null);
        Check("mercury pacote vazio rejeitado", MercuryProvider.ParseMemory(Array.Empty<byte>()) is null);
        var padded = MercuryMemoryReply(89); Array.Fill(padded, (byte)0xFF, 7, 57);
        Check("mercury ignora lixo apos quatro bytes declarados", MercuryProvider.ParseMemory(padded) == 89);

        var target = MercuryCollection();
        Check("mercury colecao validada", MercuryProvider.IsTarget(target));
        Check("mercury revisao diferente rejeitada", !MercuryProvider.IsTarget(MercuryCollection(version: 6)));
        Check("mercury outra colecao rejeitada", !MercuryProvider.IsTarget(MercuryCollection(path: "&mi_00&col03")));
        Check("mercury outro tamanho rejeitado", !MercuryProvider.IsTarget(MercuryCollection(featureLen: 65)));
        var mercury = new MercuryProvider();
        int sent = HidRequest.Sent;
        Check("mercury destino ambiguo fica sem dado", mercury.Discover(new[] { target, target }).Single().Reading.Status == ReadingStatus.NoData);
        Check("mercury destino ambiguo nao recebe consulta", HidRequest.Sent == sent);
        Check("mercury ausente nao gera aparelho", !mercury.Discover(Array.Empty<HidCollection>()).Any());
        HidRequest.NewReading(listenOnly: true, allowedVids: new[] { "373B" });
        Check("mercury respeita so escutar", mercury.Discover(new[] { target }).Single().Reading.Status == ReadingStatus.NoData && HidRequest.Sent == sent);
        HidRequest.NewReading(listenOnly: false, allowedVids: Array.Empty<string>());
        Check("mercury respeita permissao da marca", mercury.Discover(new[] { target }).Single().Reading.Status == ReadingStatus.NoData && HidRequest.Sent == sent);

        var registry = ProviderRegistry.CreateDefault();
        Check("atk providers registrados pelos IDs corretos", registry.StandaloneProviders.OfType<AtkMouseProvider>().Single().Handles(0x373B, 0x10F0)
            && registry.StandaloneProviders.OfType<MercuryProvider>().Single().Handles(0x373B, 0x107C)
            && registry.Resolve(0x373B, 0xF010) is null);
        Check("atk sem conflito nos providers standalone", registry.Resolve(0x373B, 0x10F0) is null
            && registry.StandaloneProviders.Count(p => p.Handles(0x373B, 0x10F0)) == 1
            && registry.StandaloneProviders.Count(p => p.Handles(0x373B, 0x107C)) == 1);
        Check("atk icones de mouse e fone", Known.Kind(0x373B, 0x10F0) == DeviceKind.Mouse && Known.Kind(0x373B, 0x107C) == DeviceKind.Headset);
        Check("atk pixart seleciona somente FF10/1 de 65 bytes", AtkMouseProvider.IsTarget(new HidCollection
            { Path = "fake", Vid = 0x373B, Pid = 0x10F0, UsagePage = 0xFF10, Usage = 1, InputLen = 65, FeatureLen = 9 })
            && !AtkMouseProvider.IsTarget(target));
    }

    private static void CheckAtkFeatureGate(byte[] request)
    {
        var target = new HidCollection { Path = @"\?\joule-selftest-nao-existe", Vid = 0x373B, Pid = 0x10F0,
            UsagePage = 0xFF10, Usage = 1, InputLen = 65, FeatureLen = 9 };
        using var gate = new HidRequest();
        int sent = HidRequest.Sent;
        HidRequest.NewReading(true, new[] { "373B" });
        gate.AskFeature(target, request, _ => true, TimeSpan.Zero);
        Check("feature assincrona respeita so escutar", HidRequest.Sent == sent);
        HidRequest.NewReading(false, Array.Empty<string>());
        gate.AskFeature(target, request, _ => true, TimeSpan.Zero);
        Check("feature assincrona exige permissao da marca", HidRequest.Sent == sent);
        HidRequest.NewReading(false, new[] { "373B" });
        gate.AskFeature(target, request[..8], _ => true, TimeSpan.Zero);
        Check("feature assincrona recusa comprimento errado", HidRequest.Sent == sent);
        var reserved = new HidCollection { Path = target.Path, Vid = target.Vid, Pid = target.Pid,
            UsagePage = 1, Usage = 2, InputLen = 65, FeatureLen = 9 };
        gate.AskFeature(reserved, request, _ => true, TimeSpan.Zero);
        Check("feature assincrona recusa mouse de sistema", HidRequest.Sent == sent);
        HidRequest.Blocked.Add(target.HardwareId);
        try
        {
            gate.AskFeature(target, request, _ => true, TimeSpan.Zero);
            Check("feature assincrona respeita bloqueio do modelo", HidRequest.Sent == sent);
        }
        finally { HidRequest.Blocked.Remove(target.HardwareId); }
        gate.AskFeature(target, request, _ => true, TimeSpan.Zero);
        Check("feature assincrona permitida tenta uma unica consulta", HidRequest.Sent == sent + 1 && gate.Asks == 1);
        HidRequest.NewReading(false, new[] { "373B" });
        int beforeCap = HidRequest.Sent;
        for (int i = 0; i < HidRequest.MaxAsks + 1; i++) gate.AskFeature(target, request, _ => true, TimeSpan.Zero);
        Check("feature assincrona respeita teto global", HidRequest.Sent - beforeCap == HidRequest.MaxAsks);
        HidRequest.NewReading(false, Array.Empty<string>());
    }

    private static byte[] AtkMouseReport(byte capacity, bool battery = false)
    {
        var report = new byte[65];
        byte[] header = battery ? new byte[] { 0, 0x82, 8, 2, 0, 0xFF, 3, 0, 0 }
            : new byte[] { 0, 0x81, 5, 1, 0, 1, 1, 0, 0 };
        header.CopyTo(report, 0);
        report[9] = capacity;
        report[10] = 1;
        return report;
    }

    private static byte[] MercuryMemoryReply(uint value)
    {
        var reply = new byte[64]; reply[0] = 0xAA; reply[1] = 3; reply[2] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(3, 4), value);
        return reply;
    }

    private static HidCollection MercuryCollection(ushort version = 5, string path = "fake&mi_00&col02", int featureLen = 64) =>
        new() { Path = path, Vid = 0x373B, Pid = 0x107C, Version = version, UsagePage = 0xFF22, Usage = 1, FeatureLen = featureLen };

    private static void CheckMercuryStops(string reason, byte[]?[] replies, int expectedQueries)
    {
        int queries = 0;
        var reading = MercuryProvider.ReadBattery(_ => queries < replies.Length ? replies[queries++] : throw new Exception("consulta inesperada"));
        Check($"mercury interrompe por {reason}", reading.Status == ReadingStatus.NoData && reading.Percent is null && queries == expectedQueries);
    }
}
