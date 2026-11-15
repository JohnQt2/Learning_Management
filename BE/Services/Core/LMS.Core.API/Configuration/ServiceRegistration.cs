using LMS.CoreApplicationServices.Common;

namespace LMS.Core.API.Configuration;

/// <summary>Đăng ký các Service nghiệp vụ của tầng ApplicationServices vào DI.</summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Đăng ký mọi lớp public <c>*Service</c> nằm trong namespace <c>...Implements</c> của LMS.CoreApplicationServices
    /// với vòng đời Scoped (một instance cho mỗi request, dùng chung DbContext của request đó).
    /// Nếu module có interface <c>I{TênLớp}</c> (thư mục Abstracts), interface được trỏ tới cùng instance đó,
    /// nên Controller chỉ cần nhận interface. Module mới tuân theo cấu trúc Abstracts/Dtos/Implements
    /// sẽ tự được đăng ký, không phải sửa Program.cs.
    /// </summary>
    public static IServiceCollection AddLmsApplicationServices(this IServiceCollection services)
    {
        var assembly = typeof(AppException).Assembly;
        var serviceTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } &&
                        t.Name.EndsWith("Service", StringComparison.Ordinal) &&
                        t.Namespace?.EndsWith(".Implements", StringComparison.Ordinal) == true)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var type in serviceTypes)
        {
            services.AddScoped(type);
            foreach (var contract in type.GetInterfaces().Where(i => i.Assembly == assembly && i.Name == "I" + type.Name))
                services.AddScoped(contract, provider => provider.GetRequiredService(type));
        }
        return services;
    }
}
