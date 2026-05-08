using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace uroborus
{
    public class MoveToPositionDialog : Window
    {
        public int TargetPosition { get; private set; }

        private readonly TextBox _posBox;
        private readonly int _max;

        public MoveToPositionDialog(int currentPosition, int maxPosition)
        {
            _max = maxPosition;

            Width = 300;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Margin = new Thickness(20, 24, 20, 20) };

            var label = new TextBlock
            {
                Text = "ПЕРЕМЕСТИТЬ НА ПОЗИЦИЮ",
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var hint = new TextBlock
            {
                Text = $"Текущая: {currentPosition}  ·  Всего: {maxPosition}",
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 16)
            };

            _posBox = new TextBox
            {
                Text = currentPosition.ToString(),
                Foreground = Brushes.White,
                CaretBrush = new SolidColorBrush(Color.FromRgb(224, 17, 95)),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(10, 8, 10, 8),
                VerticalAlignment = VerticalAlignment.Center,
                SelectionBrush = new SolidColorBrush(Color.FromArgb(80, 224, 17, 95))
            };

            var inputBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 20),
                Height = 44
            };
            inputBorder.Child = _posBox;

            var bottomGrid = new Grid();
            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = MakeBtn("Отмена", false);
            var ok = MakeBtn("Переместить", true);
            ok.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95));
            ok.Foreground = Brushes.White;
            rightPanel.Children.Add(cancel);
            rightPanel.Children.Add(ok);
            bottomGrid.Children.Add(rightPanel);

            stack.Children.Add(label);
            stack.Children.Add(hint);
            stack.Children.Add(inputBorder);
            stack.Children.Add(bottomGrid);
            root.Child = stack;
            Content = root;

            _posBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Return) Confirm();
                if (e.Key == Key.Escape) CancelClose();
                // Разрешаем только цифры и управляющие клавиши
                bool isDigit = (e.Key >= Key.D0 && e.Key <= Key.D9) || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9);
                bool isControl = e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Home || e.Key == Key.End;
                if (!isDigit && !isControl) e.Handled = true;
            };

            _posBox.SelectAll();
            Loaded += (_, __) => _posBox.Focus();
            root.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        }

        private Button MakeBtn(string text, bool isOk)
        {
            var b = new Button
            {
                Content = text,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false,
                Padding = new Thickness(16, 8, 16, 8),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = isOk ? new Thickness(10, 0, 0, 0) : new Thickness(0)
            };
            var template = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            bd.SetValue(Border.PaddingProperty, new Thickness(16, 8, 16, 8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            template.VisualTree = bd;
            b.Template = template;

            b.MouseEnter += (s, e) => { if (!isOk) { b.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)); b.Foreground = Brushes.White; } else b.Background = new SolidColorBrush(Color.FromRgb(196, 16, 79)); };
            b.MouseLeave += (s, e) => { if (!isOk) { b.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)); b.Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)); } else b.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95)); };

            if (isOk) b.Click += (_, __) => Confirm();
            else b.Click += (_, __) => CancelClose();
            return b;
        }

        private void Confirm()
        {
            if (int.TryParse(_posBox.Text, out int pos) && pos >= 1 && pos <= _max)
            {
                TargetPosition = pos;
                DialogResult = true;
                Close();
            }
            else
            {
                _posBox.Foreground = new SolidColorBrush(Color.FromRgb(224, 17, 95));
                _posBox.Text = $"1–{_max}";
                _posBox.SelectAll();
            }
        }

        private void CancelClose() { DialogResult = false; Close(); }
    }
}
