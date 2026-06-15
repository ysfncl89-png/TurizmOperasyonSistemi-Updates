using System;
using System.Collections.Generic;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using TurizmOperasyonSistemi.Views;

namespace TurizmOperasyonSistemi.Services
{
    public static class PrintEngine
    {
        public static FixedDocument BuildFixedDocument(PrintQueueItem[] items, int cols, int rows, bool landscape, SenderInfo sender = null)
        {
            // cols and rows specify template grid (e.g., cols=3, rows=5)
            if (cols <= 0) cols = 3;
            if (rows <= 0) rows = 5;

            double mmToDip = 96.0 / 25.4; // WPF device-independent units per mm
            double pageWidth = 210 * mmToDip;
            double pageHeight = 297 * mmToDip;
            if (landscape) { var t = pageWidth; pageWidth = pageHeight; pageHeight = t; }

            var doc = new FixedDocument();
            int perPage = cols * rows;

            // Group items by TourType+Period to avoid mixing different tours/periods across page A4 boundaries
            var groups = new Dictionary<string, List<PrintQueueItem>>();
            if (items != null)
            {
                foreach (var it in items)
                {
                    var key = (it.TourType ?? string.Empty) + "|" + (it.Period ?? string.Empty);
                    if (!groups.ContainsKey(key)) groups[key] = new List<PrintQueueItem>();
                    groups[key].Add(it);
                }
            }

            // For each group, allocate pages sequentially. This ensures items from different groups are not interleaved.
            foreach (var grp in groups)
            {
                var grpItems = grp.Value;
                int total = grpItems.Count;
                int pageCount = Math.Max(1, (int)Math.Ceiling((double)total / perPage));

                for (int p = 0; p < pageCount; p++)
                {
                    var page = new PageContent();
                    var fixedPage = new FixedPage();
                    fixedPage.Width = pageWidth;
                    fixedPage.Height = pageHeight;

                    // margins
                    double marginMm = 8; // 8mm
                    double margin = marginMm * mmToDip;
                    double usableW = pageWidth - margin * 2;
                    double usableH = pageHeight - margin * 2;

                    double cellW = usableW / cols;
                    double cellH = usableH / rows;

                    for (int i = 0; i < perPage; i++)
                    {
                        int idx = p * perPage + i;
                        int r = i / cols;
                        int c = i % cols;
                        double x = margin + c * cellW;
                        double y = margin + r * cellH;

                        var border = new Border
                        {
                            Width = cellW - 6,
                            Height = cellH - 6,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                            BorderThickness = new Thickness(1),
                            Background = Brushes.White
                        };

                        FixedPage.SetLeft(border, x + 3);
                        FixedPage.SetTop(border, y + 3);

                        // compute scale factor relative to a base template (3x5) so fonts scale logically
                        var baseCols = 3; var baseRows = 5;
                        double baseCellW = usableW / baseCols;
                        double baseCellH = usableH / baseRows;
                        double scale = 1.0;
                        try
                        {
                            scale = Math.Min(cellW / baseCellW, cellH / baseCellH);
                            // clamp scale to avoid unreadably small/large text
                            scale = Math.Max(0.5, Math.Min(1.6, scale));
                        }
                        catch { scale = 1.0; }

                        var stack = new StackPanel { Margin = new Thickness(6 * scale) };
                        if (idx < total)
                        {
                            var it = grpItems[idx];
                        // Render sender info on top if provided with a heading
                        if (sender != null)
                        {
                            stack.Children.Add(new TextBlock { Text = "GÖNDERİCİ", FontSize = 9 * scale, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,2 * scale) });
                            stack.Children.Add(new TextBlock { Text = sender.Name ?? string.Empty, FontSize = 10 * scale, FontWeight = FontWeights.Bold });
                            stack.Children.Add(new TextBlock { Text = sender.Phone ?? string.Empty, FontSize = 9 * scale });
                            var saddr = new TextBlock { Text = sender.Address ?? string.Empty, FontSize = 8 * scale, TextWrapping = TextWrapping.Wrap };
                            stack.Children.Add(saddr);
                            stack.Children.Add(new Separator { Margin = new Thickness(0,4 * scale,0,4 * scale) });
                            // recipient heading after sender
                            stack.Children.Add(new TextBlock { Text = "ALICI", FontSize = 9 * scale, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,2 * scale) });
                        }
                        else
                        {
                            // If no sender, still show recipient heading
                            stack.Children.Add(new TextBlock { Text = "ALICI", FontSize = 9 * scale, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,2 * scale) });
                        }

                        stack.Children.Add(new TextBlock { Text = it.FullName ?? string.Empty, FontSize = 11 * scale, FontWeight = FontWeights.SemiBold });
                        stack.Children.Add(new TextBlock { Text = $"TC: {it.Tc}", FontSize = 10 * scale });
                        stack.Children.Add(new TextBlock { Text = $"Tel: {it.Phone}", FontSize = 10 * scale });
                        var addr = new TextBlock { Text = it.Address ?? string.Empty, FontSize = 9 * scale, TextWrapping = TextWrapping.Wrap };
                        stack.Children.Add(addr);
                    }
                    else
                    {
                        // empty slot
                        stack.Children.Add(new TextBlock { Text = string.Empty });
                    }
                    // Assign stack directly; font sizes adjusted by scale
                    border.Child = stack;
                    fixedPage.Children.Add(border);
                }

                ((IAddChild)page).AddChild(fixedPage);
                doc.Pages.Add(page);
                }
            }

            return doc;
        }
    }
}
