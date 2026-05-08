using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace uroborus
{
    public class RenameDialog : Window
    {
        public string NewName { get; private set; } = "";
        public string NewImagePath { get; private set; } = "";

        private readonly TextBox _nameBox;
        private readonly Border _imgBorder;

        public RenameDialog(string currentName, string currentImagePath)
        {
            Title = "Плейлист"; Width = 340; Height = 185;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;

            UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            NewImagePath = currentImagePath;

            var root = new Border { Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)), CornerRadius = new CornerRadius(16), BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(1) };

            var stack = new StackPanel { Margin = new Thickness(20, 24, 20, 20) };

            var label = new TextBlock { Text = string.IsNullOrEmpty(currentName) ? "Создать плейлист" : "Настройки плейлиста", Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)), FontFamily = new FontFamily("Consolas"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };

            _imgBorder = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Выбрать обложку", ClipToBounds = true };

            bool hasCustomImg = !string.IsNullOrEmpty(currentImagePath) && System.IO.File.Exists(currentImagePath);

            if (hasCustomImg)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(currentImagePath); bmp.EndInit();
                _imgBorder.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
            }
            else
            {
                _imgBorder.Child = CreatePlusIcon();
            }

            _nameBox = new TextBox { Text = currentName, Foreground = Brushes.White, CaretBrush = new SolidColorBrush(Color.FromRgb(224, 17, 95)), BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(10, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center };
            var nameBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0), Height = 36 };
            nameBorder.Child = _nameBox;

            var inputGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_imgBorder, 0); Grid.SetColumn(nameBorder, 1);
            inputGrid.Children.Add(_imgBorder); inputGrid.Children.Add(nameBorder);

            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var removePhotoBtn = new Button { Content = "удалить фото", Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Focusable = false, FontFamily = new FontFamily("Consolas"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };

            var rmTemplate = new ControlTemplate(typeof(Button));
            var rmBd = new FrameworkElementFactory(typeof(Border));
            rmBd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var rmCp = new FrameworkElementFactory(typeof(ContentPresenter));
            rmCp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            rmBd.AppendChild(rmCp); rmTemplate.VisualTree = rmBd; removePhotoBtn.Template = rmTemplate;

            removePhotoBtn.MouseEnter += (s, e) => removePhotoBtn.Foreground = new SolidColorBrush(Color.FromRgb(224, 17, 95));
            removePhotoBtn.MouseLeave += (s, e) => removePhotoBtn.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));

            removePhotoBtn.Visibility = hasCustomImg ? Visibility.Visible : Visibility.Collapsed;

            removePhotoBtn.Click += (s, e) =>
            {
                NewImagePath = "";
                _imgBorder.Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
                _imgBorder.Child = CreatePlusIcon();
                removePhotoBtn.Visibility = Visibility.Collapsed;
            };

            _imgBorder.MouseLeftButtonDown += (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.webp" };
                if (dlg.ShowDialog() == true)
                {
                    NewImagePath = dlg.FileName;
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(NewImagePath); bmp.EndInit();
                    _imgBorder.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
                    _imgBorder.Child = null;
                    removePhotoBtn.Visibility = Visibility.Visible;
                }
            };

            Grid.SetColumn(removePhotoBtn, 0);
            bottomGrid.Children.Add(removePhotoBtn);

            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(rightPanel, 1);

            var cancel = MakeBtn("Отмена", false); var ok = MakeBtn("Сохранить", true);
            ok.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95)); ok.Foreground = Brushes.White;
            rightPanel.Children.Add(cancel); rightPanel.Children.Add(ok);
            bottomGrid.Children.Add(rightPanel);

            stack.Children.Add(label); stack.Children.Add(inputGrid); stack.Children.Add(bottomGrid);
            root.Child = stack; Content = root;

            _nameBox.KeyDown += (s, e) => { if (e.Key == Key.Return) Confirm(); if (e.Key == Key.Escape) CancelClose(); };
            _nameBox.SelectAll(); Loaded += (_, __) => _nameBox.Focus();
            root.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        }

        private UIElement CreatePlusIcon()
        {
            return new Path
            {
                Data = Geometry.Parse("M 11 0 L 13 0 L 13 11 L 24 11 L 24 13 L 13 13 L 13 24 L 11 24 L 11 13 L 0 13 L 0 11 L 11 11 Z"),
                Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Button MakeBtn(string text, bool isOk)
        {
            var b = new Button { Content = text, Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Focusable = false, Padding = new Thickness(14, 6, 14, 6), FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = isOk ? new Thickness(8, 0, 0, 0) : new Thickness(0) };
            var template = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); bd.SetValue(Border.PaddingProperty, new Thickness(14, 6, 14, 6));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp); template.VisualTree = bd; b.Template = template;

            b.MouseEnter += (s, e) => { if (!isOk) b.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)); else b.Background = new SolidColorBrush(Color.FromRgb(196, 16, 79)); };
            b.MouseLeave += (s, e) => { if (!isOk) b.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)); else b.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95)); };

            if (isOk) b.Click += (_, __) => Confirm(); else b.Click += (_, __) => CancelClose();
            return b;
        }

        private void Confirm() { NewName = _nameBox.Text; DialogResult = true; Close(); }
        private void CancelClose() { DialogResult = false; Close(); }
    }
}