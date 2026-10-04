using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 把 Unity AudioClip 编码为标准 16-bit PCM WAV 字节。
/// </summary>
public static class WavEncoder
{
    private const int BitsPerSample = 16;
    private const int BytesPerSample = BitsPerSample / 8;

    public static byte[] Encode(AudioClip clip)
    {
        if (clip == null)
        {
            throw new ArgumentNullException(nameof(clip));
        }

        int channels = clip.channels;
        int sampleFrames = clip.samples;
        int totalSampleCount = sampleFrames * channels;

        float[] floatSamples = new float[totalSampleCount];

        if (!clip.GetData(floatSamples, 0))
        {
            throw new InvalidOperationException(
                "无法读取 AudioClip 中的录音数据。"
            );
        }

        int dataByteCount = totalSampleCount * BytesPerSample;

        using MemoryStream stream = new MemoryStream(
            44 + dataByteCount
        );

        using BinaryWriter writer = new BinaryWriter(
            stream,
            Encoding.UTF8,
            true
        );

        WriteAscii(writer, "RIFF");
        writer.Write(36 + dataByteCount);
        WriteAscii(writer, "WAVE");

        WriteAscii(writer, "fmt ");
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(clip.frequency);
        writer.Write(
            clip.frequency * channels * BytesPerSample
        );
        writer.Write(
            (short)(channels * BytesPerSample)
        );
        writer.Write((short)BitsPerSample);

        WriteAscii(writer, "data");
        writer.Write(dataByteCount);

        for (int index = 0; index < floatSamples.Length; index++)
        {
            float clampedSample = Mathf.Clamp(
                floatSamples[index],
                -1f,
                1f
            );

            short pcmSample = (short)Mathf.RoundToInt(
                clampedSample * short.MaxValue
            );

            writer.Write(pcmSample);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteAscii(
        BinaryWriter writer,
        string value
    )
    {
        writer.Write(
            Encoding.ASCII.GetBytes(value)
        );
    }
}
