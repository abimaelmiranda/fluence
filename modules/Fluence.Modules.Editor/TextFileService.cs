using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.Editor;

public sealed class TextFileService : ITextFileService
{
    private const int TextSampleSize = 64 * 1024;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictUtf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private static readonly Encoding StrictUtf16LittleEndian = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
    private static readonly Encoding StrictUtf16BigEndian = new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z",
        ".avi",
        ".bin",
        ".dll",
        ".dmg",
        ".exe",
        ".gif",
        ".gz",
        ".ico",
        ".jpeg",
        ".jpg",
        ".mov",
        ".mp3",
        ".mp4",
        ".pdf",
        ".png",
        ".rar",
        ".tar",
        ".webp",
        ".zip",
    };

    public bool CanOpenAsText(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Directory.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension) && BlockedExtensions.Contains(extension))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        var sampleLength = (int)Math.Min(TextSampleSize, stream.Length);
        if (sampleLength == 0)
        {
            return true;
        }

        var sample = new byte[sampleLength];
        var bytesRead = stream.Read(sample, 0, sample.Length);
        return IsTextSample(sample.AsSpan(0, bytesRead));
    }

    public async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (DecoderFallbackException ex)
        {
            throw new UnsupportedTextFileException(path, "This file could not be decoded as text.", ex);
        }

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (!TryGetTextEncoding(bytes, out var encoding, out var preambleLength))
        {
            throw new UnsupportedTextFileException(path);
        }

        if (EncodingHasNullByteGuard(encoding) && HasNullByte(bytes.AsSpan(preambleLength)))
        {
            throw new UnsupportedTextFileException(path);
        }

        try
        {
            return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException ex)
        {
            throw new UnsupportedTextFileException(path, "This file could not be decoded as text.", ex);
        }
    }

    public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        return File.WriteAllTextAsync(path, content, cancellationToken);
    }

    private static bool IsTextSample(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
        {
            return true;
        }

        if (!TryGetTextEncoding(sample, out var encoding, out var preambleLength))
        {
            return false;
        }

        if (EncodingHasNullByteGuard(encoding) && HasNullByte(sample[preambleLength..]))
        {
            return false;
        }

        return CanDecodeSample(sample[preambleLength..], encoding);
    }

    private static bool TryGetTextEncoding(ReadOnlySpan<byte> bytes, out Encoding encoding, out int preambleLength)
    {
        if (StartsWithBytes(bytes, 0xEF, 0xBB, 0xBF))
        {
            encoding = StrictUtf8Bom;
            preambleLength = 3;
            return true;
        }

        if (StartsWithBytes(bytes, 0xFF, 0xFE))
        {
            encoding = StrictUtf16LittleEndian;
            preambleLength = 2;
            return true;
        }

        if (StartsWithBytes(bytes, 0xFE, 0xFF))
        {
            encoding = StrictUtf16BigEndian;
            preambleLength = 2;
            return true;
        }

        encoding = StrictUtf8;
        preambleLength = 0;
        return true;
    }

    private static bool StartsWithBytes(ReadOnlySpan<byte> bytes, byte first, byte second)
    {
        return bytes.Length >= 2 && bytes[0] == first && bytes[1] == second;
    }

    private static bool StartsWithBytes(ReadOnlySpan<byte> bytes, byte first, byte second, byte third)
    {
        return bytes.Length >= 3 && bytes[0] == first && bytes[1] == second && bytes[2] == third;
    }

    private static bool CanDecodeSample(ReadOnlySpan<byte> sample, Encoding encoding)
    {
        try
        {
            var decoder = encoding.GetDecoder();
            var chars = new char[encoding.GetMaxCharCount(sample.Length)];
            decoder.Convert(sample, chars, flush: false, out _, out _, out _);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool EncodingHasNullByteGuard(Encoding encoding)
    {
        return encoding.CodePage == StrictUtf8.CodePage;
    }

    private static bool HasNullByte(ReadOnlySpan<byte> bytes)
    {
        return bytes.Contains((byte)0);
    }
}
