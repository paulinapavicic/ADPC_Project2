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
using WpfAppScraper.Services;

namespace WpfAppScraper
{
    /// <summary>
    /// Interaction logic for FileChoiceWindow.xaml
    /// </summary>
    public partial class FileChoiceWindow : Window
    {
        public FileChoiceWindow()
        {
            InitializeComponent();
        }
        private async void OnSkipScrapingClick(object sender, RoutedEventArgs e)
        {
            var cohortDataService = new CohortDataService();
            await cohortDataService.ProcessFilesFromMinIO();
            this.Close();
        }

        private async void OnScrapeAndLoadClick(object sender, RoutedEventArgs e)
        {
            var cohortDataService = new CohortDataService();
            await cohortDataService.ScrapeAndDownloadFilesAsync();
            await cohortDataService.ProcessFilesFromMinIO();
            this.Close();
        }
    }
}
