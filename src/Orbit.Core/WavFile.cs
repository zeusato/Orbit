using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Orbit.Core;

// Minimal PCM WAV handling for joining clips that were first decoded to one common format.
public static class WavFile
{
    public sealed record Info(ushort Format, ushort Channels, uint SampleRate, ushort BitsPerSample, long DataOffset, long DataLength)
    {
        public int BlockAlign => Channels * BitsPerSample / 8;
        public TimeSpan Duration => TimeSpan.FromSeconds(DataLength / (double)(SampleRate * (uint)BlockAlign));
    }

    public static Info Read(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Không phải WAV.");
        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Không phải WAV.");
        ushort format = 0, channels = 0, bits = 0; uint rate = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4)); long size = reader.ReadUInt32(); var body = stream.Position;
            if (id == "fmt ")
            {
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                if (format == 0xFFFE && size >= 40) { stream.Position = body + 24; format = reader.ReadUInt16(); } // WAVE_FORMAT_EXTENSIBLE sub-format
            }
            else if (id == "data")
            {
                if (channels == 0) throw new InvalidDataException("WAV thiếu khối fmt.");
                // Writers that stream may leave 0 or 0xFFFFFFFF; the data then runs to the end of the file.
                var length = size is 0 or uint.MaxValue || body + size > stream.Length ? stream.Length - body : size;
                return new Info(format, channels, rate, bits, body, length);
            }
            stream.Position = body + size + (size % 2);
        }
        throw new InvalidDataException("WAV thiếu dữ liệu âm thanh.");
    }

    public static void Concat(IReadOnlyList<string> inputs, string output, Action<double>? progress = null)
    {
        var infos = inputs.Select(Read).ToArray();
        var first = infos[0];
        if (first.Format != 1 || infos.Any(i => i.Format != 1 || i.Channels != first.Channels || i.SampleRate != first.SampleRate || i.BitsPerSample != first.BitsPerSample))
            throw new InvalidDataException("Các đoạn âm thanh phải cùng định dạng PCM trước khi ghép.");
        var total = infos.Sum(i => i.DataLength - i.DataLength % i.BlockAlign);
        if (total > uint.MaxValue - 36) throw new NotSupportedException("Tổng âm thanh vượt giới hạn 4 GB của WAV.");
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write); using var writer = new BinaryWriter(stream);
        WriteHeader(writer, first.Channels, first.SampleRate, first.BitsPerSample, (uint)total);
        var buffer = new byte[1 << 20]; long done = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            using var input = File.OpenRead(inputs[i]); input.Position = infos[i].DataOffset;
            var remaining = infos[i].DataLength - infos[i].DataLength % infos[i].BlockAlign;
            while (remaining > 0)
            {
                var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (read == 0) throw new EndOfStreamException("WAV bị cắt ngắn.");
                writer.Write(buffer, 0, read); remaining -= read; done += read; progress?.Invoke(done / (double)Math.Max(1, total));
            }
        }
    }

    public static void WriteHeader(BinaryWriter writer, ushort channels, uint rate, ushort bits, uint dataLength)
    {
        var align = (ushort)(channels * bits / 8);
        writer.Write("RIFF"u8); writer.Write(36 + dataLength); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(16u); writer.Write((ushort)1); writer.Write(channels); writer.Write(rate); writer.Write(rate * align); writer.Write(align); writer.Write(bits);
        writer.Write("data"u8); writer.Write(dataLength);
    }
}
