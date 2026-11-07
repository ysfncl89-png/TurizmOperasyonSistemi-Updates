using CommunityToolkit.Maui.Views;

namespace TOSLite.Mobile.Views;

internal enum ColumnDesignAction
{
    Cancel,
    Save,
    Apply
}

internal sealed class ColumnDesignResult
{
    public ColumnDesignAction Action { get; init; }
}

internal sealed class MobileColumnOption(string key, string title, int order, bool isVisible)
{
    public string Key { get; set; } = key;
    public string Title { get; set; } = title;
    public int Order { get; set; } = order;
    public bool IsVisible { get; set; } = isVisible;

    public MobileColumnOption Clone() => new(Key, Title, Order, IsVisible);
}

internal sealed class ColumnDesignPopup : Popup
{
    private readonly List<MobileColumnOption> _columns;
    private readonly bool _canEditTitles;
    private readonly bool _showFixedPassportRow;
    private readonly Func<string, Task<string?>> _editTitle;
    private readonly VerticalStackLayout _columnOptionsLayout = new() { Spacing = 5, Padding = new Thickness(0, 4) };
    private readonly Func<ColumnDesignAction, List<MobileColumnOption>, Task<bool>> _completed;

    public ColumnDesignPopup(
        IReadOnlyList<MobileColumnOption> columns,
        bool canEditTitles,
        bool showFixedPassportRow,
        Func<string, Task<string?>> editTitle,
        Func<ColumnDesignAction, List<MobileColumnOption>, Task<bool>> completed)
    {
        _columns = columns.Select(column => column.Clone()).ToList();
        _canEditTitles = canEditTitles;
        _showFixedPassportRow = showFixedPassportRow;
        _editTitle = editTitle;
        _completed = completed;
        CanBeDismissedByTappingOutsideOfPopup = true;
        var content = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 10, MinimumHeightRequest = 0 };
        content.Children.Add(new Label
        {
            Text = "Listeyi Tasarla",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimary"]
        });
        var instruction = new Label
        {
            Text = "Kolonları sıralayın; işaretini kaldırdığınız kolon dışa aktarılmaz.",
            FontSize = 13,
            TextColor = (Color)Application.Current!.Resources["TextSecondary"]
        };
        Grid.SetRow(instruction, 1);
        content.Children.Add(instruction);

