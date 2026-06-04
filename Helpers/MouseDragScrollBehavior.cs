using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TurizmOperasyonSistemi.Helpers
{
    // Attached behavior enabling mouse-drag panning on controls that host a ScrollViewer (e.g. DataGrid)
    public static class MouseDragScrollBehavior
    {
        public static readonly DependencyProperty EnableMouseDragScrollProperty = DependencyProperty.RegisterAttached(
            "EnableMouseDragScroll",
            typeof(bool),
            typeof(MouseDragScrollBehavior),
            new PropertyMetadata(false, OnEnableMouseDragScrollChanged));

        public static void SetEnableMouseDragScroll(DependencyObject element, bool value) => element.SetValue(EnableMouseDragScrollProperty, value);
        public static bool GetEnableMouseDragScroll(DependencyObject element) => (bool)element.GetValue(EnableMouseDragScrollProperty);

        private static void OnEnableMouseDragScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement fe)
            {
                fe.Loaded -= Fe_Loaded;
                if ((bool)e.NewValue)
                {
                    fe.Loaded += Fe_Loaded;
                }
                else
                {
                    // nothing to do at unload here
                }
            }
        }

        private static void Fe_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            var scroll = FindDescendantScrollViewer(fe);
            if (scroll == null) return;

            // attach handlers to the hosted element (not to scrollviewer) so DataGrid events are caught
            fe.PreviewMouseLeftButtonDown -= Fe_PreviewMouseLeftButtonDown;
            fe.PreviewMouseMove -= Fe_PreviewMouseMove;
            fe.PreviewMouseLeftButtonUp -= Fe_PreviewMouseLeftButtonUp;
            fe.MouseLeave -= Fe_MouseLeave;

            fe.PreviewMouseLeftButtonDown += Fe_PreviewMouseLeftButtonDown;
            fe.PreviewMouseMove += Fe_PreviewMouseMove;
            fe.PreviewMouseLeftButtonUp += Fe_PreviewMouseLeftButtonUp;
            fe.MouseLeave += Fe_MouseLeave;

            // store reference to scrollviewer
            fe.SetValue(ScrollViewerRefProperty, scroll);
        }

        private static ScrollViewer FindDescendantScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;
                var res = FindDescendantScrollViewer(child);
                if (res != null) return res;
            }
            return null;
        }

        // attached storage
        private static readonly DependencyProperty ScrollViewerRefProperty = DependencyProperty.RegisterAttached("ScrollViewerRef", typeof(ScrollViewer), typeof(MouseDragScrollBehavior));
        private static readonly DependencyProperty IsDraggingProperty = DependencyProperty.RegisterAttached("IsDragging", typeof(bool), typeof(MouseDragScrollBehavior), new PropertyMetadata(false));
        private static readonly DependencyProperty StartPointProperty = DependencyProperty.RegisterAttached("StartPoint", typeof(Point), typeof(MouseDragScrollBehavior));
        private static readonly DependencyProperty StartOffsetProperty = DependencyProperty.RegisterAttached("StartOffset", typeof(Point), typeof(MouseDragScrollBehavior));

        private static void Fe_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            var sv = fe.GetValue(ScrollViewerRefProperty) as ScrollViewer;
            if (sv == null) return;

            // record starting state
            fe.CaptureMouse();
            fe.SetValue(IsDraggingProperty, true);
            fe.SetValue(StartPointProperty, e.GetPosition(fe));
            fe.SetValue(StartOffsetProperty, new Point(sv.HorizontalOffset, sv.VerticalOffset));
            // change cursor to indicate panning
            try { Mouse.OverrideCursor = Cursors.SizeAll; } catch { }
        }

        private static void Fe_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            var isDragging = (bool)fe.GetValue(IsDraggingProperty);
            if (!isDragging) return;
            var sv = fe.GetValue(ScrollViewerRefProperty) as ScrollViewer;
            if (sv == null) return;

            var start = (Point)fe.GetValue(StartPointProperty);
            var startOffset = (Point)fe.GetValue(StartOffsetProperty);
            var pos = e.GetPosition(fe);
            var dx = pos.X - start.X;
            var dy = pos.Y - start.Y;

            // apply movement with inverted sign (drag left -> scroll right)
            var newH = startOffset.X - dx;
            var newV = startOffset.Y - dy;
            // clamp
            if (newH < 0) newH = 0;
            if (newV < 0) newV = 0;
            try { sv.ScrollToHorizontalOffset(newH); } catch { }
            try { sv.ScrollToVerticalOffset(newV); } catch { }
        }

        private static void Fe_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            if ((bool)fe.GetValue(IsDraggingProperty))
            {
                fe.SetValue(IsDraggingProperty, false);
                try { fe.ReleaseMouseCapture(); } catch { }
                try { Mouse.OverrideCursor = null; } catch { }
            }
        }

        private static void Fe_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            if ((bool)fe.GetValue(IsDraggingProperty))
            {
                fe.SetValue(IsDraggingProperty, false);
                try { fe.ReleaseMouseCapture(); } catch { }
                try { Mouse.OverrideCursor = null; } catch { }
            }
        }
    }
}
