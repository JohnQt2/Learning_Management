using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LMS.Core.Infrastructure.Adapters;

public interface ITextExtractor
{
    Task<TextExtractionResult> ExtractAsync(Stream input, string extension, CancellationToken ct);
}

/// <summary>Reads document text only. Never renders or executes document content.</summary>
public sealed class MaterialTextExtractor : ITextExtractor
{
    private readonly MaterialStorageOptions _limits;
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    public MaterialTextExtractor(MaterialStorageOptions? limits = null)
    {
        _limits = limits ?? new();
        _limits.Validate();
    }

    public async Task<TextExtractionResult> ExtractAsync(Stream input, string extension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        extension = extension.ToLowerInvariant();
        _ = MaterialFormats.ContentType(extension);
        try
        {
            if (extension is ".txt" or ".md") return await ExtractTextAsync(input, ct);
            if (extension == ".pdf") return ExtractPdf(input, ct);
            return await ExtractOfficeAsync(input, extension, ct);
        }
        catch (DecoderFallbackException ex)
        { throw new InvalidDataException("TXT/Markdown phải là văn bản UTF-8 hợp lệ", ex); }
        catch (XmlException ex)
        { throw new InvalidDataException("Tài liệu Office chứa XML không hợp lệ hoặc không an toàn", ex); }
    }

