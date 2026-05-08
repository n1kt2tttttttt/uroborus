using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace uroborus
{
    public class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string message)
        {
            Width = 320;
            SizeToContent = SizeToContent.Height; // АВТО-ВЫСОТА: Окно само растянется, кнопки больше не обрежутся!
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;

            UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);

            var root = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)), CornerRadius = new CornerRadius(16), BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(1) };
            var stack = new StackPanel { Margin = new Thickness(24, 24, 24, 24) };

            // Заголовок оставляем в стиле плеера (Consolas)
            var titleText = new TextBlock { Text = title.ToUpper(), Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), FontFamily = new FontFamily("Consolas"), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };

            // Текст меняем на красивый Segoe UI
            var messageText = new TextBlock { Text = message, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Margin = new Thickness(0, 0, 0, 24), TextWrapping = TextWrapping.Wrap };

            var bottomGrid = new Grid();
            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var cancel = MakeBtn("Нет", false);
            var ok = MakeBtn("Да", true);
            ok.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95)); ok.Foreground = Brushes.White;

            rightPanel.Children.Add(cancel); rightPanel.Children.Add(ok);
            bottomGrid.Children.Add(rightPanel);

            stack.Children.Add(titleText); stack.Children.Add(messageText); stack.Children.Add(bottomGrid);
            root.Child = stack; Content = root;

            root.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        }

        private Button MakeBtn(string text, bool isOk)
        {
            var b = new Button { Content = text, Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Focusable = false, Padding = new Thickness(20, 8, 20, 8), FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = isOk ? new Thickness(10, 0, 0, 0) : new Thickness(0) };
            var template = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); bd.SetValue(Border.PaddingProperty, new Thickness(20, 8, 20, 8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp); template.VisualTree = bd; b.Template = template;

            b.MouseEnter += (s, e) => { if (!isOk) { b.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)); b.Foreground = Brushes.White; } else { b.Background = new SolidColorBrush(Color.FromRgb(196, 16, 79)); } };
            b.MouseLeave += (s, e) => { if (!isOk) { b.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)); b.Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)); } else { b.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95)); } };

            if (isOk) b.Click += (_, __) => { DialogResult = true; Close(); };
            else b.Click += (_, __) => { DialogResult = false; Close(); };
            return b;
        }
    }
}