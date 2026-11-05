using Microsoft.Maui.Controls.Shapes;

namespace TOSLite.Mobile;

public static class MobileTheme
{
    private static readonly Color Page = Color.FromArgb("#F8FAFC");
    private static readonly Color Surface = Colors.White;
    private static readonly Color BorderColor = Color.FromArgb("#E2E8F0");
    private static readonly Color TextPrimary = Color.FromArgb("#0F172A");
    private static readonly Color TextSecondary = Color.FromArgb("#64748B");
    private static readonly Color Primary = Color.FromArgb("#4F46E5");

    public static void Apply(ContentPage page)
    {
        page.BackgroundColor = Page;
        ApplyTo(page);
    }

    private static void ApplyTo(IVisualTreeElement element)
    {
        if (element is VisualElement visual)
        {
            visual.BackgroundColor = MapBackground(visual.BackgroundColor);
        }

        switch (element)
        {
            case Label label:
                label.TextColor = MapText(label.TextColor);
                break;
            case Button button:
                var buttonBackground = button.BackgroundColor;
                button.BackgroundColor = MapButton(buttonBackground);
                if (buttonBackground == Colors.Transparent)
                {
                    button.TextColor = Primary;
                }
                else if (button.TextColor == Colors.White || button.TextColor == Color.FromArgb("#FFFFFF")) button.TextColor = Colors.White;
                break;
            case Entry entry:
                entry.TextColor = MapText(entry.TextColor);
                entry.PlaceholderColor = MapText(entry.PlaceholderColor);
                break;
            case Editor editor:
                editor.TextColor = MapText(editor.TextColor);
                editor.PlaceholderColor = MapText(editor.PlaceholderColor);
                break;
            case Picker picker:
                picker.TextColor = MapText(picker.TextColor);
                picker.TitleColor = MapText(picker.TitleColor);
                break;
            case DatePicker datePicker:
                datePicker.TextColor = MapText(datePicker.TextColor);
                break;
            case ProgressBar progressBar:
                progressBar.ProgressColor = Primary;
                progressBar.BackgroundColor = BorderColor;
                break;
            case CheckBox checkBox:
                checkBox.Color = Primary;
                break;
            case Border border when border.Stroke is SolidColorBrush stroke:
                border.Stroke = new SolidColorBrush(MapStroke(stroke.Color));
                break;
        }

        foreach (var child in element.GetVisualChildren())
        {
            ApplyTo(child);
        }
    }

    private static Color MapBackground(Color? color)
    {
        if (color == null) return Page;
        if (color == Color.FromArgb("#F6F7FB") || color == Color.FromArgb("#F8FAFC")) return Page;
        if (color == Color.FromArgb("#24213A") || color == Color.FromArgb("#1F2430")) return TextPrimary;
        if (color == Color.FromArgb("#687083") || color == Color.FromArgb("#5F687B") || color == Color.FromArgb("#60697A")) return TextSecondary;
        if (color == Color.FromArgb("#E4E6EF") || color == Color.FromArgb("#E4DFFF")) return BorderColor;
        if (color == Color.FromArgb("#0F172A")) return Page;
        if (color == Color.FromArgb("#172033") || color == Color.FromArgb("#1E293B") || color == Color.FromArgb("#111827") || color == Color.FromArgb("#1F2937")) return Surface;
        if (color == Color.FromArgb("#334155")) return BorderColor;
        if (color == Color.FromArgb("#512BD4") || color == Color.FromArgb("#7C3AED") || color == Color.FromArgb("#6D4AFF") || color == Color.FromArgb("#4932A8") || color == Color.FromArgb("#4933B5")) return Primary;
        if (color == Color.FromArgb("#F0ECFF") || color == Color.FromArgb("#EFEAFF") || color == Color.FromArgb("#F4F1FF")) return Color.FromArgb("#EEF2FF");
        if (color == Color.FromArgb("#047857") || color == Color.FromArgb("#16A34A") || color == Color.FromArgb("#16875D")) return Color.FromArgb("#059669");
        if (color == Color.FromArgb("#B91C1C") || color == Color.FromArgb("#C53B4A")) return Color.FromArgb("#DC2626");
        if (color == Color.FromArgb("#B45309") || color == Color.FromArgb("#B7791F")) return Color.FromArgb("#D97706");
        if (color == Color.FromArgb("#1D4ED8") || color == Color.FromArgb("#5140B8")) return Primary;
        return color;
    }

    private static Color MapButton(Color? color)
    {
        if (color == null) return Color.FromArgb("#EFEAFF");
        if (color == Colors.Transparent) return Colors.Transparent;
        return MapBackground(color);
    }

    private static Color MapText(Color? color)
    {
        if (color == null) return TextPrimary;
        if (color == Colors.White || color == Color.FromArgb("#FFFFFF")) return TextPrimary;
        if (color == Color.FromArgb("#24213A") || color == Color.FromArgb("#1F2430")) return TextPrimary;
        if (color == Color.FromArgb("#687083") || color == Color.FromArgb("#5F687B") || color == Color.FromArgb("#60697A") || color == Color.FromArgb("#8B91A1")) return TextSecondary;
        if (color == Color.FromArgb("#6D4AFF") || color == Color.FromArgb("#4932A8") || color == Color.FromArgb("#4933B5") || color == Color.FromArgb("#5B43B5") || color == Color.FromArgb("#5140B8")) return Primary;
        if (color == Color.FromArgb("#16875D")) return Color.FromArgb("#059669");
        if (color == Color.FromArgb("#C53B4A") || color == Color.FromArgb("#B42336")) return Color.FromArgb("#DC2626");
        if (color == Color.FromArgb("#CBD5E1") || color == Color.FromArgb("#E2E8F0") || color == Color.FromArgb("#94A3B8")) return TextSecondary;
        if (color == Color.FromArgb("#C4B5FD") || color == Color.FromArgb("#A7F3D0") || color == Color.FromArgb("#67E8F9")) return Color.FromArgb("#5B43B5");
        if (color == Color.FromArgb("#FCA5A5") || color == Color.FromArgb("#FECACA")) return Color.FromArgb("#B42336");
        return color;
    }

    private static Color MapStroke(Color color)
    {
        if (color == Color.FromArgb("#334155") || color == Color.FromArgb("#22D3EE") || color == Color.FromArgb("#E4E6EF") || color == Color.FromArgb("#E4DFFF")) return BorderColor;
        return color;
    }
}