    private async Task<TextExtractionResult> ExtractTextAsync(Stream input, CancellationToken ct)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), false, 8192, leaveOpen: true);
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (text.Length + read > _limits.MaxExtractedCharacters)
                throw new InvalidDataException("Nội dung vượt giới hạn ký tự trích xuất được cấu hình");
            text.Append(buffer, 0, read);
        }
        var normalized = Normalize(text.ToString());
        if (normalized.Any(c => char.IsControl(c) && c is not ('\n' or '\t')))
            throw new InvalidDataException("TXT/Markdown không phải tệp văn bản hợp lệ");
        return Complete(normalized, "NoText", "Tệp không chứa nội dung văn bản có thể sử dụng.");
    }

    private TextExtractionResult ExtractPdf(Stream input, CancellationToken ct)
    {
        var signature = new byte[5];
        if (input.Read(signature, 0, 5) != 5 || !signature.AsSpan().SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("Tệp không phải PDF hợp lệ");
        input.Position = 0;
        try
        {
            var filters = new BoundedPdfFilterProvider(_limits.MaxArchiveExpandedBytes, ct);
            using var document = PdfDocument.Open(input, new ParsingOptions { FilterProvider = filters, MaxStackDepth = 64 });
            filters.ThrowIfRejected();
            if (document.NumberOfPages > _limits.MaxPdfPages)
                throw new InvalidDataException("PDF vượt giới hạn số trang được cấu hình");
            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                filters.ThrowIfRejected();
                ct.ThrowIfCancellationRequested();
                var pageText = Normalize(ContentOrderTextExtractor.GetText(page));
                if (string.IsNullOrWhiteSpace(pageText)) continue;
                AppendBounded(text, $"[Trang {page.Number}]\n{pageText}\n\n");
            }
            filters.ThrowIfRejected();
            return Complete(text.ToString().TrimEnd(), "NeedsOcr",
                "PDF không có lớp văn bản có thể trích xuất (có thể là bản scan). Chưa hỗ trợ OCR.");
        }
        catch (InvalidDataException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { throw new InvalidDataException("Không đọc được cấu trúc PDF; tệp có thể hỏng hoặc cần mật khẩu", ex); }
    }

    private async Task<TextExtractionResult> ExtractOfficeAsync(Stream input, string extension, CancellationToken ct)
    {
        var signature = new byte[4];
        if (await input.ReadAsync(signature, ct) != 4 || !signature.AsSpan().SequenceEqual("PK\u0003\u0004"u8))
            throw new InvalidDataException("Tệp Office không phải gói Open XML hợp lệ");
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > _limits.MaxArchiveEntries)
            throw new InvalidDataException("Tài liệu có quá nhiều thành phần nén");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Split('/').Contains("..") || !names.Add(name))
                throw new InvalidDataException("Gói Office chứa đường dẫn không an toàn hoặc trùng lặp");
            if (entry.Length < 0 || entry.Length > _limits.MaxArchiveExpandedBytes - expanded)
                throw new InvalidDataException("Tài liệu vượt giới hạn dung lượng giải nén");
            expanded += entry.Length;
            if (name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("vbaData.xml", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("word/activeX/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("ppt/activeX/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Không hỗ trợ tài liệu chứa macro hoặc ActiveX");
        }

        var types = await ReadXmlAsync(archive, "[Content_Types].xml", ct);
        if (types.Root?.Name != ContentTypes + "Types") throw new InvalidDataException("Thiếu Content Types Open XML hợp lệ");
        if (types.Descendants().Attributes("ContentType").Any(a => a.Value.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) || a.Value.Contains("vba", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Không hỗ trợ tài liệu chứa macro");
        var part = extension == ".docx" ? "word/document.xml" : "ppt/presentation.xml";
        var expected = extension == ".docx"
            ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"
            : "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
        if (!types.Root.Elements(ContentTypes + "Override").Any(e => (string?)e.Attribute("PartName") == "/" + part && (string?)e.Attribute("ContentType") == expected))
            throw new InvalidDataException("Định dạng thực tế không khớp DOCX/PPTX");
        var packageRels = await ReadXmlAsync(archive, "_rels/.rels", ct);
        if (packageRels.Root?.Name != Relationships + "Relationships" ||
            !packageRels.Root.Elements(Relationships + "Relationship").Any(e =>
                ((string?)e.Attribute("Type"))?.EndsWith("/officeDocument", StringComparison.Ordinal) == true &&
                (string?)e.Attribute("TargetMode") != "External" && ((string?)e.Attribute("Target"))?.TrimStart('/') == part))
            throw new InvalidDataException("Thiếu quan hệ tài liệu chính Open XML hợp lệ");

        var text = new StringBuilder();
        if (extension == ".docx")
        {
            var document = await ReadXmlAsync(archive, part, ct);
            var body = document.Root?.Name == Word + "document" ? document.Root.Element(Word + "body") : null;
            if (body == null) throw new InvalidDataException("Không tìm thấy nội dung DOCX hợp lệ");
            foreach (var block in body.Elements())
            {
                if (block.Name == Word + "tbl")
                {
                    foreach (var row in block.Elements(Word + "tr"))
                        AppendBounded(text, string.Join('\t', row.Elements(Word + "tc").Select(cell =>
                            string.Join(' ', cell.Descendants(Word + "p").Select(WordParagraph)))) + "\n");
                }
                else if (block.Name == Word + "p") AppendBounded(text, WordParagraph(block) + "\n");
                else foreach (var paragraph in block.Descendants(Word + "p")) AppendBounded(text, WordParagraph(paragraph) + "\n");
            }
        }
        else
        {
            // Slide ordering comes from the presentation relationship list, never ZIP order
            // or lexicographic slide10/slide2 filenames. External references are never opened.
            var presentation = await ReadXmlAsync(archive, part, ct);
            if (presentation.Root?.Name != Presentation + "presentation") throw new InvalidDataException("PPTX không hợp lệ");
            var rels = await ReadXmlAsync(archive, "ppt/_rels/presentation.xml.rels", ct);
            if (rels.Root?.Name != Relationships + "Relationships") throw new InvalidDataException("Quan hệ slide không hợp lệ");
            var byId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rel in rels.Root.Elements(Relationships + "Relationship"))
            {
                if ((string?)rel.Attribute("TargetMode") == "External" ||
                    ((string?)rel.Attribute("Type"))?.EndsWith("/slide", StringComparison.Ordinal) != true) continue;
                var id = (string?)rel.Attribute("Id"); var target = (string?)rel.Attribute("Target");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target) || !byId.TryAdd(id, ResolveSlideTarget(target)))
                    throw new InvalidDataException("Quan hệ slide không hợp lệ hoặc trùng lặp");
            }
            var slides = presentation.Root.Element(Presentation + "sldIdLst")?.Elements(Presentation + "sldId").ToList() ?? [];
            if (slides.Count > _limits.MaxPdfPages) throw new InvalidDataException("PPTX vượt giới hạn số slide được cấu hình");
            for (var i = 0; i < slides.Count; i++)
            {
                var id = (string?)slides[i].Attribute(OfficeRelationships + "id");
                if (id == null || !byId.TryGetValue(id, out var target)) throw new InvalidDataException("Thiếu slide nội bộ hợp lệ");
                var slide = await ReadXmlAsync(archive, target, ct);
                if (slide.Root?.Name != Presentation + "sld") throw new InvalidDataException("Nội dung slide không hợp lệ");
                var paragraphs = slide.Descendants(Drawing + "p").Select(DrawingParagraph).Where(p => !string.IsNullOrWhiteSpace(p));
                var slideText = string.Join('\n', paragraphs);
                if (!string.IsNullOrWhiteSpace(slideText)) AppendBounded(text, $"[Slide {i + 1}]\n{slideText}\n\n");
            }
        }
        return Complete(text.ToString().TrimEnd(), "NoText", extension == ".pptx"
            ? "Không tìm thấy chữ trong slide. Slide chỉ chứa ảnh cần OCR; hiện chưa hỗ trợ OCR."
            : "DOCX không chứa văn bản có thể trích xuất. Nội dung chỉ có ảnh cần OCR; hiện chưa hỗ trợ OCR.");
    }

    private async Task<XDocument> ReadXmlAsync(ZipArchive archive, string name, CancellationToken ct)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("Thiếu thành phần Open XML bắt buộc");
        if (entry.Length > _limits.MaxArchiveExpandedBytes) throw new InvalidDataException("Thành phần Open XML quá lớn");
        // Validate depth before constructing a tree. This also prevents adversarial nested
        // text elements from amplifying XElement.Value allocations during extraction.
        using (var validationStream = entry.Open())
        using (var boundedValidation = new BoundedReadStream(validationStream, Math.Min(entry.Length, _limits.MaxArchiveExpandedBytes)))
        using (var validation = XmlReader.Create(boundedValidation, XmlSettings()))
        {
            int? textDepth = null;
            while (await validation.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();
                if (validation.Depth > 64) throw new InvalidDataException("XML tài liệu có độ sâu vượt giới hạn");
                if (validation.NodeType == XmlNodeType.Element)
                {
                    if (textDepth.HasValue && validation.Depth > textDepth.Value)
                        throw new InvalidDataException("Thành phần văn bản Open XML không được chứa phần tử lồng nhau");
                    if (!validation.IsEmptyElement && validation.LocalName == "t" &&
                        (validation.NamespaceURI == Word.NamespaceName || validation.NamespaceURI == Drawing.NamespaceName))
                        textDepth = validation.Depth;
                }
                else if (validation.NodeType == XmlNodeType.EndElement && validation.Depth == textDepth) textDepth = null;
            }
        }
        using var stream = entry.Open();
        // Hard cap actual decompressed bytes too; a forged central directory cannot defeat
        // the declared-size checks. XML never resolves a DTD, URL, or external entity.
        using var bounded = new BoundedReadStream(stream, Math.Min(entry.Length, _limits.MaxArchiveExpandedBytes));
        using var reader = XmlReader.Create(bounded, XmlSettings());
        return await XDocument.LoadAsync(reader, LoadOptions.None, ct);
    }

    private XmlReaderSettings XmlSettings() => new()
    {
        Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
        MaxCharactersInDocument = _limits.MaxArchiveExpandedBytes, MaxCharactersFromEntities = 1
    };

    private static string ResolveSlideTarget(string target)
    {
        if (target.Contains('\\') || target.Contains(':') || target.Split('/').Contains(".."))
            throw new InvalidDataException("Đường dẫn slide không an toàn");
        var resolved = target.StartsWith('/') ? target.TrimStart('/') : "ppt/" + target.TrimStart('.', '/');
        if (!Regex.IsMatch(resolved, @"^ppt/slides/slide[0-9]+\.xml$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Đường dẫn slide không hợp lệ");
        return resolved;
    }

    private static string WordParagraph(XElement paragraph) => string.Concat(paragraph.Descendants().Select(e =>
        e.Name == Word + "t" ? e.Value : e.Name == Word + "tab" ? "\t" : e.Name == Word + "br" || e.Name == Word + "cr" ? "\n" : ""));
    private static string DrawingParagraph(XElement paragraph) => string.Concat(paragraph.Descendants().Select(e =>
        e.Name == Drawing + "t" ? e.Value : e.Name == Drawing + "br" ? "\n" : ""));
    private static string Normalize(string text) => text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
    private void AppendBounded(StringBuilder text, string value)
    {
        if (value.Length > _limits.MaxExtractedCharacters - text.Length)
            throw new InvalidDataException("Nội dung vượt giới hạn ký tự trích xuất được cấu hình");
        text.Append(value);
    }
    private static TextExtractionResult Complete(string text, string emptyStatus, string message) =>
        string.IsNullOrWhiteSpace(text) ? new("", emptyStatus, message) : new(text, "Extracted", "Đã trích xuất văn bản; chưa thực hiện OCR.");

    private sealed class BoundedReadStream(Stream inner, long maximum) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { var n = inner.Read(buffer, offset, count); Track(n); return n; }
        public override int Read(Span<byte> buffer)
        { var n = inner.Read(buffer); Track(n); return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var n = await inner.ReadAsync(buffer, cancellationToken); Track(n); return n; }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { var n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken); Track(n); return n; }
        private void Track(int n)
        { _read += n; if (_read > maximum) throw new InvalidDataException("Thành phần nén vượt giới hạn dung lượng thực tế"); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
