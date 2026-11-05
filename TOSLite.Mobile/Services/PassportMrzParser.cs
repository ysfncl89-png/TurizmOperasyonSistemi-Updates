using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TOSLite.Mobile.Services;

public sealed record PassportExtractedData(
    string? FirstName,
    string? LastName,
    string? DocumentNumber,
    string? NationalityCode,
    string? GenderCode,
    DateTime? BirthDate,
    DateTime? PassportStartDate,
    DateTime? PassportEndDate,
    string? TcKimlikNo);

public static class PassportMrzParser
{
    private static readonly string[] IssueDateLabels =
    [
        "date of issue", "date of issuance", "date issued", "issue date", "issued on", "date de délivrance",
        "date de delivrance", "düzenlenme tarihi", "düzenlenme", "düzenleme tarihi", "düzenleme", "veriliş tarihi", "verilis tarihi", "verildiği tarih", "verildigi tarih",
        "date of delivery", "date of grant", "date of commencement", "valid from", "validity from", "date de début",
        "date de debut", "fecha de expedición", "fecha de expedicion", "fecha de emisión", "fecha de emision",
        "ausstellungsdatum", "gultig ab", "gültig ab", "data di rilascio", "datum izdavanja"
    ];

    private static readonly string[] ExpiryDateLabels =
    [
        "date of expiry", "date of expiration", "expiry date", "expiration date", "valid until", "valid to",
        "data di scadenza", "gültig bis", "fecha de caducidad", "date d'expiration"
    ];

    public static PassportExtractedData? Parse(string? recognizedText)
    {
        var lines = (recognizedText ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeMrzLine)
            .Select(RepairNameLinePrefix)
            .Where(line => line.Length >= 25)
            .ToList();

        for (var nameLineIndex = 0; nameLineIndex < lines.Count; nameLineIndex++)
        {
            var nameLine = lines[nameLineIndex];
            if (!nameLine.StartsWith("P<", StringComparison.Ordinal) || nameLine.Length < 25)
            {
                continue;
            }

            var dataLine = GetDataLineCandidates(lines, nameLineIndex + 1)
                .Select(ParseMrzDataLine)
                .FirstOrDefault(fields => fields is not null);
            if (dataLine is null)
            {
                continue;
            }

            var nameData = nameLine.Length > 5 ? nameLine[5..] : string.Empty;
            var nameParts = nameData.Split("<<", 2, StringSplitOptions.None);
            var lastName = CleanMrzName(nameParts[0]);
            var firstName = nameParts.Length > 1 ? CleanMrzName(nameParts[1]) : string.Empty;
            var documentNumber = dataLine.DocumentNumber;
            var nationalityCode = dataLine.NationalityCode;
            var birthDate = dataLine.BirthDate;
            var genderCode = dataLine.GenderCode;
            var passportEndDate = dataLine.PassportEndDate;

            if (string.IsNullOrWhiteSpace(firstName)
                && string.IsNullOrWhiteSpace(lastName)
                && string.IsNullOrWhiteSpace(documentNumber)
                && !birthDate.HasValue
                && !passportEndDate.HasValue)
            {
                continue;
            }

            return new PassportExtractedData(
                firstName,
                lastName,
                documentNumber,
                nationalityCode,
                genderCode,
                birthDate,
                FindIssueDate(recognizedText ?? string.Empty),
                passportEndDate,
                FindTcKimlikNo(recognizedText ?? string.Empty));
        }

        return null;
    }

    private static IEnumerable<string> GetDataLineCandidates(IReadOnlyList<string> lines, int startIndex)
    {
        for (var index = startIndex; index < Math.Min(lines.Count, startIndex + 4); index++)
        {
            var candidate = new StringBuilder();
            for (var end = index; end < Math.Min(lines.Count, index + 3); end++)
            {
                candidate.Append(lines[end]);
                if (candidate.Length >= 25)
                {
                    yield return candidate.ToString();
                }
            }
        }
    }

