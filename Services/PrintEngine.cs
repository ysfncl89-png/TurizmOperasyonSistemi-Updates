using System;
using System.Collections.Generic;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace TurizmOperasyonSistemi.Services
{
    public static class PrintEngine
    {
        public static FixedDocument BuildFixedDocument(PrintQueueItem[] items, Views.PrintPreviewWindow.TemplateSpec template, bool landscape)
        {
            // Determine rows/cols based on template
            int cols = 3, rows = 5;
            switch (template)
            {
                case Views.PrintPreviewWindow.TemplateSpec.T3x4: cols = 3; rows = 4; break;
                case Views.PrintPreviewWindow.TemplateSpec.T3x5: cols = 3; rows = 5; break;
                case Views.PrintPreviewWindow.TemplateSpec.T2x6: cols = 2; rows = 6; break;
                case Views.PrintPreviewWindow.TemplateSpec.T2x7: cols = 2; rows = 7; break;
            }

            double mmToDip = 96.0 / 25.4; // WPF device-independent units per mm
            double pageWidth = 210 * mmToDip;
            double pageHeight = 297 * mmToDip;
            if (landscape) { var t = pageWidth; pageWidth = pageHeight; pageHeight = t; }

            var doc = new FixedDocument();
            int perPage = cols * rows;
            int total = items?.Length ?? 0;
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

                    var stack = new StackPanel { Margin = new Thickness(6) };
                    if (idx < total)
                    {
                        var it = items[idx];
                        stack.Children.Add(new TextBlock { Text = it.FullName ?? string.Empty, FontSize = 11, FontWeight = FontWeights.SemiBold });
                        stack.Children.Add(new TextBlock { Text = $"TC: {it.Tc}", FontSize = 10 });
                        stack.Children.Add(new TextBlock { Text = $"Tel: {it.Phone}", FontSize = 10 });
                        var addr = new TextBlock { Text = it.Address ?? string.Empty, FontSize = 9, TextWrapping = TextWrapping.Wrap };
                        stack.Children.Add(addr);
                    }
                    else
                    {
                        // empty slot
                        stack.Children.Add(new TextBlock { Text = string.Empty });
                    }

                    border.Child = stack;
                    fixedPage.Children.Add(border);
                }

                ((IAddChild)page).AddChild(fixedPage);
                doc.Pages.Add(page);
            }

            return doc;
        }
    }
}
