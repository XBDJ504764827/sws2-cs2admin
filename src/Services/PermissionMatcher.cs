namespace Sws2Cs2Admin.Services;

/// <summary>
/// Matches admin permission nodes with wildcard support.
/// </summary>
internal static class PermissionMatcher
{
    public static bool Matches(IEnumerable<string> granted, string required)
    {
        if (string.IsNullOrWhiteSpace(required))
        {
            return true;
        }

        foreach (var permission in granted)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                continue;
            }

            if (permission.Equals("*", StringComparison.OrdinalIgnoreCase)
                || permission.Equals(required, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (permission.EndsWith(".*", StringComparison.Ordinal)
                && required.StartsWith(permission[..^1], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
