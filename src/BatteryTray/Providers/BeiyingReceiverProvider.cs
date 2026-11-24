using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Keyboards on the Beiying Tech "8K Wireless Receiver" (372E:106C): checked on a
/// GravaStar Mercury K98 Pro; the AULA HERO 68 Air ships with the same receiver.
/// GravaStar's own web app (GS HUB) asks the battery with these same frames.
///
/// The receiver talks on collection FF60/0061 (interface 2) with acknowledged
/// 20-byte frames. Format documented by Wireless-Device-Battery-Monitor
/// (src/drivers/aula.cpp, from a USB capture of AULA Hub; GPL-3.0); only the
/// format is used here, the code is Joule's own:
///   [0] 09 (report id)  [1] 66  [2] 05  [3] frame index 1..5  [4] frame type
///   type 0E = data (14 payload bytes at 5..18, byte 19 = sum of bytes 1..18),
///   00 = ACK, 08 = trailer from the host, 07 = trailer from the keyboard.
///   The top bits of bytes 2, 3 and 4 form a 3-bit counter, one step per frame.
///   A message is 4 data frames (56 bytes) and a trailer; the host trailer
///   carries 0xFF - sum(message) at byte 12 and sum of bytes 1..12 at byte 13.
///   Every data and trailer frame is acknowledged by the other side: bytes 2..4
///   echoed with the type cleared (counter bit kept), byte 5 = sum of bytes 1..4.
///
/// Battery question (read-only): message 09 87 00 00 01 00. The keyboard answers
/// with a message whose payload is 87 00 00 01 00 02 &lt;level&gt; &lt;flags&gt;, or pushes
/// FE 05 &lt;level&gt; &lt;flags&gt; on its own; flags 0x10 = charging on the cable.
/// Seen on the K98 Pro: 87 00 00 01 00 02 60 00 = 96%, about 45 ms for the whole
/// exchange. Right after waking it may answer 87 00 00 01 00 00 (no level yet),
/// which stays "no data". An asleep keyboard does not ACK the first frame, so the
/// reading stops after one write.
///
/// Writes per reading: 5 frames of the question plus 5 ACKs for the answer, one
/// at a time and each waiting for its reply, through HidRequest. Nothing is
/// resent: a frame without its ACK ends the reading with "no data" (the keyboard
/// resends its own frames by itself, so a lost one still reaches us).
/// </summary>
public sealed class BeiyingReceiverProvider : IStandaloneProvider
{
    internal const ushort Vid = 0x372E, Pid = 0x106C;
    internal const int FrameLength = 20;
    private const byte ReportId = 0x09, Magic = 0x66, Channel = 0x05;
    private const byte TypeData = 0x0E, TypeAck = 0x00, TypeHostTrailer = 0x08, TypeDeviceTrailer = 0x07;
    private const byte BatteryCommand = 0x87, PushCommand = 0xFE;
    private const int MessageLength = 56, PayloadPerFrame = 14, DataFrames = 4;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(400);

    public string Name => "Beiying 8K receiver";

    public bool Handles(ushort vid, ushort pid) => vid == Vid && pid == Pid;

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var claimed = collections.Where(c => Handles(c.Vid, c.Pid)).ToList();
        if (claimed.Count == 0) yield break;

