using BatteryTray.Core;
using BatteryTray.Providers;

namespace BatteryTray.Diag;

/// <summary>
/// Beiying 8K receiver (372E:106C): the frames and the whole acknowledged
/// exchange, played against a scripted keyboard, without hardware.
/// </summary>
internal static partial class SelfTest
{
    private static void RunBeiyingTests()
    {
        var question = BeiyingReceiverProvider.BatteryQuestion();
        Check("beiying pergunta 09 87 00 00 01", question.Length == 56 && question[0] == 0x09 && question[1] == 0x87 && question[4] == 0x01
            && question.Skip(5).All(b => b == 0));

        var first = BeiyingReceiverProvider.DataFrame(1, 1, question);
        Check("beiying quadro 1: 09 66 05 01 8E, soma no byte 19",
            first[0] == 0x09 && first[1] == 0x66 && first[2] == 0x05 && first[3] == 0x01 && first[4] == 0x8E
            && first[5] == 0x09 && first[6] == 0x87 && first[19] == SumOf(first, 1, 19));
        var fourth = BeiyingReceiverProvider.DataFrame(4, 4, question);
        Check("beiying contador 4 no bit alto do byte 2", fourth[2] == 0x85 && fourth[3] == 0x04 && fourth[4] == 0x0E);
        var trailer = BeiyingReceiverProvider.HostTrailer(5, question);
        Check("beiying trailer: 0xFF - soma da mensagem e soma 1..12",
            trailer[3] == 0x05 && (trailer[4] & 0x7F) == 0x08 && trailer[12] == (byte)(0xFF - 0x91) && trailer[13] == SumOf(trailer, 1, 13));
        var ack = BeiyingReceiverProvider.Ack(first);
        Check("beiying ACK ecoa 2..3, limpa o tipo, soma 1..4",
            ack[2] == first[2] && ack[3] == first[3] && ack[4] == 0x80 && ack[5] == SumOf(ack, 1, 5) && ack.Skip(6).All(b => b == 0));
        Check("beiying ACK reconhecido", BeiyingReceiverProvider.IsAckFor(ack, first));
        Check("beiying ACK de outro quadro recusado", !BeiyingReceiverProvider.IsAckFor(ack, BeiyingReceiverProvider.DataFrame(2, 2, question)));

        // Real frames from a GravaStar Mercury K98 Pro (report id included).
        var k98 = Convert.FromHexString("096605018E8700000100026000000000000000E4");
        Check("beiying K98 Pro real: 96%, not charging", BeiyingReceiverProvider.ParseFirstFrame(k98) is { Percent: 96, Charging: false });
        var k98Waking = Convert.FromHexString("096685818E870000010000000000000000000082");
        Check("beiying K98 Pro waking reply without level is not a reading", BeiyingReceiverProvider.ParseFirstFrame(k98Waking) is null);
        var k98Push = Convert.FromHexString("096605010EFE05600000000000000000000000DD");
        Check("beiying K98 Pro real push FE 05: 96%", BeiyingReceiverProvider.ParseFirstFrame(k98Push) is { Percent: 96 });
        Check("beiying K98 Pro real ACK for frame 1", BeiyingReceiverProvider.IsAckFor(Convert.FromHexString("0966050180EC0000000000000000000000000000"), first));

        var reply = KeyboardData(1, Msg(0x87, 0x00, 0x00, 0x01, 0x00, 0x02, 76, 0x00));
        Check("beiying resposta 87: 76% na bateria", BeiyingReceiverProvider.ParseFirstFrame(reply) is { Percent: 76, Charging: false });
        Check("beiying resposta 87 carregando", BeiyingReceiverProvider.ParseFirstFrame(KeyboardData(1, Msg(0x87, 0, 0, 1, 0, 2, 76, 0x10))) is { Charging: true });
        Check("beiying aviso FE 05 tambem serve", BeiyingReceiverProvider.ParseFirstFrame(KeyboardData(1, Msg(0xFE, 0x05, 55, 0x00))) is { Percent: 55 });
        Check("beiying nivel 101 recusado", BeiyingReceiverProvider.ParseFirstFrame(KeyboardData(1, Msg(0x87, 0, 0, 1, 0, 2, 101, 0))) is null);
        Check("beiying nivel 0 recusado (nunca 0%)", BeiyingReceiverProvider.ParseFirstFrame(KeyboardData(1, Msg(0x87, 0, 0, 1, 0, 2, 0, 0))) is null);
        var damaged = (byte[])reply.Clone(); damaged[19]++;
        Check("beiying soma errada recusada", BeiyingReceiverProvider.ParseFirstFrame(damaged) is null);
        Check("beiying quadro 2 nao e' o primeiro", BeiyingReceiverProvider.ParseFirstFrame(KeyboardData(2, Msg(0x87, 0, 0, 1, 0, 2, 76, 0))) is null);

        // A troca inteira contra um teclado de mentira que segue o protocolo.
        int writes = 0;
        var keyboard = new Queue<byte[]>();
        byte[]? Fake(byte[] frame, Func<byte[], bool> isReply)
        {
            writes++;
            int type = frame[4] & 0x7F;
            if (type == 0x0E) keyboard.Enqueue(Acked(frame));
            else if (type == 0x08)
            {
                keyboard.Enqueue(Acked(frame));
                keyboard.Enqueue(KeyboardData(1, Msg(0x87, 0x00, 0x00, 0x01, 0x00, 0x02, 76, 0x10)));
            }
            else if (type == 0x00 && (frame[3] & 0x7F) < 4) keyboard.Enqueue(KeyboardData((byte)((frame[3] & 0x7F) + 1), new byte[14]));
            else if (type == 0x00 && (frame[3] & 0x7F) == 4) keyboard.Enqueue(KeyboardTrailer());
            while (keyboard.Count > 0)
            {
                var r = keyboard.Dequeue();
                if (isReply(r)) return r;
            }
            return null;
        }
        var full = BeiyingReceiverProvider.ReadBattery(Fake);
        Eq("beiying troca completa: 76%", 76, full.Percent);
        Check("beiying troca completa: carregando", full.ChargeText is not null);
        Eq("beiying troca completa: 10 escritas (5 da pergunta, 5 ACKs)", 10, writes);

        int silentWrites = 0;
        var silent = BeiyingReceiverProvider.ReadBattery((_, _) => { silentWrites++; return null; });
        Check("beiying teclado dormindo: sem dado, nunca 0%", silent is { Status: ReadingStatus.NoData, Percent: null });
        Eq("beiying teclado dormindo: para na primeira escrita", 1, silentWrites);
    }

    private static byte[] Msg(params byte[] head)
    {
        var m = new byte[14];
        Array.Copy(head, m, head.Length);
        return m;
    }

    private static byte[] KeyboardData(byte index, byte[] payload)
    {
        var f = new byte[20];
        f[0] = 0x09; f[1] = 0x66; f[2] = 0x05; f[3] = index; f[4] = 0x0E;
        Array.Copy(payload, 0, f, 5, 14);
        f[19] = SumOf(f, 1, 19);
        return f;
    }

    private static byte[] KeyboardTrailer()
    {
        var f = new byte[20];
        f[0] = 0x09; f[1] = 0x66; f[2] = 0x05; f[3] = 0x05; f[4] = 0x07;
        return f;
    }

    private static byte[] Acked(byte[] frame)
    {
        var f = new byte[20];
        f[0] = 0x09; f[1] = 0x66; f[2] = frame[2]; f[3] = frame[3]; f[4] = (byte)(frame[4] & 0x80);
        f[5] = SumOf(f, 1, 5);
        return f;
    }

    private static byte SumOf(byte[] b, int from, int to)
    {
        int s = 0;
        for (int i = from; i < to; i++) s += b[i];
        return (byte)s;
    }
}
