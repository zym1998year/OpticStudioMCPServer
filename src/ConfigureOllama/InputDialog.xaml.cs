using System.Windows;

namespace ConfigureOllama
{
    public partial class InputDialog : Window
    {
        public string ResponseText => txtInput.Text;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            txtPrompt.Text = prompt;
            txtInput.Text = defaultValue;
            txtInput.SelectAll();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
