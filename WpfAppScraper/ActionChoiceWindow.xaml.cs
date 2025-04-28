using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfAppScraper
{
    /// <summary>
    /// Interaction logic for ActionChoiceWindow.xaml
    /// </summary>
    public partial class ActionChoiceWindow : Window
    {
        public ActionChoiceWindow()
        {
            InitializeComponent();
        }

        private void OnParseDataClick(object sender, RoutedEventArgs e)
        {
            this.Close();

            var fileChoiceWindow = new FileChoiceWindow();
            fileChoiceWindow.ShowDialog();
        }

        private void OnUseExistingDataClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
