namespace Intermedia.Web.Models;

public sealed class AuthOptions
{
    public List<AuthUser> Users { get; set; } = new();
}

public sealed class AuthUser
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}