        var scrollView = new ScrollView
        {
            Content = _columnOptionsLayout,
            VerticalOptions = LayoutOptions.Fill,
            VerticalScrollBarVisibility = ScrollBarVisibility.Always
        };
        Grid.SetRow(scrollView, 2);
        content.Children.Add(scrollView);

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) },
            ColumnSpacing = 8,
            Padding = new Thickness(0, 4, 0, 0)
        };
        var cancel = new Button { Text = "Vazgeç", BackgroundColor = Color.FromArgb("#E0E7FF"), TextColor = Color.FromArgb("#3730A3"), CornerRadius = 14, FontAttributes = FontAttributes.Bold, Padding = new Thickness(4, 8) };
        cancel.Clicked += (_, _) => Complete(ColumnDesignAction.Cancel);
        var save = new Button { Text = "Şablonu Kaydet", BackgroundColor = Color.FromArgb("#0B6B3A"), TextColor = Colors.White, CornerRadius = 14, FontAttributes = FontAttributes.Bold, FontSize = 12, Padding = new Thickness(4, 8) };
        save.Clicked += (_, _) => Complete(ColumnDesignAction.Save);
        var apply = new Button { Text = "Uygula", BackgroundColor = Color.FromArgb("#4F46E5"), TextColor = Colors.White, CornerRadius = 14, FontAttributes = FontAttributes.Bold, Padding = new Thickness(4, 8) };
        apply.Clicked += (_, _) => Complete(ColumnDesignAction.Apply);
        buttons.Children.Add(cancel);
        Grid.SetColumn(save, 1);
        buttons.Children.Add(save);
        Grid.SetColumn(apply, 2);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 3);
        content.Children.Add(buttons);

        var card = new Border
        {
            Content = content,
            BackgroundColor = (Color)Application.Current!.Resources["PageBackground"],
            Stroke = Color.FromArgb("#F59E0B"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding = 14,
            Margin = new Thickness(20, 0),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            HeightRequest = 560
        };
        Content = card;
        BuildRows();
    }

    private void BuildRows()
    {
        _columnOptionsLayout.Children.Clear();
        foreach (var column in _columns)
        {
            var checkBox = new CheckBox { IsChecked = column.IsVisible, Color = Color.FromArgb("#4F46E5"), VerticalOptions = LayoutOptions.Center };
            checkBox.CheckedChanged += (_, args) => column.IsVisible = args.Value;
            var title = new Label
            {
                Text = column.Title,
                TextColor = (Color)Application.Current!.Resources["TextPrimary"],
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            var order = new Label { Text = column.Order.ToString(), TextColor = (Color)Application.Current!.Resources["TextSecondary"], VerticalOptions = LayoutOptions.Center };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
                ColumnSpacing = 6,
                Padding = new Thickness(2, 4)
            };
            Grid.SetColumn(title, 1);
            Grid.SetColumn(order, 2);
            row.Children.Add(checkBox);
            row.Children.Add(title);
            row.Children.Add(order);

            var moveButtons = new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
            var up = new Button { Text = "↑", Padding = new Thickness(3, 0), FontSize = 12, HeightRequest = 36, MinimumWidthRequest = 28, MaximumWidthRequest = 32 };
            up.Clicked += (_, _) => MoveColumn(column, -1);
            var down = new Button { Text = "↓", Padding = new Thickness(3, 0), FontSize = 12, HeightRequest = 36, MinimumWidthRequest = 28, MaximumWidthRequest = 32 };
            down.Clicked += (_, _) => MoveColumn(column, 1);
            moveButtons.Children.Add(up);
            moveButtons.Children.Add(down);
            Grid.SetColumn(moveButtons, 3);
            row.Children.Add(moveButtons);

            if (_canEditTitles)
            {
                var edit = new Button
                {
                    Text = "Düzenle",
                    Padding = new Thickness(5, 2),
                    FontSize = 11,
                    HeightRequest = 38,
                    MinimumWidthRequest = 56,
                    MaximumWidthRequest = 60
                };
                edit.Clicked += async (_, _) =>
                {
                    var edited = await _editTitle(column.Title);
                    if (string.IsNullOrWhiteSpace(edited)) return;
                    column.Title = edited.Trim();
                    title.Text = column.Title;
                };
                Grid.SetColumn(edit, 4);
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                row.Children.Add(edit);
            }
            _columnOptionsLayout.Children.Add(row);
        }

        if (_showFixedPassportRow)
        {
            var fixedRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
                ColumnSpacing = 8,
                Padding = new Thickness(2, 4)
            };
            fixedRow.Children.Add(new CheckBox { IsChecked = false, IsEnabled = false, VerticalOptions = LayoutOptions.Center });
            var fixedLabel = new Label { Text = "Pasaportu Geldi mi ?", TextColor = (Color)Application.Current!.Resources["TextPrimary"], VerticalOptions = LayoutOptions.Center };
            Grid.SetColumn(fixedLabel, 1);
            fixedRow.Children.Add(fixedLabel);
            var order = new Label { Text = "--", TextColor = (Color)Application.Current!.Resources["TextSecondary"], VerticalOptions = LayoutOptions.Center };
            Grid.SetColumn(order, 2);
            fixedRow.Children.Add(order);
            _columnOptionsLayout.Children.Add(fixedRow);
        }
    }

    private void MoveColumn(MobileColumnOption column, int offset)
    {
        var index = _columns.IndexOf(column);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _columns.Count) return;
        _columns.RemoveAt(index);
        _columns.Insert(target, column);
        for (var i = 0; i < _columns.Count; i++) _columns[i].Order = i + 1;
        BuildRows();
    }

    private async void Complete(ColumnDesignAction action)
    {
        if (await _completed(action, _columns)) await CloseAsync();
    }
}