    private static string NormalizeMrzLine(string line)
    {
        var normalized = line.ToUpperInvariant()
            .Replace('«', '<')
            .Replace('‹', '<')
            .Replace('〈', '<')
            .Replace('＜', '<');
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '<')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string RepairNameLinePrefix(string line)
    {
        if (line.Length >= 4
            && line[0] == 'P'
            && line[1] != '<'
            && line.Substring(1, 3).All(character => character is >= 'A' and <= 'Z'))
        {
            return string.Concat(line.AsSpan(0, 1), "<", line.AsSpan(1));
        }

        return line;
    }

    private static MrzDataFields? ParseMrzDataLine(string line)
    {
        if (line.Length < 25)
        {
            return null;
        }

        for (var birthStart = 10; birthStart <= 18 && birthStart + 13 <= line.Length; birthStart++)
        {
            var nationalityStart = birthStart - 3;
            if (nationalityStart < 1)
            {
                continue;
            }

            var nationality = line.Substring(nationalityStart, 3);
            if (nationality.Length != 3 || !nationality.All(character => character is >= 'A' and <= 'Z'))
            {
                continue;
            }

            var birthDate = ParseMrzDate(line.Substring(birthStart, 6), isExpiration: false);
            if (!birthDate.HasValue)
            {
                continue;
            }

            for (var genderIndex = birthStart + 6; genderIndex <= birthStart + 7; genderIndex++)
            {
                if (genderIndex + 7 > line.Length || line[genderIndex] is not ('M' or 'F' or '<'))
                {
                    continue;
                }

                var expirationDate = ParseMrzDate(line.Substring(genderIndex + 1, 6), isExpiration: true);
                if (!expirationDate.HasValue)
                {
                    continue;
                }

                var documentNumber = line[..(nationalityStart - 1)]
                    .Replace("<", string.Empty, StringComparison.Ordinal)
                    .Trim();
                if (string.IsNullOrWhiteSpace(documentNumber))
                {
                    continue;
                }

                return new MrzDataFields(documentNumber, nationality, birthDate, line[genderIndex] == '<' ? null : line[genderIndex].ToString(), expirationDate);
            }
        }

        return null;
    }

