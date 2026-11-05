namespace LMS.CoreApplicationServices.MaterialModule.Dtos;
public record MaterialDto(int Id, int CourseId, int TopicId, string Title, string OriginalFileName, bool CanGenerateAI,
    string FileFormat = "", string ContentType = "", long? SizeBytes = null, int? UploadedById = null,
    DateTime? UploadedAt = null, string ExtractionStatus = "Legacy", string ExtractionMessage = "", int ExtractedCharacterCount = 0);
public record MaterialTextDto(int MaterialId, string Text, string Status, string Message, bool Truncated, int CharacterCount);
public record MaterialUploadPolicyDto(long MaxUploadBytes, string[] Extensions, int MaxPreviewCharacters, string[] SupportedExtensions);
