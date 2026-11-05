namespace LMS.Core.Infrastructure.Adapters;

public interface IFileStorage
{
    Task<(string StoragePath, string Text)> SaveAndExtractAsync(Stream input, string fileName, CancellationToken ct);
    void Delete(string storagePath);
    Stream OpenRead(string storagePath);
    MaterialStorageOptions Limits => new();
    async Task<StoredMaterial> SaveWithMetadataAsync(Stream input, string fileName, string? contentType, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        MaterialFormats.ValidateMime(extension, contentType);
        var saved = await SaveAndExtractAsync(input, fileName, ct);
        using var stream = OpenRead(saved.StoragePath);
        return new(saved.StoragePath, saved.Text, stream.Length, extension.TrimStart('.'), MaterialFormats.ContentType(extension),
            string.IsNullOrWhiteSpace(saved.Text) ? extension == ".pdf" ? "NeedsOcr" : "NoText" : "Extracted", "");
    }
    async Task<StoredMaterial> ExtractExistingAsync(string storagePath, string fileName, CancellationToken ct)
    {
        using var stream = OpenRead(storagePath);
        if (stream.Length > Limits.MaxUploadBytes) throw new InvalidDataException("Tệp vượt giới hạn dung lượng được cấu hình");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var result = await new MaterialTextExtractor(Limits).ExtractAsync(stream, extension, ct);
        return new(storagePath, result.Text, stream.Length, extension.TrimStart('.'), MaterialFormats.ContentType(extension), result.Status, result.Message);
    }
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly ITextExtractor _extractor;
    public MaterialStorageOptions Limits { get; }
    public LocalFileStorage(string root, MaterialStorageOptions? options = null)
    {
        Limits = options ?? new();
        Limits.Validate();
        _extractor = new MaterialTextExtractor(Limits);
        _root = Path.GetFullPath(root);
        RequireSafeRoot();
        Directory.CreateDirectory(_root);
        RequireSafeRoot();
    }

    private void RequireSafeRoot()
    {
        // Do not follow junctions/symbolic links outside the managed directory.
        for (DirectoryInfo? directory = new(_root); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Thư mục học liệu không được là liên kết");
    }

    public async Task<(string StoragePath, string Text)> SaveAndExtractAsync(Stream input, string fileName, CancellationToken ct)
    {
        var result = await SaveWithMetadataAsync(input, fileName, null, ct);
        return (result.StoragePath, result.Text);
    }

    public async Task<StoredMaterial> SaveWithMetadataAsync(Stream input, string fileName, string? contentType, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        MaterialFormats.ValidateMime(ext, contentType);
        var safeName = $"{Guid.NewGuid():N}{ext}";
        var path = ValidPath(safeName);
        try
        {
            long copied = 0;
            // Enforce the limit on actual received bytes, not just the Content-Length header.
            await using (var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) != 0)
                {
                    copied += read;
                    if (copied > Limits.MaxUploadBytes) throw new InvalidDataException("Tệp vượt giới hạn dung lượng được cấu hình");
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (copied == 0) throw new InvalidDataException("File trống");
            }
            await using var file = File.OpenRead(path);
            var extracted = await _extractor.ExtractAsync(file, ext, ct);
            return new(safeName, extracted.Text, copied, ext.TrimStart('.'), MaterialFormats.ContentType(ext), extracted.Status, extracted.Message);
        }
        catch
        {
            Cleanup(safeName);
            throw;
        }
    }

    private string ValidPath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || Path.GetFileName(storagePath) != storagePath ||
            !System.Text.RegularExpressions.Regex.IsMatch(storagePath, @"\A[a-f0-9]{32}\.(txt|pdf|md|docx|pptx)\z"))
            throw new FileNotFoundException("Không tìm thấy học liệu");
        RequireSafeRoot();
        var path = Path.Combine(_root, storagePath);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("Không tìm thấy học liệu");
        return path;
    }
    public Stream OpenRead(string storagePath) => File.OpenRead(ValidPath(storagePath));
    public void Delete(string storagePath)
    {
        try
        {
            var path = ValidPath(storagePath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (FileNotFoundException) { /* No file to delete */ }
    }

    private void Cleanup(string storagePath)
    {
        try { Delete(storagePath); }
        catch (IOException) { /* Preserve the original save/validation failure. */ }
        catch (UnauthorizedAccessException) { /* Preserve the original save/validation failure. */ }
    }
}
