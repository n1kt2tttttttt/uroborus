using System.Windows;
using System.Windows.Input;

namespace uroborus
{
    public partial class CustomMessageBox : Window
    {
        public bool ApplyToAll => ApplyToAllCheckBox.IsChecked == true;

        // Добавили параметр showCheckBox, который по умолчанию равен true
        public CustomMessageBox(string title, string message, bool showCheckBox = true)
        {
            InitializeComponent();
            TitleText.Text = title.ToUpper();
            MessageText.Text = message;

            // Если showCheckBox пришел как false — скрываем галочку
            ApplyToAllCheckBox.Visibility = showCheckBox ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void YesBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void NoBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}