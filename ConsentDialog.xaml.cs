using System.Windows;

namespace Sims3ModernPatcher
{
    public partial class ConsentDialog : Window
    {
        public ConsentDialog()
        {
            InitializeComponent();
        }

        private void Agree_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
