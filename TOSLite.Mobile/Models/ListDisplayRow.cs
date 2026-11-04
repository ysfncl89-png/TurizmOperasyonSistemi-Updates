namespace TOSLite.Mobile.Models;

public sealed class ListDisplayRow
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public Dictionary<string, string> ExportValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
