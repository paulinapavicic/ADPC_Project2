using Microsoft.Win32;
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
using WpfAppScraper.Helpers;
using WpfAppScraper.Services;

namespace WpfAppScraper
{
    /// <summary>
    /// Interaction logic for StartupChoiceWindow.xaml
    /// </summary>
    public partial class StartupChoiceWindow : Window
    {

        private readonly XenaDataService _xenaService = new();
        private readonly MinioService _minioService = new("localhost:9000", "admin", "admin123", "scraper");
        private readonly MongoService _mongoService = new();

        public StartupChoiceWindow()
        {
            InitializeComponent();
        }

        private async void OnDownloadClick(object sender, RoutedEventArgs e)
        {
            SetUIState(false);
            txtLog.Text = "Fetching datasets from Xena...\n";
            try
            {
                var datasets = await _xenaService.GetDatasetUrlsAsync();
                progressBar.Visibility = Visibility.Visible;
                progressBar.Maximum = datasets.Count;
                progressBar.Value = 0;

                foreach (var (url, cohort) in datasets)
                {
                    txtLog.AppendText($"Downloading {cohort}...\n");
                    using var stream = await _xenaService.DownloadDatasetAsync(url);
                    txtLog.AppendText($"Uploading {cohort} to MinIO...\n");
                    await _minioService.UploadFileAsync($"{cohort}.tsv.gz", stream);

                    // Parse and save to MongoDB
                    stream.Position = 0;
                    var expressions = TsvParser.ParseGeneExpressions(stream, cohort);
                    await _mongoService.InsertGeneExpressionsAsync(expressions);

                    progressBar.Value++;
                }

                txtLog.AppendText("Download and processing complete.\n");
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                txtLog.AppendText($"Error: {ex.Message}\n");
            }
            finally
            {
                SetUIState(true);
            }
        }

        private void OnUseExistingClick(object sender, RoutedEventArgs e)
        {
            // No processing, just close and continue to main window
            this.DialogResult = true;
            this.Close();
        }

        private async void OnImportClinicalClick(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "TSV Files (*.tsv)|*.tsv",
                Title = "Select Clinical Survival Data File"
            };

            if (openDialog.ShowDialog() == true)
            {
                SetUIState(false);
                txtLog.Text = "Importing clinical data...\n";
                try
                {
                    var clinicalData = TsvParser.ParseClinicalData(openDialog.FileName);
                    var expressions = await _mongoService.GetGeneExpressionsAsync();

                    int matchCount = 0;
                    foreach (var expr in expressions)
                    {
                        var basePatientId = string.Join("-", expr.PatientId.Split('-').Take(3));
                        if (clinicalData.TryGetValue(basePatientId, out var clinical))
                        {
                            expr.Clinical = clinical;
                            matchCount++;
                        }
                    }
                    await _mongoService.UpdateClinicalDataAsync(expressions);
                    txtLog.AppendText($"Merged clinical data for {matchCount} patients.\n");
                    this.DialogResult = true;
                    this.Close();
                }
                catch (Exception ex)
                {
                    txtLog.AppendText($"Error: {ex.Message}\n");
                }
                finally
                {
                    SetUIState(true);
                }
            }
        }

        private void SetUIState(bool enabled)
        {
            progressBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
