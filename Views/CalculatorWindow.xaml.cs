using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace TurizmOperasyonSistemi.Views
{
    public partial class CalculatorWindow : Window
    {
        private double _memory = 0.0;
        private double _accumulator = 0.0;
        private string _pendingOp = null;
        private bool _newEntry = true;

        public CalculatorWindow()
        {
            InitializeComponent();
            Display.Text = "0";
            this.KeyDown += CalculatorWindow_KeyDown;
            // Use PreviewTextInput to capture actual characters according to keyboard layout (TR-Q etc.)
            this.PreviewTextInput += CalculatorWindow_PreviewTextInput;
        }

        private void CalculatorWindow_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            var ch = e.Text;
            switch (ch)
            {
                case "+":
                    Op_Click("+"); e.Handled = true; break;
                case "-":
                    Op_Click("-"); e.Handled = true; break;
                case "*":
                    Op_Click("*"); e.Handled = true; break;
                case "/":
                    Op_Click("/"); e.Handled = true; break;
                case "=":
                    Equals_Click(null, null); e.Handled = true; break;
                case ",":
                case ".":
                    Decimal_Click(null, null); e.Handled = true; break;
            }
        }

        private void CalculatorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                var digit = (e.Key - Key.D0).ToString();
                AppendDigit(digit);
                e.Handled = true;
                return;
            }
            if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                var digit = (e.Key - Key.NumPad0).ToString();
                AppendDigit(digit);
                e.Handled = true;
                return;
            }
            // Map numeric keypad and main keyboard operator keys. Support Shift+keys too.
            if (e.Key == Key.Add || (e.Key == Key.OemPlus && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) { Op_Click("+"); e.Handled = true; return; }
            if (e.Key == Key.Subtract || e.Key == Key.OemMinus) { Op_Click("-"); e.Handled = true; return; }
            if (e.Key == Key.Multiply || (e.Key == Key.D8 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) { Op_Click("*"); e.Handled = true; return; }
            if (e.Key == Key.Divide || e.Key == Key.Oem2 || e.Key == Key.OemQuestion || e.Key == Key.OemBackslash) { Op_Click("/"); e.Handled = true; return; }

            // Escape clears calculator (ready for new entry)
            if (e.Key == Key.Escape)
            {
                Clear_Click(null, null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Return) { Equals_Click(null, null); e.Handled = true; return; }
            if (e.Key == Key.Back) { Backspace_Click(null, null); e.Handled = true; return; }
            if (e.Key == Key.OemComma || e.Key == Key.Decimal) { Decimal_Click(null, null); e.Handled = true; return; }
        }

        private void AppendDigit(string d)
        {
            if (_newEntry || Display.Text == "0")
            {
                Display.Text = d;
                _newEntry = false;
            }
            else
            {
                Display.Text += d;
            }
        }

        private void Digit_Click(object sender, RoutedEventArgs e)
        {
            var content = (sender as System.Windows.Controls.Button).Content.ToString();
            AppendDigit(content);
        }

        private void Decimal_Click(object sender, RoutedEventArgs e)
        {
            if (_newEntry)
            {
                Display.Text = "0,";
                _newEntry = false;
            }
            else if (!Display.Text.Contains(","))
            {
                Display.Text += ",";
            }
        }

        private double ParseDisplay()
        {
            var s = (Display.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return 0.0;

            // keep only digits, signs and separators
            var cleaned = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-' ).ToArray());
            if (string.IsNullOrWhiteSpace(cleaned)) return 0.0;

            // If both dot and comma present, decide which is decimal separator by last occurrence
            char decimalSep = '.';
            if (cleaned.Contains(',') && cleaned.Contains('.'))
            {
                decimalSep = cleaned.LastIndexOf(',') > cleaned.LastIndexOf('.') ? ',' : '.';
            }
            else if (cleaned.Contains(','))
            {
                decimalSep = ',';
            }
            else
            {
                decimalSep = '.';
            }

            // remove thousands separators (the other char)
            var otherSep = decimalSep == ',' ? '.' : ',';
            var withoutThousands = cleaned.Replace(otherSep.ToString(), string.Empty);
            // normalize decimal separator to dot for invariant parse
            var normalized = withoutThousands.Replace(decimalSep.ToString(), ".");

            if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            // final fallbacks
            if (double.TryParse(cleaned, NumberStyles.Any, new CultureInfo("tr-TR"), out v)) return v;
            if (double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0.0;
        }

        private void Op_Click(object sender, RoutedEventArgs e)
        {
            string op;
            if (sender is System.Windows.Controls.Button btn) op = btn.Tag?.ToString() ?? btn.Content.ToString();
            else op = (string)sender;
            Op_Click(op);
        }

        private void Op_Click(string op)
        {
            try
            {
                var val = ParseDisplay();
                if (_pendingOp != null && !_newEntry)
                {
                    Compute(val);
                    Display.Text = _accumulator.ToString(CultureInfo.CurrentCulture);
                }
                else
                {
                    _accumulator = val;
                }
                _pendingOp = op;
                _newEntry = true;
            }
            catch { }
        }

        private void Compute(double val)
        {
            switch (_pendingOp)
            {
                case "+": _accumulator += val; break;
                case "-": _accumulator -= val; break;
                case "*": _accumulator *= val; break;
                case "/": if (val != 0) _accumulator /= val; else _accumulator = double.NaN; break;
            }
        }

        private void Equals_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var val = ParseDisplay();
                if (_pendingOp != null)
                {
                    Compute(val);
                    Display.Text = _accumulator.ToString(CultureInfo.CurrentCulture);
                    _pendingOp = null;
                    _newEntry = true;
                }
            }
            catch { }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            Display.Text = "0";
            _accumulator = 0.0;
            _pendingOp = null;
            _newEntry = true;
        }

        private void ClearEntry_Click(object sender, RoutedEventArgs e)
        {
            Display.Text = "0";
            _newEntry = true;
        }

        private void Backspace_Click(object sender, RoutedEventArgs e)
        {
            if (!_newEntry && Display.Text.Length > 0)
            {
                Display.Text = Display.Text.Substring(0, Display.Text.Length - 1);
                if (Display.Text.Length == 0) Display.Text = "0";
            }
        }

        private void Negate_Click(object sender, RoutedEventArgs e)
        {
            var v = ParseDisplay();
            v = -v;
            Display.Text = v.ToString(CultureInfo.CurrentCulture);
        }

        private void Percent_Click(object sender, RoutedEventArgs e)
        {
            var v = ParseDisplay();
            v = v / 100.0;
            Display.Text = v.ToString(CultureInfo.CurrentCulture);
        }

        private void Inverse_Click(object sender, RoutedEventArgs e)
        {
            var v = ParseDisplay();
            if (v != 0) Display.Text = (1.0 / v).ToString(CultureInfo.CurrentCulture);
        }

        private void Square_Click(object sender, RoutedEventArgs e)
        {
            var v = ParseDisplay();
            Display.Text = (v * v).ToString(CultureInfo.CurrentCulture);
        }

        private void Sqrt_Click(object sender, RoutedEventArgs e)
        {
            var v = ParseDisplay();
            if (v >= 0) Display.Text = Math.Sqrt(v).ToString(CultureInfo.CurrentCulture);
        }

        private void MemoryClear_Click(object sender, RoutedEventArgs e)
        {
            _memory = 0.0;
        }

        private void MemoryRecall_Click(object sender, RoutedEventArgs e)
        {
            Display.Text = _memory.ToString(CultureInfo.CurrentCulture);
            _newEntry = true;
        }

        private void MemoryAdd_Click(object sender, RoutedEventArgs e)
        {
            _memory += ParseDisplay();
        }

        private void MemorySub_Click(object sender, RoutedEventArgs e)
        {
            _memory -= ParseDisplay();
        }
    }
}