    private static string CleanMrzName(string value) =>
        string.Join(" ", value.Split('<', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static DateTime? ParseMrzDate(string value, bool isExpiration)
    {
        var digits = value.Replace('O', '0').Replace('Q', '0')
            .Replace('I', '1').Replace('L', '1')
            .Replace('S', '5').Replace('B', '8');
        if (digits.Length != 6 || !digits.All(char.IsDigit))
        {
            return null;
        }

        var shortYear = int.Parse(digits[..2], CultureInfo.InvariantCulture);
        var month = int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture);
        var currentYear = DateTime.Today.Year;
        var year = isExpiration
            ? 2000 + shortYear
            : (shortYear <= currentYear % 100 ? 2000 : 1900) + shortYear;

        if (isExpiration && year > currentYear + 20)
        {
            year -= 100;
        }

        if (!isExpiration && year > currentYear)
        {
            year -= 100;
        }

        try
        {
            return new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime? FindIssueDate(string recognizedText)
    {
        var lines = recognizedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            for (var labelLineCount = 1; labelLineCount <= 3 && index + labelLineCount <= lines.Length; labelLineCount++)
            {
                var labelText = string.Join(" ", lines.Skip(index).Take(labelLineCount));
                var normalizedLabel = NormalizeLabel(labelText);
                if (ExpiryDateLabels.Any(candidate => normalizedLabel.Contains(NormalizeLabel(candidate), StringComparison.Ordinal)))
                {
                    break;
                }

                if (!IssueDateLabels.Any(candidate => normalizedLabel.Contains(NormalizeLabel(candidate), StringComparison.Ordinal)))
                {
                    continue;
                }

                for (var dateLineIndex = index + labelLineCount - 1; dateLineIndex < Math.Min(lines.Length, index + labelLineCount + 2); dateLineIndex++)
                {
                    var normalizedDateLine = NormalizeLabel(lines[dateLineIndex]);
                    if (ExpiryDateLabels.Any(candidate => normalizedDateLine.Contains(NormalizeLabel(candidate), StringComparison.Ordinal)))
                    {
                        break;
                    }

                    if (FindDate(lines[dateLineIndex]) is { } date)
                    {
                        return date;
                    }
                }

                break;
            }
        }

        return null;
    }

    private static DateTime? FindDate(string value)
    {
        var match = Regex.Match(value, @"(?<!\d)(\d{1,2})[./ -](\d{1,2})[./ -](\d{2,4})(?!\d)|(?<!\d)(\d{4})[./ -](\d{1,2})[./ -](\d{1,2})(?!\d)");
        if (!match.Success)
        {
            return null;
        }

        var dateText = match.Value.Replace('-', '/').Replace('.', '/');
        var formats = new[] { "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy", "yyyy/M/d", "yyyy/MM/dd" };
        if (DateTime.TryParseExact(dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.Date;
        }

        var namedDate = Regex.Match(value, @"(?<!\d)(\d{1,2})[\s,./-]+([A-ZĞÜŞİÖÇ]{3,10})[\s,./-]+(\d{2,4})(?!\d)|(?<!\d)([A-ZĞÜŞİÖÇ]{3,10})[\s,./-]+(\d{1,2})[\s,./-]+(\d{2,4})(?!\d)", RegexOptions.IgnoreCase);
        if (!namedDate.Success)
        {
            return null;
        }

        var namedDateText = namedDate.Groups[1].Success
            ? $"{namedDate.Groups[1].Value} {namedDate.Groups[2].Value} {namedDate.Groups[3].Value}"
            : $"{namedDate.Groups[5].Value} {namedDate.Groups[4].Value} {namedDate.Groups[6].Value}";
        var namedFormats = new[] { "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy", "MMM d yyyy", "MMMM d yyyy", "d MMM yy", "dd MMM yy", "d MMMM yy", "dd MMMM yy", "MMM d yy", "MMMM d yy" };
        return DateTime.TryParseExact(namedDateText, namedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParseExact(namedDateText, namedFormats, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out date)
            ? date.Date
            : null;
    }

    private static string? FindTcKimlikNo(string recognizedText)
    {
        var lines = recognizedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var label = NormalizeLabel(lines[index]);
            if (!label.Contains("tckimlik", StringComparison.Ordinal)
                && !label.Contains("kimlikno", StringComparison.Ordinal)
                && !label.Contains("turkishidentity", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Regex.Match(lines[index], @"(?<!\d)\d{11}(?!\d)");
            if (!match.Success && index + 1 < lines.Length)
            {
                match = Regex.Match(lines[index + 1], @"(?<!\d)\d{11}(?!\d)");
            }

            if (match.Success && IsValidTcKimlikNo(match.Value))
            {
                return match.Value;
            }
        }

        return Regex.Matches(recognizedText, @"(?<!\d)\d{11}(?!\d)")
            .Select(match => match.Value)
            .FirstOrDefault(IsValidTcKimlikNo);
    }

    private static string NormalizeLabel(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsValidTcKimlikNo(string value)
    {
        if (value.Length != 11 || value[0] == '0')
        {
            return false;
        }

        var digits = value.Select(character => character - '0').ToArray();
        var oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var evenSum = digits[1] + digits[3] + digits[5] + digits[7];
        return (oddSum * 7 - evenSum) % 10 == digits[9]
            && digits.Take(10).Sum() % 10 == digits[10];
    }

    private sealed record MrzDataFields(
        string DocumentNumber,
        string NationalityCode,
        DateTime? BirthDate,
        string? GenderCode,
        DateTime? PassportEndDate);
}
