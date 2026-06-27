using System.Text;

namespace TaskTracker.Core.Storage;

public static class GroupFileNames
{
    public const string Extension = ".group.json";

    public static string BuildFileName(string groupName, string groupId)
    {
        TaskValidation.ValidateGroupName(groupName);

        var slug = Slugify(groupName);
        var shortId = groupId.Length <= 8 ? groupId : groupId[..8];
        return $"{slug}-{shortId}{Extension}";
    }

    public static string Slugify(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, 48));
        var pendingDash = false;

        foreach (var raw in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(raw))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(raw);
                pendingDash = false;
            }
            else if (char.IsWhiteSpace(raw) || raw is '-' or '_' or '.')
            {
                pendingDash = builder.Length > 0;
            }

            if (builder.Length >= 48)
            {
                break;
            }
        }

        return builder.Length == 0 ? "group" : builder.ToString().Trim('-');
    }
}
