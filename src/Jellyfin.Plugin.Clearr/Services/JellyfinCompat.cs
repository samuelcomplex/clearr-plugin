using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Clearr.Services;

/// <summary>
/// Bridges small API differences between Jellyfin versions so one plugin package runs everywhere.
/// Jellyfin 10.11.0 lists users with the <c>Users</c> property; later 10.11.x and 12.x use <c>GetUsers()</c>.
/// </summary>
public static class JellyfinCompat
{
    private static readonly System.Reflection.MethodInfo? GetUsersMethod = typeof(IUserManager).GetMethod("GetUsers", Type.EmptyTypes);
    private static readonly System.Reflection.PropertyInfo? UsersProperty = typeof(IUserManager).GetProperty("Users");

    public static IEnumerable<User> AllUsers(IUserManager users)
    {
        if (GetUsersMethod != null)
        {
            return (IEnumerable<User>)GetUsersMethod.Invoke(users, null)!;
        }

        if (UsersProperty != null)
        {
            return (IEnumerable<User>)UsersProperty.GetValue(users)!;
        }

        throw new InvalidOperationException("This Jellyfin version has neither IUserManager.GetUsers() nor IUserManager.Users");
    }
}
