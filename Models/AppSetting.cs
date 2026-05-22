public class AppSetting
{
    public int Id { get; set; }

    public string Type { get; set; }

    public string Value { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }
}
