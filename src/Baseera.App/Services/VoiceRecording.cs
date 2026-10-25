using System.Runtime.InteropServices;

namespace Baseera.App.Services;

internal interface IVoiceRecorder : IDisposable { bool Recording { get; } void Start(); byte[] Stop(); }

internal sealed class VoiceRecording : IVoiceRecorder
{
    internal const int SampleRate = 16000, MaxSeconds = 120, Capacity = SampleRate * 2 * MaxSeconds;
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat
    { internal ushort Format, Channels; internal uint Rate, BytesPerSecond; internal ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader
    { internal IntPtr Data; internal uint Length, Recorded; internal UIntPtr User; internal uint Flags, Loops; internal IntPtr Next; internal UIntPtr Reserved; }
    private IntPtr handle, data, header; private bool prepared;
    public bool Recording => handle != IntPtr.Zero;
    public void Start()
    {
        if (Recording) throw new InvalidOperationException("Already recording.");
        var format = new WaveFormat { Format = 1, Channels = 1, Rate = SampleRate, BytesPerSecond = SampleRate * 2, BlockAlign = 2, Bits = 16 };
        uint opened = waveInOpen(out handle, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (opened != 0) { handle = IntPtr.Zero; Check(opened); }
        try
        {
            data = Marshal.AllocHGlobal(Capacity); header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.StructureToPtr(new WaveHeader { Data = data, Length = Capacity }, header, false);
            Check(waveInPrepareHeader(handle, header, (uint)Marshal.SizeOf<WaveHeader>())); prepared = true;
            Check(waveInAddBuffer(handle, header, (uint)Marshal.SizeOf<WaveHeader>())); Check(waveInStart(handle));
        }
        catch { Dispose(); throw; }
    }
    public byte[] Stop()
    {
        if (!Recording) throw new InvalidOperationException("Not recording.");
        try
        {
            Check(waveInStop(handle)); Check(waveInReset(handle));
            int length = checked((int)Marshal.PtrToStructure<WaveHeader>(header).Recorded);
            if (length is < 2 or > Capacity || length % 2 != 0) throw new InvalidDataException(I18n.T("chatAudioEmpty"));
            var pcm = new byte[length]; Marshal.Copy(data, pcm, 0, length); return WrapPcm(pcm);
        }
        finally { Dispose(); }
    }
    internal static byte[] WrapPcm(byte[] pcm)
    {
        if (pcm.Length is < 2 or > Capacity || pcm.Length % 2 != 0) throw new InvalidDataException("Invalid PCM recording size.");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + pcm.Length); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((ushort)2); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm); return stream.ToArray();
    }
    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        waveInReset(handle);
        // Do not free memory a faulty driver may still own.
        if (prepared && waveInUnprepareHeader(handle, header, (uint)Marshal.SizeOf<WaveHeader>()) != 0) return;
        prepared = false; waveInClose(handle); handle = IntPtr.Zero;
        if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
        if (data != IntPtr.Zero) { Marshal.FreeHGlobal(data); data = IntPtr.Zero; }
    }
    private static void Check(uint result) { if (result != 0) throw new IOException(I18n.T("chatMicFailed") + " (" + result + ")"); }
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);
}
