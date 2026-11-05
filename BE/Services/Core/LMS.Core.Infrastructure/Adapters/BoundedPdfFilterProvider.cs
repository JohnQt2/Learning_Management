using System.IO.Compression;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace LMS.Core.Infrastructure.Adapters;

/// <summary>
/// A document-scoped decoded-byte budget. Expanding filters write through the budget
/// before allocating their output; unsupported codecs are never delegated to an
/// unbounded decoder. Create one instance per PDF, not per page.
/// </summary>
public sealed class BoundedPdfFilterProvider : IFilterProvider
{
    private readonly long _maximum;
    private readonly CancellationToken _cancellation;
    private long _decoded;
    private InvalidDataException? _rejection;

    public BoundedPdfFilterProvider(long maximumDecodedBytes, CancellationToken cancellation = default)
    {
        if (maximumDecodedBytes is < 1 or > 200_000_000) throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
        _maximum = maximumDecodedBytes;
        _cancellation = cancellation;
    }

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary)
        => Wrap(DefaultFilterProvider.Instance.GetFilters(dictionary));
    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names)
        => Wrap(DefaultFilterProvider.Instance.GetNamedFilters(names));
    public IReadOnlyList<IFilter> GetAllFilters() => Wrap(DefaultFilterProvider.Instance.GetAllFilters());

    // PdfPig may recover from a malformed stream. Call this after Open/GetPage as
    // well, so a swallowed safety rejection cannot become an empty/OCR success.
    public void ThrowIfRejected()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_rejection != null) throw _rejection;
    }

    private IReadOnlyList<IFilter> Wrap(IReadOnlyList<IFilter> filters)
        => filters.Select(filter => (IFilter)new SafeFilter(this, filter)).ToArray();

    private InvalidDataException Reject(string message)
        => _rejection ??= new InvalidDataException(message);

    private void Charge(int bytes)
    {
        ThrowIfRejected();
        if (bytes > _maximum - _decoded)
            throw Reject("PDF vượt giới hạn tổng dung lượng giải nén được cấu hình");
        _decoded += bytes;
    }

    private void Write(MemoryStream output, ReadOnlySpan<byte> bytes)
    {
        Charge(bytes.Length);
        output.Write(bytes);
    }

    private Memory<byte> Inflate(Memory<byte> input, DictionaryToken dictionary, int index)
    {
        // ZLibStream validates the zlib wrapper/checksum and exposes incremental
        // reads, unlike PdfPig's built-in FlateFilter which materializes all bytes.
        using var encoded = new MemoryStream(input.ToArray(), writable: false);
        using var decoder = new ZLibStream(encoded, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = decoder.Read(chunk, 0, chunk.Length)) > 0) Write(output, chunk.AsSpan(0, read));
        var expanded = output.ToArray();
        var parameters = DecodeParameterResolver.GetFilterParameters(dictionary, index);
        var predictor = Integer(parameters, NameToken.Predictor, 1);
        if (predictor == 1) return expanded;
        if (predictor != 2 && predictor is not (>= 10 and <= 15))
            throw Reject("PDF sử dụng bộ dự đoán nén chưa được hỗ trợ an toàn");
        var colors = Integer(parameters, NameToken.Colors, 1);
        var bits = Integer(parameters, NameToken.BitsPerComponent, 8);
        var columns = Integer(parameters, NameToken.Columns, 1);
        if (colors is < 1 or > 32 || bits is not (1 or 2 or 4 or 8 or 16) || columns < 1)
            throw Reject("Tham số nén PDF không hợp lệ");
        var rowLength = ((long)columns * colors * bits + 7) / 8;
        // Bound predictor scratch buffers independently of total document size.
        if (rowLength > Math.Min(_maximum, 1_000_000))
            throw Reject("Hàng dữ liệu nén PDF vượt giới hạn an toàn");
        return DecodePredictor(expanded, (int)rowLength, colors, bits, columns, predictor);
    }

    private int Integer(DictionaryToken parameters, NameToken name, int fallback)
    {
        if (!parameters.TryGet(name, out var value)) return fallback;
        if (value is not NumericToken number || number.Double != Math.Truncate(number.Double) ||
            number.Double < int.MinValue || number.Double > int.MaxValue)
            throw Reject("Tham số nén PDF không hợp lệ");
        return number.Int;
    }

    private Memory<byte> DecodePredictor(byte[] input, int rowLength, int colors, int bits, int columns, int predictor)
    {
        var encodedRow = rowLength + (predictor >= 10 ? 1 : 0);
        if (input.Length % encodedRow != 0) throw Reject("Dữ liệu dự đoán nén PDF không đầy đủ");
        var rows = input.Length / encodedRow;
        var result = new byte[rows * rowLength];
        var pixelBytes = (colors * bits + 7) / 8;
        for (var row = 0; row < rows; row++)
        {
            ThrowIfRejected();
            var offset = row * encodedRow;
            var mode = predictor >= 10 ? input[offset++] : 1;
            if (mode > 4) throw Reject("Bộ dự đoán PNG trong PDF không hợp lệ");
            var target = result.AsSpan(row * rowLength, rowLength);
            input.AsSpan(offset, rowLength).CopyTo(target);
            if (predictor == 2)
            {
                var mask = (1 << bits) - 1;
                var samples = checked(columns * colors);
                for (var sample = colors; sample < samples; sample++)
                {
                    var value = (ReadBits(target, sample * bits, bits) + ReadBits(target, (sample - colors) * bits, bits)) & mask;
                    WriteBits(target, sample * bits, bits, value);
                }
                continue;
            }
            for (var column = 0; column < rowLength; column++)
            {
                var left = column >= pixelBytes ? target[column - pixelBytes] : 0;
                var up = row > 0 ? result[(row - 1) * rowLength + column] : 0;
                var diagonal = row > 0 && column >= pixelBytes ? result[(row - 1) * rowLength + column - pixelBytes] : 0;
                var addition = mode switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, 4 => Paeth(left, up, diagonal), _ => 0 };
                target[column] = unchecked((byte)(target[column] + addition));
            }
        }
        return result;
    }

    private static int Paeth(int left, int up, int diagonal)
    {
        var estimate = left + up - diagonal;
        var a = Math.Abs(estimate - left); var b = Math.Abs(estimate - up); var c = Math.Abs(estimate - diagonal);
        return a <= b && a <= c ? left : b <= c ? up : diagonal;
    }

    private static int ReadBits(ReadOnlySpan<byte> bytes, int offset, int count)
    {
        var result = 0;
        for (var i = 0; i < count; i++) result = (result << 1) | ((bytes[(offset + i) / 8] >> (7 - (offset + i) % 8)) & 1);
        return result;
    }

    private static void WriteBits(Span<byte> bytes, int offset, int count, int value)
    {
        for (var i = 0; i < count; i++)
        {
            var index = (offset + i) / 8; var mask = 1 << (7 - (offset + i) % 8);
            bytes[index] = (byte)((bytes[index] & ~mask) | (((value >> (count - i - 1)) & 1) != 0 ? mask : 0));
        }
    }

    private Memory<byte> RunLength(Memory<byte> input)
    {
        using var output = new MemoryStream();
        var bytes = input.Span;
        Span<byte> repeat = stackalloc byte[128];
        for (var index = 0; index < bytes.Length;)
        {
            var count = bytes[index++];
            if (count == 128) return output.ToArray();
            if (count < 128)
            {
                var length = count + 1;
                if (length > bytes.Length - index) throw Reject("RunLength PDF không đầy đủ");
                Write(output, bytes.Slice(index, length)); index += length;
            }
            else
            {
                if (index == bytes.Length) throw Reject("RunLength PDF không đầy đủ");
                var length = 257 - count;
                repeat[..length].Fill(bytes[index++]); Write(output, repeat[..length]);
            }
        }
        throw Reject("RunLength PDF thiếu dấu kết thúc");
    }

    private Memory<byte> AsciiHex(Memory<byte> input)
    {
        using var output = new MemoryStream();
        var pending = -1;
        foreach (var value in input.Span)
        {
            if (White(value)) continue;
            if (value == '>') break;
            var digit = value is >= (byte)'0' and <= (byte)'9' ? value - '0' :
                value is >= (byte)'A' and <= (byte)'F' ? value - 'A' + 10 :
                value is >= (byte)'a' and <= (byte)'f' ? value - 'a' + 10 : -1;
            if (digit < 0) throw Reject("ASCIIHex PDF không hợp lệ");
            if (pending < 0) pending = digit;
            else { Charge(1); output.WriteByte((byte)((pending << 4) | digit)); pending = -1; }
        }
        if (pending >= 0) { Charge(1); output.WriteByte((byte)(pending << 4)); }
        return output.ToArray();
    }

    private Memory<byte> Ascii85(Memory<byte> input)
    {
        using var output = new MemoryStream();
        var source = input.Span;
        var start = source.Length >= 2 && source[0] == '<' && source[1] == '~' ? 2 : 0;
        ulong value = 0; var digits = 0;
        Span<byte> decoded = stackalloc byte[4];
        for (var index = start; index < source.Length; index++)
        {
            var character = source[index];
            if (White(character)) continue;
            if (character == '~')
            {
                if (++index >= source.Length || source[index] != '>') throw Reject("ASCII85 PDF không hợp lệ");
                break;
            }
            if (character == 'z')
            {
                if (digits != 0) throw Reject("ASCII85 PDF không hợp lệ");
                decoded.Clear(); Write(output, decoded); continue;
            }
            if (character < '!' || character > 'u') throw Reject("ASCII85 PDF không hợp lệ");
            value = value * 85 + (uint)(character - '!'); digits++;
            if (digits == 5)
            {
                if (value > uint.MaxValue) throw Reject("ASCII85 PDF không hợp lệ");
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(decoded, (uint)value);
                Write(output, decoded); digits = 0; value = 0;
            }
        }
        if (digits == 1) throw Reject("ASCII85 PDF không đầy đủ");
        if (digits > 1)
        {
            var length = digits - 1;
            while (digits++ < 5) value = value * 85 + 84;
            if (value > uint.MaxValue) throw Reject("ASCII85 PDF không hợp lệ");
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(decoded, (uint)value);
            Write(output, decoded[..length]);
        }
        return output.ToArray();
    }

    private static bool White(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;

    private sealed class SafeFilter(BoundedPdfFilterProvider owner, IFilter original) : IFilter
    {
        // Keep the parser's existing capability signal. For codecs which PdfPig
        // normally decodes (e.g. LZW), Decode must run and reject explicitly.
        // Image-only codecs remain lazy and are never decoded for text extraction.
        public bool IsSupported => original.IsSupported;

        public Memory<byte> Decode(Memory<byte> input, DictionaryToken dictionary, IFilterProvider provider, int index)
        {
            owner.ThrowIfRejected();
            if (input.Length > owner._maximum) throw owner.Reject("Luồng PDF vượt giới hạn dung lượng an toàn");
            try
            {
                return original switch
                {
                    FlateFilter => owner.Inflate(input, dictionary, index),
                    RunLengthFilter => owner.RunLength(input),
                    AsciiHexDecodeFilter => owner.AsciiHex(input),
                    Ascii85Filter => owner.Ascii85(input),
                    _ => throw owner.Reject("PDF sử dụng bộ lọc nén chưa được hỗ trợ an toàn; hãy dùng TXT hoặc xuất lại PDF")
                };
            }
            catch (InvalidDataException ex)
            {
                owner._rejection ??= ex;
                throw;
            }
        }
    }
}
