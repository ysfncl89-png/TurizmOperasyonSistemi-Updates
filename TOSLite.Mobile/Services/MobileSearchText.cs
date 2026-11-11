using System.Text;

namespace TOSLite.Mobile.Services;

public static class MobileSearchText
{
    public static bool Contains(string? value, string? query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return Normalize(value).Contains(Normalize(query), StringComparison.Ordinal);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var normalized = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            normalized.Append(character is 'I' or 'İ' or 'ı' or 'i'
                ? 'i'
                : char.ToUpperInvariant(character));
        }

        return normalized.ToString();
    }
}
