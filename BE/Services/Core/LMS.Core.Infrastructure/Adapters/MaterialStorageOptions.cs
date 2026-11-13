namespace LMS.Core.Infrastructure.Adapters;

public sealed class MaterialStorageOptions
{
    public long MaxUploadBytes { get; init; } = 5_000_000;
    public int MaxExtractedCharacters { get; init; } = 2_000_000;
    public long MaxArchiveExpandedBytes { get; init; } = 50_000_000;
    public int MaxArchiveEntries { get; init; } = 2000;
    public int MaxPdfPages { get; init; } = 500;
    public int MaxPreviewCharacters { get; init; } = 30_000;

    public void Validate()
    {
        if (MaxUploadBytes is < 1 or > 100_000_000 ||
            MaxExtractedCharacters is < 1 or > 10_000_000 ||
            MaxArchiveExpandedBytes is < 1 or > 200_000_000 ||
            MaxArchiveEntries is < 1 or > 20_000 || MaxPdfPages is < 1 or > 5000 ||
            MaxPreviewCharacters is < 1 or > 100_000)
            throw new InvalidOperationException("Storage limits are outside the supported safety bounds.");
    }
}

public sealed record TextExtractionResult(string Text, string Status, string Message);
public sealed record StoredMaterial(string StoragePath, string Text, long SizeBytes, string FileFormat,
    string ContentType, string ExtractionStatus, string ExtractionMessage);

public static class MaterialFormats
{
    public static readonly string[] Extensions = [".txt", ".pdf", ".docx", ".pptx", ".md"];
    public static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => throw new InvalidDataException("Chỉ hỗ trợ TXT, PDF, DOCX, PPTX và Markdown")
    };

    public static void ValidateMime(string extension, string? suppliedType)
    {
        var expected = ContentType(extension);
        var mime = suppliedType?.Split(';')[0].Trim().ToLowerInvariant();
        // Missing and generic MIME values are common browser behavior. The parser still
        // validates the actual bytes, archive structure, and document content type.
        if (string.IsNullOrEmpty(mime) || mime == "application/octet-stream" || mime == expected ||
            extension == ".md" && mime is "text/plain" or "text/x-markdown" ||
            extension is ".docx" or ".pptx" && mime is "application/zip" or "application/x-zip-compressed") return;
        throw new InvalidDataException("MIME type không phù hợp với định dạng tệp");
    }
}