        yield return new DeviceEntry
        {
            Key = "beiying:8k",
            DisplayName = L.T("Teclado no 8K Wireless Receiver", "Keyboard on 8K Wireless Receiver"),
            HardwareId = Known.Id(Vid, Pid),
            ProviderName = Name,
            Kind = DeviceKind.Keyboard,
            Origin = "Dongle",
            IsConnected = true,
            Reading = Read(claimed),
        };
    }

    internal static bool IsTarget(HidCollection c) =>
        c.Vid == Vid && c.Pid == Pid && c.UsagePage == 0xFF60 && c.Usage == 0x0061
        && c.InputLen == FrameLength && c.OutputLen == FrameLength;

    private static BatteryReading Read(IReadOnlyList<HidCollection> claimed)
    {
        var target = claimed.FirstOrDefault(IsTarget);
        if (target is null)
            return BatteryReading.NoData(L.T("não achei o canal FF60 do receptor", "couldn’t find the receiver’s FF60 channel"));

        try
        {
            using var session = HidRequest.Open(new[] { target });
            if (session is null)
                return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));
            return ReadBattery((frame, isReply) => session.Ask(target, frame, isReply, FrameTimeout));
        }
        catch (Exception ex)
        {
            Log.Error("BeiyingReceiver.Read", ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    /// <summary>
    /// The whole exchange. <paramref name="ask"/> writes one frame and waits for a
    /// report the predicate accepts (null = nothing came); injected so the
    /// sequence can be tested without hardware.
    /// </summary>
    internal static BatteryReading ReadBattery(Func<byte[], Func<byte[], bool>, byte[]?> ask)
    {
        var question = BatteryQuestion();
        var asleep = BatteryReading.NoData(L.T(
            "o receptor não respondeu; a pergunta pode estar sem permissão ou o teclado pode estar dormindo",
            "the receiver didn’t answer; the request may not be permitted or the keyboard may be asleep"));

        // The question: 4 data frames, each waits for its ACK.
        int counter = 1;
        for (byte index = 1; index <= DataFrames; index++, counter = (counter + 1) & 7)
        {
            var frame = DataFrame(counter, index, question);
            if (ask(frame, r => IsAckFor(r, frame)) is null) return asleep;
        }

        // The trailer. Its ACK is skipped: what matters is the first frame of the
        // keyboard's answer, which comes right after it.
        var reply = ask(HostTrailer(counter, question), r => IsDeviceData(r, 1));
        if (reply is null) return asleep;

        Battery? battery = ParseFirstFrame(reply);

        // ACK every frame of the answer, waiting each time for the next one; the
        // keyboard's trailer gets the last ACK.
        for (byte next = 2; next <= DataFrames + 1; next++)
        {
            byte wanted = next;
            reply = ask(Ack(reply), r => wanted <= DataFrames ? IsDeviceData(r, wanted) : IsDeviceTrailer(r));
            if (reply is null)
                return battery is { } partial ? ToReading(partial) : asleep;
        }
        ask(Ack(reply), _ => false); // closes the exchange; nothing more is expected

        return battery is { } b
            ? ToReading(b)
            : BatteryReading.NoData(L.T("o teclado respondeu sem o nível da bateria", "the keyboard answered without a battery level"));
    }

    internal readonly record struct Battery(int Percent, bool Charging);

    private static BatteryReading ToReading(Battery b) =>
        BatteryReading.Percentage(b.Percent, chargeText: b.Charging ? L.T("carregando", "charging") : null);

    /// <summary>Level from the first data frame of a keyboard message: the battery answer or the keyboard's own push. Null when it is neither or out of range.</summary>
    internal static Battery? ParseFirstFrame(byte[] r)
    {
        if (!IsDeviceData(r, 1)) return null;
        if (r[5] == BatteryCommand && r[10] == 0x02 && r[11] is >= 1 and <= 100)
            return new Battery(r[11], (r[12] & 0x10) != 0);
        if (r[5] == PushCommand && r[6] == 0x05 && r[7] is >= 1 and <= 100)
            return new Battery(r[7], (r[8] & 0x10) != 0);
        return null;
    }

    internal static byte[] BatteryQuestion()
    {
        var message = new byte[MessageLength];
        message[0] = 0x09;
        message[1] = BatteryCommand;
        message[4] = 0x01;
        return message;
    }

    private static byte[] Header(int counter, byte index, byte type)
    {
        var f = new byte[FrameLength];
        f[0] = ReportId;
        f[1] = Magic;
        f[2] = (byte)(Channel | ((counter & 4) != 0 ? 0x80 : 0));
        f[3] = (byte)(index | ((counter & 2) != 0 ? 0x80 : 0));
        f[4] = (byte)(type | ((counter & 1) != 0 ? 0x80 : 0));
        return f;
    }

    internal static byte[] DataFrame(int counter, byte index, byte[] message)
    {
        var f = Header(counter, index, TypeData);
        Array.Copy(message, (index - 1) * PayloadPerFrame, f, 5, PayloadPerFrame);
        f[19] = Sum(f, 1, 19);
        return f;
    }

    internal static byte[] HostTrailer(int counter, byte[] message)
    {
        var f = Header(counter, 5, TypeHostTrailer);
        f[12] = (byte)(0xFF - Sum(message, 0, message.Length));
        f[13] = Sum(f, 1, 13);
        return f;
    }

    internal static byte[] Ack(byte[] frame)
    {
        var f = new byte[FrameLength];
        f[0] = ReportId;
        f[1] = Magic;
        f[2] = frame[2];
        f[3] = frame[3];
        f[4] = (byte)(frame[4] & 0x80);
        f[5] = Sum(f, 1, 5);
        return f;
    }

    private static bool IsFrame(byte[] r) => r.Length >= FrameLength && r[0] == ReportId && r[1] == Magic;

    internal static bool IsAckFor(byte[] r, byte[] sent) =>
        IsFrame(r) && (r[4] & 0x7F) == TypeAck && r[2] == sent[2] && r[3] == sent[3]
        && (r[4] & 0x80) == (sent[4] & 0x80) && r[5] == Sum(r, 1, 5);

    internal static bool IsDeviceData(byte[] r, byte index) =>
        IsFrame(r) && (r[4] & 0x7F) == TypeData && (r[3] & 0x7F) == index && r[19] == Sum(r, 1, 19);

    internal static bool IsDeviceTrailer(byte[] r) =>
        IsFrame(r) && (r[4] & 0x7F) == TypeDeviceTrailer;

    private static byte Sum(byte[] b, int from, int to)
    {
        int s = 0;
        for (int i = from; i < to; i++) s += b[i];
        return (byte)s;
    }
}
