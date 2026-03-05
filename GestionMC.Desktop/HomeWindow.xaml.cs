using System.Windows;

namespace GestionMC.Desktop
{
    public partial class HomeWindow : Window
    {
        public HomeWindow()
        {
            InitializeComponent();
        }

        private void BtnDescargar_Click(object sender, RoutedEventArgs e)
        {
            var win = new MainWindow
            {
                Owner = this
            };
            win.Show();
        }

        private void BtnColores_Click(object sender, RoutedEventArgs e)
        {
            var win = new ColoresWindow
            {
                Owner = this
            };
            win.Show();
        }

        private void BtnRetencionesClientes_Click(object sender, RoutedEventArgs e)
        {
            var win = new RetencionesClientesWindow
            {
                Owner = this
            };
            win.Show();
        }
    }
}
