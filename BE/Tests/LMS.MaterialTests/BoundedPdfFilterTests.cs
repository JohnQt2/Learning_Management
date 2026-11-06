using System.IO.Compression;
using System.Text;
using LMS.Core.Infrastructure.Adapters;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace LMS.MaterialTests;

internal static class BoundedPdfFilterTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var plain = Encoding.ASCII.GetBytes("bounded PDF stream text");
        var provider = new BoundedPdfFilterProvider(1024);
        var flate = Filter(provider, NameToken.FlateDecode);
        check(flate.Decode(Compress(plain), Dictionary(NameToken.FlateDecode), provider, 0).Span.SequenceEqual(plain), "bounded Flate preserves standard zlib text");

        var bomb = Compress(new byte[64_000]);
        var small = new BoundedPdfFilterProvider(1000);
        Reject(() => Filter(small, NameToken.FlateDecode).Decode(bomb, Dictionary(NameToken.FlateDecode), small, 0), check, "Flate expansion rejected at actual decoded-byte budget");
        Reject(small.ThrowIfRejected, check, "PDF guard preserves rejection after parser recovery");
        var shared = new BoundedPdfFilterProvider(40);
        Filter(shared, NameToken.FlateDecode).Decode(Compress(plain), Dictionary(NameToken.FlateDecode), shared, 0);
        Reject(() => Filter(shared, NameToken.FlateDecode).Decode(Compress(plain), Dictionary(NameToken.FlateDecode), shared, 0), check, "PDF expansion budget covers multiple streams together");

        // PNG Up: first row is [10,20,30], next residual row [1,2,3].
        var png = new BoundedPdfFilterProvider(1000);
        var pngDictionary = Dictionary(NameToken.FlateDecode, new Dictionary<NameToken, IToken>
        {
            [NameToken.Predictor] = new NumericToken(12), [NameToken.Columns] = new NumericToken(3)
        });
        check(Filter(png, NameToken.FlateDecode).Decode(Compress([2, 10, 20, 30, 2, 1, 2, 3]), pngDictionary, png, 0).Span.SequenceEqual(new byte[] { 10, 20, 30, 11, 22, 33 }), "bounded PDF PNG predictor decodes actual rows");
        var tiff = new BoundedPdfFilterProvider(1000);
        var tiffDictionary = Dictionary(NameToken.FlateDecode, new Dictionary<NameToken, IToken>
        {
            [NameToken.Predictor] = new NumericToken(2), [NameToken.Columns] = new NumericToken(3)
        });
        check(Filter(tiff, NameToken.FlateDecode).Decode(Compress([1, 2, 3]), tiffDictionary, tiff, 0).Span.SequenceEqual(new byte[] { 1, 3, 6 }), "bounded PDF TIFF predictor decodes samples");
        var badRows = new BoundedPdfFilterProvider(1000);
        var badRowDictionary = Dictionary(NameToken.FlateDecode, new Dictionary<NameToken, IToken>
        {
            [NameToken.Predictor] = new NumericToken(12), [NameToken.Columns] = new NumericToken(int.MaxValue)
        });
        Reject(() => Filter(badRows, NameToken.FlateDecode).Decode(Compress([0]), badRowDictionary, badRows, 0), check, "PDF predictor rejects huge dimensions before scratch allocation");

        var runLength = new BoundedPdfFilterProvider(1000);
        check(Filter(runLength, NameToken.RunLengthDecode).Decode(new byte[] { 254, 65, 128 }, Dictionary(NameToken.RunLengthDecode), runLength, 0).Span.SequenceEqual("AAA"u8), "bounded RunLength preserves repeated bytes");
        var runLengthSmall = new BoundedPdfFilterProvider(10);
        Reject(() => Filter(runLengthSmall, NameToken.RunLengthDecode).Decode(new byte[] { 129, 65, 128 }, Dictionary(NameToken.RunLengthDecode), runLengthSmall, 0), check, "RunLength checks expansion before allocation");
        var hex = new BoundedPdfFilterProvider(1000);
        check(Filter(hex, NameToken.AsciiHexDecode).Decode(Encoding.ASCII.GetBytes("4142 4>"), Dictionary(NameToken.AsciiHexDecode), hex, 0).Span.SequenceEqual(new byte[] { 65, 66, 64 }), "bounded ASCIIHex handles whitespace and odd final nibble");
        var ascii85 = new BoundedPdfFilterProvider(1000);
        check(Filter(ascii85, NameToken.Ascii85Decode).Decode(Encoding.ASCII.GetBytes("87cURD_*#TDfTZ)+T~>"), Dictionary(NameToken.Ascii85Decode), ascii85, 0).Span.SequenceEqual("Hello, world!"u8), "bounded ASCII85 decodes complete and partial groups");
        var short85 = new BoundedPdfFilterProvider(3);
        Reject(() => Filter(short85, NameToken.Ascii85Decode).Decode(new byte[] { (byte)'z' }, Dictionary(NameToken.Ascii85Decode), short85, 0), check, "ASCII85 zero shortcut respects actual expansion cap");
        var unsupported = new BoundedPdfFilterProvider(1000);
        Reject(() => Filter(unsupported, NameToken.LzwDecode).Decode(new byte[] { 0 }, Dictionary(NameToken.LzwDecode), unsupported, 0), check, "unbounded LZW decoder is explicitly unsupported");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stopped = new BoundedPdfFilterProvider(1000, cancelled.Token);
        try { Filter(stopped, NameToken.FlateDecode).Decode(Compress(plain), Dictionary(NameToken.FlateDecode), stopped, 0); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { check(true, "PDF decoding honors cancellation before expansion"); }

        var extractor = new MaterialTextExtractor();
        using var textPdf = new MemoryStream(Pdf(Compress(Encoding.ASCII.GetBytes("BT /F1 12 Tf 20 100 Td (Safe compressed PDF) Tj ET"))));
        var extracted = await extractor.ExtractAsync(textPdf, ".pdf", default);
        check(extracted.Status == "Extracted" && extracted.Text.Contains("Safe compressed PDF"), "PDF parser integration extracts bounded compressed text layer");
        var limited = new MaterialTextExtractor(new MaterialStorageOptions { MaxArchiveExpandedBytes = 1000 });
        using var oversizedPdf = new MemoryStream(Pdf(Compress(Enumerable.Repeat((byte)' ', 64_000).ToArray())));
        try { await limited.ExtractAsync(oversizedPdf, ".pdf", default); throw new Exception("Compressed PDF bypassed expansion limit"); }
        catch (InvalidDataException) { check(true, "full PDF parser rejects oversized compressed stream instead of returning NeedsOcr"); }
        using var unsupportedPdf = new MemoryStream(Pdf([0], "LZWDecode"));
        try { await extractor.ExtractAsync(unsupportedPdf, ".pdf", default); throw new Exception("Unsupported codec reported as empty PDF"); }
        catch (InvalidDataException) { check(true, "unsupported PDF content codec returns explicit failure instead of NeedsOcr"); }
    }

    private static IFilter Filter(BoundedPdfFilterProvider provider, NameToken name) => provider.GetNamedFilters([name]).Single();
    private static DictionaryToken Dictionary(NameToken filter, Dictionary<NameToken, IToken>? parameters = null)
    {
        var entries = new Dictionary<NameToken, IToken> { [NameToken.Filter] = filter };
        if (parameters != null) entries[NameToken.DecodeParms] = new DictionaryToken(parameters);
        return new DictionaryToken(entries);
    }

    private static byte[] Compress(byte[] input)
    {
        using var destination = new MemoryStream();
        using (var compressor = new ZLibStream(destination, CompressionLevel.SmallestSize, leaveOpen: true)) compressor.Write(input);
        return destination.ToArray();
    }

    private static void Reject(Action action, Action<bool, string> check, string label)
    {
        try { action(); throw new Exception("Expected rejection: " + label); }
        catch (InvalidDataException) { check(true, label); }
    }

    private static byte[] Pdf(byte[] compressed, string filter = "FlateDecode")
    {
        using var document = new MemoryStream();
        void Append(string value) => document.Write(Encoding.ASCII.GetBytes(value));
        Append("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        void Object(int id, string value) { offsets.Add(document.Position); Append($"{id} 0 obj\n{value}\nendobj\n"); }
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        offsets.Add(document.Position);
        Append($"4 0 obj\n<< /Length {compressed.Length} /Filter /{filter} >>\nstream\n");
        document.Write(compressed); Append("\nendstream\nendobj\n");
        Object(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var xref = document.Position;
        Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Append($"{offset:D10} 00000 n \n");
        Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return document.ToArray();
    }
}
