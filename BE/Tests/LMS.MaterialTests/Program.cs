using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LMS.MaterialTests;

internal static class MaterialEntry
{
    private static int _checks;
    private static readonly string PackageNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly string OfficeNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly string ContentNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly string PresentationNamespace = "http://schemas.openxmlformats.org/presentationml/2006/main";

    private static async Task Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-material-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var password = "Test!" + Guid.NewGuid().ToString("N") + "a1";
        var settings = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", ["DotEnv__Enabled"] = "false",
            ["Database__Provider"] = "Sqlite", ["Database__AutoMigrate"] = "true",
            ["ConnectionStrings__Sqlite"] = "Data Source=" + Path.Combine(directory, "test.sqlite"),
            ["Storage__RootPath"] = Path.Combine(directory, "Uploads"), ["Storage__MaxUploadBytes"] = "4096",
            ["Storage__MaxExtractedCharacters"] = "1024", ["Storage__MaxArchiveExpandedBytes"] = "20000",
            ["Storage__MaxArchiveEntries"] = "40", ["Storage__MaxPdfPages"] = "10", ["Storage__MaxPreviewCharacters"] = "16",
            ["Demo__Seed"] = "true", ["Demo__Password"] = password, ["Bootstrap__AdminUsername"] = "",
            ["Ai__Mode"] = "Disabled", ["Jwt__Key"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Cors__AllowedOrigins__0"] = "http://localhost:5173", ["RateLimiting__AuthPermitLimit"] = "100"
        };
        var originals = settings.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
        foreach (var setting in settings) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        try
        {
            await StorageChecks(Path.Combine(directory, "UnitUploads"));
            await BoundedPdfFilterTests.Run(Check);
            await HttpChecks(directory, password);
            Console.WriteLine($"MATERIAL TESTS PASSED: {_checks} assertions; genuine local parsers, isolated SQLite/HTTP; OCR and live Gemini NOT RUN.");
        }
        finally
        {
            foreach (var setting in originals) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            SqliteConnection.ClearAllPools();
            var full = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(full) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("lms-material-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup target");
            Directory.Delete(full, true);
        }
    }

    private static async Task StorageChecks(string directory)
    {
        IFileStorage storage = new LocalFileStorage(directory, new() { MaxUploadBytes = 4096, MaxExtractedCharacters = 1024, MaxArchiveEntries = 40, MaxArchiveExpandedBytes = 20000, MaxPdfPages = 10 });
        var original = Encoding.UTF8.GetBytes("\uFEFFTiếng Việt\r\nDòng 2\rDòng 3");
        var saved = await storage.SaveAndExtractAsync(new MemoryStream(original), "../../lesson.TXT", default);
        Check(saved.Text == "Tiếng Việt\nDòng 2\nDòng 3" && saved.StoragePath.Length == 36, "legacy storage interface; safe basename; UTF8 BOM and newline normalization");
        using (var read = storage.OpenRead(saved.StoragePath)) Check(ReadBytes(read).SequenceEqual(original), "original bytes preserved while extracted text normalized");
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(new byte[4097]), "large.txt", default), "actual stream bytes bounded");
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream([0xc3, 0x28]), "invalid.md", default), "invalid UTF8 Markdown rejected");
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream([0, 1, 2]), "binary.txt", default), "binary disguised as TXT rejected");
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 1025))), "characters.md", default), "extraction characters bounded without silent truncation");
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream("%PDF-1.4\n%%EOF"u8.ToArray()), "broken.pdf", default), "PDF magic alone insufficient; malformed structure rejected");
        await Invalid(() => storage.SaveWithMetadataAsync(new MemoryStream(original), "wrong.txt", "application/pdf", default), "declared MIME mismatch rejected");
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Docx()), "wrong.pptx", default), "DOCX renamed PPTX rejected by package content type");
        var malicious = WordEntries(); malicious["word/vbaProject.bin"] = "malicious macro";
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(malicious)), "macro.docx", default), "macro parts rejected without execution");
        malicious = WordEntries(); malicious["../escape.xml"] = "escape";
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(malicious)), "path.docx", default), "archive traversal entry rejected");
        malicious = WordEntries(); malicious["word/document.xml"] = "<!DOCTYPE x [<!ENTITY ext SYSTEM 'https://example.invalid/never'>]><w:document xmlns:w='" + WordNamespace + "'><w:body><w:p><w:r><w:t>&ext;</w:t></w:r></w:p></w:body></w:document>";
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(malicious)), "entity.docx", default), "DTD and external XML entity rejected");
        malicious = WordEntries(); malicious["word/unused.xml"] = new string('a', 20001);
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(malicious)), "compressed.docx", default), "compressed expansion total bounded before decompress");
        malicious = WordEntries(); malicious["word/document.xml"] = $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p><w:r><w:t><w:t>nested</w:t></w:t></w:r></w:p></w:body></w:document>";
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(malicious)), "nested.docx", default), "nested text elements rejected before tree text amplification");
        malicious = WordEntries(); malicious["word/document.xml"] = $"<w:document xmlns:w='{WordNamespace}'><w:body>" + string.Concat(Enumerable.Repeat("<w:sdt>", 70)) + string.Concat(Enumerable.Repeat("</w:sdt>", 70)) + "</w:body></w:document>";
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(malicious)), "deep.docx", default), "XML tree depth bounded before allocation");
        malicious = WordEntries(); for (var i = 0; i < 41; i++) malicious[$"word/unused{i}.xml"] = "x";
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(malicious)), "many.docx", default), "archive entry count bounded");
        var ppt = PresentationEntries(); ppt["ppt/_rels/presentation.xml.rels"] = $"<Relationships xmlns='{PackageNamespace}'><Relationship Id='slideA' Type='{OfficeNamespace}/slide' Target='https://example.invalid/slide.xml' TargetMode='External'/></Relationships>";
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Zip(ppt)), "external.pptx", default), "external slide target never fetched");
        var count = Directory.GetFiles(directory).Length;
        await Invalid(() => storage.SaveAndExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes("invalid")), "fake.docx", default), "Office signature checked");
        Check(Directory.GetFiles(directory).Length == count, "failed uploads remove only their generated file");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await storage.SaveAndExtractAsync(new MemoryStream(original), "cancel.txt", canceled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(Directory.GetFiles(directory).Length == count, "canceled upload cleans partial file"); }
        var scan = await storage.SaveWithMetadataAsync(new MemoryStream(Pdf(false, true)), "scan.pdf", "application/pdf", default);
        Check(scan.Text == "" && scan.ExtractionStatus == "NeedsOcr" && scan.ExtractionMessage.Contains("OCR"), "image-only PDF accurately reports NeedsOcr");
        var imageSlides = await storage.SaveWithMetadataAsync(new MemoryStream(Pptx(false)), "images.pptx", null, default);
        Check(imageSlides.ExtractionStatus == "NoText" && imageSlides.Text == "", "PPTX without text reports NoText");
        var outside = Path.Combine(Path.GetDirectoryName(directory)!, "preserve.txt"); File.WriteAllText(outside, "preserve");
        storage.Delete("../preserve.txt");
        Check(File.Exists(outside), "storage deletion preserves paths outside managed root");
        try { storage.OpenRead("../preserve.txt"); throw new InvalidOperationException("Traversal accepted"); }
        catch (FileNotFoundException) { Check(true, "storage read rejects traversal"); }
    }

    private static async Task HttpChecks(string directory, string password)
    {
        int courseId, keepId, legacyId, deletedId;
        byte[] keepBytes = Docx();
        using (var host = new TestHost())
        {
            using var anonymous = host.CreateClient();
            using var teacher = await Login(host, "lecturer", password);
            using var admin = await Login(host, "admin", password);
            using var student = await Login(host, "student", password);
            await Send(admin, "POST", "/api/admin/users/lecturers", new { username = "material_other", fullName = "Other Lecturer", password }, 201);
            using var other = await Login(host, "material_other", password);
            var course = await Send(teacher, "POST", "/api/courses", new { code = "MAT101", title = "Material course" }, 201);
            courseId = course!["id"]!.GetValue<int>();
            var topic = await Send(teacher, "POST", $"/api/courses/{courseId}/topics", new { name = "Extraction" }, 201);
            var topicId = topic!["id"]!.GetValue<int>();
            await Send(anonymous, "GET", "/api/materials/upload-policy", null, 401);
            var policy = await Send(teacher, "GET", "/api/materials/upload-policy", null, 200);
            Check(policy!["maxUploadBytes"]!.GetValue<long>() == 4096 && policy["extensions"]!.AsArray().Count == 5 && policy["maxPreviewCharacters"]!.GetValue<int>() == 16, "upload policy reflects configured limits and five formats");
            var capabilities = await Send(student, "GET", "/api/materials/capabilities", null, 200);
            Check(capabilities!["supportedExtensions"]!.AsArray().Any(x => x!.GetValue<string>() == ".pptx"), "capability alias supports authenticated UI discovery");
            await Upload(other, courseId, topicId, "denied.txt", Encoding.UTF8.GetBytes("denied"), "text/plain", 403);
            await Upload(student, courseId, topicId, "denied.txt", Encoding.UTF8.GetBytes("denied"), "text/plain", 403);
            await Upload(teacher, courseId, topicId + 999, "bad-topic.txt", Encoding.UTF8.GetBytes("denied"), "text/plain", 400);
            var formats = new (string Name, string Mime, byte[] Bytes, string Expected)[]
            {
                ("vi.txt", "text/plain", Encoding.UTF8.GetBytes("\uFEFFTiếng Việt\r\nBài học dài để xem trước"), "Tiếng Việt\nBài học"),
                ("lesson.md", "text/markdown", Encoding.UTF8.GetBytes("# Tiêu đề\r\n**Nội dung** và bảng"), "# Tiêu đề\n**Nội dung**"),
                ("text.pdf", "application/pdf", Pdf(true), "LMS PDF text"),
                ("lesson.docx", MaterialFormats.ContentType(".docx"), keepBytes, "Đoạn đầu\nÔ A\tÔ B\nĐoạn cuối"),
                ("slides.pptx", MaterialFormats.ContentType(".pptx"), Pptx(true), "[Slide 1]\nFirst slide\nTable text\n\n[Slide 2]\nSecond slide")
            };
            var uploaded = new List<int>();
            foreach (var format in formats)
            {
                var material = await Upload(teacher, courseId, topicId, format.Name, format.Bytes, format.Mime, 201);
                var id = material!["id"]!.GetValue<int>(); uploaded.Add(id);
                Check(material["fileFormat"]!.GetValue<string>() == Path.GetExtension(format.Name)[1..] && material["sizeBytes"]!.GetValue<long>() == format.Bytes.Length &&
                    material["uploadedById"] != null && material["uploadedAt"] != null && material["extractionStatus"]!.GetValue<string>() == "Extracted" && material["canGenerateAI"]!.GetValue<bool>(), "actual metadata and AI eligibility for " + format.Name);
                await Send(anonymous, "GET", $"/api/materials/{id}/download", null, 401);
                await Send(student, "GET", $"/api/materials/{id}/extracted-text", null, 403);
                using var download = await teacher.GetAsync($"/api/materials/{id}/download");
                Check(download.IsSuccessStatusCode && (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(format.Bytes) && download.Content.Headers.ContentType!.MediaType == format.Mime, "original download bytes and server MIME for " + format.Name);
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                var row = await db.LearningMaterials.AsNoTracking().SingleAsync(m => m.Id == id);
                Check(row.ExtractedText.Contains(format.Expected, StringComparison.Ordinal), "real parser preserves meaningful text/order for " + format.Name);
                var preview = await Send(teacher, "GET", $"/api/materials/{id}/extracted-text", null, 200);
                Check(preview!["text"]!.GetValue<string>().Length <= 16 && preview["characterCount"]!.GetValue<int>() == row.ExtractedText.Length &&
                    preview["truncated"]!.GetValue<bool>() == (row.ExtractedText.Length > 16), "bounded preview reports full character count for " + format.Name);
            }
            keepId = uploaded[3]; deletedId = uploaded[0];
            await Send(student, "POST", "/api/courses/enroll", new { joinCode = course["joinCode"]!.GetValue<string>() }, 200);
            await Send(student, "GET", $"/api/materials/{keepId}/extracted-text", null, 200);
            await Send(other, "GET", $"/api/materials/{keepId}/extracted-text", null, 403);
            await Send(student, "DELETE", $"/api/materials/{deletedId}", null, 403);
            await Send(other, "DELETE", $"/api/materials/{deletedId}", null, 403);
            await Send(other, "POST", $"/api/materials/{keepId}/extract", new { }, 403);
            await Upload(teacher, courseId, topicId, "mime.txt", Encoding.UTF8.GetBytes("safe text"), "text/html", 400);
            await Upload(teacher, courseId, topicId, "fake.pdf", Encoding.UTF8.GetBytes("fake"), "application/pdf", 400);
            await Upload(teacher, courseId, topicId, "fake.docx", Pptx(true), MaterialFormats.ContentType(".docx"), 400);
            await Upload(teacher, courseId, topicId, "oversize.txt", Encoding.UTF8.GetBytes(new string('a', 4097)), "text/plain", 400);
            await Upload(teacher, courseId, topicId, "overcharacters.txt", Encoding.UTF8.GetBytes(new string('a', 1025)), "text/plain", 400);
            var scan = await Upload(teacher, courseId, topicId, "scan.pdf", Pdf(false, true), "application/pdf", 201);
            Check(scan!["extractionStatus"]!.GetValue<string>() == "NeedsOcr" && !scan["canGenerateAI"]!.GetValue<bool>(), "scan PDF upload succeeds with honest OCR limitation");
            var images = await Upload(teacher, courseId, topicId, "images.pptx", Pptx(false), MaterialFormats.ContentType(".pptx"), 201);
            Check(images!["extractionStatus"]!.GetValue<string>() == "NoText" && !images["canGenerateAI"]!.GetValue<bool>(), "image-only slides cannot generate questions");
            string originalStoragePath;
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                var material = await db.LearningMaterials.FindAsync(deletedId); originalStoragePath = material!.StoragePath;
                var question = new Question { CourseId = courseId, TopicId = topicId, MaterialId = deletedId, Content = "Historical question", Status = "Approved", Options = [new() { Content = "A", IsCorrect = true }, new() { Content = "B" }, new() { Content = "C" }, new() { Content = "D" }] };
                var quiz = new Quiz { CourseId = courseId, Title = "Historical quiz", Status = "Published", QuizQuestions = [new() { Question = question }] };
                db.Quizzes.Add(quiz);
                IFileStorage storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
                var saved = await storage.SaveAndExtractAsync(new MemoryStream(Pdf(true)), "legacy.pdf", default);
                var legacy = new LearningMaterial { CourseId = courseId, TopicId = topicId, Title = "Legacy PDF", OriginalFileName = "legacy.pdf", StoragePath = saved.StoragePath, ExtractionStatus = "Legacy" };
                db.LearningMaterials.Add(legacy); await db.SaveChangesAsync(); legacyId = legacy.Id;
            }
            var legacyList = await Send(teacher, "GET", $"/api/courses/{courseId}/materials", null, 200);
            var old = legacyList!.AsArray().Single(m => m!["id"]!.GetValue<int>() == legacyId)!;
            Check(old["uploadedAt"] == null && old["uploadedById"] == null && old["sizeBytes"] == null && old["extractionStatus"]!.GetValue<string>() == "Legacy", "legacy unknown metadata remains null");
            var extracted = await Send(teacher, "POST", $"/api/materials/{legacyId}/extract", new { }, 200);
            Check(extracted!["canGenerateAI"]!.GetValue<bool>() && extracted["sizeBytes"]!.GetValue<long>() > 0 && extracted["uploadedAt"] == null && extracted["uploadedById"] == null, "legacy PDF re-extracts actual original without fabricating uploader/time");
            await Send(teacher, "DELETE", $"/api/materials/{deletedId}", null, 204);
            await Send(teacher, "DELETE", $"/api/materials/{deletedId}", null, 204);
            await Send(teacher, "GET", $"/api/materials/{deletedId}/download", null, 404);
            await Send(student, "GET", $"/api/materials/{deletedId}/extracted-text", null, 404);
            await Send(teacher, "POST", $"/api/materials/{deletedId}/extract", new { }, 404);
            var list = await Send(teacher, "GET", $"/api/courses/{courseId}/materials", null, 200);
            Check(!list!.AsArray().Any(m => m!["id"]!.GetValue<int>() == deletedId), "soft-deleted material hidden from list");
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                Check(await db.LearningMaterials.AnyAsync(m => m.Id == deletedId && m.IsDeleted) &&
                    await db.Questions.AnyAsync(q => q.MaterialId == deletedId) && await db.QuizQuestions.AnyAsync(), "soft delete retains original FK row, Question and Quiz");
                Check(File.Exists(Path.Combine(directory, "Uploads", originalStoragePath)), "soft delete preserves original file");
            }
        }
        using (var restarted = new TestHost())
        {
            using var teacher = await Login(restarted, "lecturer", password);
            Check((await teacher.GetByteArrayAsync($"/api/materials/{keepId}/download")).SequenceEqual(keepBytes), "document original persists across host restart");
            var list = await Send(teacher, "GET", $"/api/courses/{courseId}/materials", null, 200);
            Check(!list!.AsArray().Any(m => m!["id"]!.GetValue<int>() == deletedId) && list.AsArray().Single(m => m!["id"]!.GetValue<int>() == legacyId)!["canGenerateAI"]!.GetValue<bool>(), "extraction metadata and deletion persist across restart");
        }
    }

    private static byte[] ReadBytes(Stream stream) { using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); }
    private static async Task Invalid(Func<Task> operation, string label)
    {
        try { await operation(); throw new InvalidOperationException("Expected invalid-data rejection: " + label); }
        catch (InvalidDataException) { Check(true, label); }
    }
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException("FAIL " + message); _checks++; Console.WriteLine("PASS " + message); }
    private static async Task<HttpClient> Login(TestHost host, string username, string password)
    {
        using var client = host.CreateClient();
        var login = await Send(client, "POST", "/api/auth/login", new { username, password }, 200);
        var authenticated = host.CreateClient(); authenticated.DefaultRequestHeaders.Authorization = new("Bearer", login!["accessToken"]!.GetValue<string>()); return authenticated;
    }
    private static async Task<JsonNode?> Send(HttpClient client, string method, string path, object? body, int expected)
    {
        using var request = new HttpRequestMessage(new(method), path); if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == expected, $"{method} {path}: HTTP {(int)response.StatusCode}, expected {expected}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
    private static async Task<JsonNode?> Upload(HttpClient client, int course, int topic, string name, byte[] bytes, string mime, int expected)
    {
        using var form = new MultipartFormDataContent(); using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(mime); form.Add(content, "file", name); form.Add(new StringContent(topic.ToString()), "topicId");
        using var response = await client.PostAsync($"/api/courses/{course}/materials", form); var text = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == expected, $"upload {name}: HTTP {(int)response.StatusCode}, expected {expected}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
    private static byte[] Docx() => Zip(WordEntries());
    private static Dictionary<string, string> WordEntries() => new()
    {
        ["[Content_Types].xml"] = $"<Types xmlns='{ContentNamespace}'><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>",
        ["_rels/.rels"] = $"<Relationships xmlns='{PackageNamespace}'><Relationship Id='main' Type='{OfficeNamespace}/officeDocument' Target='word/document.xml'/></Relationships>",
        ["word/document.xml"] = $"<w:document xmlns:w='{WordNamespace}'><w:body><w:p><w:r><w:t>Đoạn đầu</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Ô A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Ô B</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p><w:r><w:t>Đoạn cuối</w:t></w:r></w:p></w:body></w:document>"
    };
    private static byte[] Pptx(bool text) => Zip(PresentationEntries(text));
    private static Dictionary<string, string> PresentationEntries(bool text = true) => new()
    {
        ["[Content_Types].xml"] = $"<Types xmlns='{ContentNamespace}'><Override PartName='/ppt/presentation.xml' ContentType='application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml'/></Types>",
        ["_rels/.rels"] = $"<Relationships xmlns='{PackageNamespace}'><Relationship Id='main' Type='{OfficeNamespace}/officeDocument' Target='ppt/presentation.xml'/></Relationships>",
        ["ppt/presentation.xml"] = $"<p:presentation xmlns:p='{PresentationNamespace}' xmlns:r='{OfficeNamespace}'><p:sldIdLst><p:sldId id='256' r:id='slideA'/><p:sldId id='257' r:id='slideB'/></p:sldIdLst></p:presentation>",
        ["ppt/_rels/presentation.xml.rels"] = $"<Relationships xmlns='{PackageNamespace}'><Relationship Id='slideB' Type='{OfficeNamespace}/slide' Target='slides/slide2.xml'/><Relationship Id='slideA' Type='{OfficeNamespace}/slide' Target='slides/slide10.xml'/></Relationships>",
        ["ppt/slides/slide2.xml"] = $"<p:sld xmlns:p='{PresentationNamespace}' xmlns:a='{DrawingNamespace}'><p:cSld><p:spTree>{(text ? "<p:sp><p:txBody><a:p><a:r><a:t>Second slide</a:t></a:r></a:p></p:txBody></p:sp>" : "<p:pic/>")}</p:spTree></p:cSld></p:sld>",
        ["ppt/slides/slide10.xml"] = $"<p:sld xmlns:p='{PresentationNamespace}' xmlns:a='{DrawingNamespace}'><p:cSld><p:spTree>{(text ? "<p:sp><p:txBody><a:p><a:r><a:t>First slide</a:t></a:r></a:p></p:txBody></p:sp><p:graphicFrame><a:tbl><a:tr><a:tc><a:txBody><a:p><a:r><a:t>Table text</a:t></a:r></a:p></a:txBody></a:tc></a:tr></a:tbl></p:graphicFrame>" : "<p:pic/>")}</p:spTree></p:cSld></p:sld>"
    };
    private static byte[] Zip(Dictionary<string, string> entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var entry in entries)
        { using var stream = zip.CreateEntry(entry.Key, CompressionLevel.SmallestSize).Open(); var bytes = Encoding.UTF8.GetBytes(entry.Value); stream.Write(bytes); }
        return memory.ToArray();
    }
    private static byte[] Pdf(bool text, bool image = false)
    {
        var content = text ? "BT /F1 12 Tf 20 100 Td (LMS PDF text) Tj ET" : image ? "q 100 0 0 100 20 20 cm /Im1 Do Q" : "";
        var resources = text ? "/Font << /F1 5 0 R >>" : image ? "/XObject << /Im1 5 0 R >>" : "";
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << {resources} >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream" };
        if (text) objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        else if (image) objects.Add("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>\nstream\nabc\nendstream");
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = pdf.Length; pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return Encoding.ASCII.GetBytes(pdf.ToString());
    }
    private sealed class TestHost : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "LMS.sln"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Cannot find BE/LMS.sln");
            builder.UseContentRoot(Path.Combine(root.FullName, "Services", "Core", "LMS.Core.API"));
            builder.UseEnvironment("Development"); builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
