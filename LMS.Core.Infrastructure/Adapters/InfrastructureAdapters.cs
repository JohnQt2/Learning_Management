using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LMS.Core.Infrastructure.Adapters
{
    public interface IJwtTokenGenerator
    {
        string GenerateToken(int userId, string role);
    }

    public class JwtTokenGenerator : IJwtTokenGenerator
    {
        private readonly IConfiguration _config;
        public JwtTokenGenerator(IConfiguration config) => _config = config;

        public string GenerateToken(int userId, string role)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "SuperSecretKeyForLmsAiProject2026!@#"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"] ?? "LMS.AI",
                audience: _config["Jwt:Audience"] ?? "LMS.AI.Users",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public interface IFileStorage
    {
        Task<string> SaveAsync(Stream stream, string fileName);
    }

    public class LocalFileStorage : IFileStorage
    {
        public async Task<string> SaveAsync(Stream stream, string fileName)
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{Guid.NewGuid()}_{fileName}");
            using var fs = new FileStream(path, FileMode.Create);
            await stream.CopyToAsync(fs);
            return path;
        }
    }

    public interface ITextExtractor
    {
        Task<string> ExtractTextAsync(string storagePath, string contentType);
    }

    public class TextExtractor : ITextExtractor
    {
        public async Task<string> ExtractTextAsync(string storagePath, string contentType)
        {
            // Hỗ trợ đọc trực tiếp TXT và trích xuất nội dung văn bản
            if (storagePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return await File.ReadAllTextAsync(storagePath);

            // Với PDF, thực tế có thể tích hợp PdfPig/iText7; ở đây đọc nội dung text stream
            var raw = await File.ReadAllTextAsync(storagePath);
            return raw.Trim();
        }
    }

    public interface IAIClient
    {
        Task<string> GenerateQuestionsAsync(string prompt, TimeSpan timeout);
    }

    public class ExternalAIClient : IAIClient
    {
        private readonly HttpClient _httpClient;
        public ExternalAIClient(HttpClient httpClient) => _httpClient = httpClient;

        public async Task<string> GenerateQuestionsAsync(string prompt, TimeSpan timeout)
        {
            using var cts = new System.Threading.CancellationTokenSource(timeout);
            // Nếu chưa cấu hình API key ngoài, trả về mẫu JSON chuẩn phục vụ kiểm thử luồng vàng MVP
            await Task.Delay(500, cts.Token);
            return @"[
              {
                ""content"": ""Đâu là nguyên tắc bảo mật quan trọng nhất khi chấm điểm Quiz trong LMS-AI?"",
                ""explanation"": ""Điểm số luôn do backend tự tính bằng cách đối chiếu bảng AnswerOption trong CSDL."",
                ""options"": [
                  { ""content"": ""Backend tự đối chiếu AnswerOption trong CSDL"", ""isCorrect"": true },
                  { ""content"": ""Frontend tự tính điểm rồi gửi lên API"", ""isCorrect"": false },
                  { ""content"": ""Gửi trường IsCorrect xuống cho Sinh viên khi tải đề"", ""isCorrect"": false }
                ]
              }
            ]";
        }
    }
}
