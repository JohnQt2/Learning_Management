using System.Security.Claims;
namespace LMS.Core.API.Controllers;
public static class CurrentUser
{
    public static int Id(ClaimsPrincipal user)
        => int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var x) ? x : 0;
    public static string Role(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Role) ?? "";
}
