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
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfAppScraper.Models;
using WpfAppScraper.Services;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LiveCharts;
using LiveCharts.Wpf;
using OxyPlot;
using LiveCharts.Wpf.Charts.Base;
using OxyPlot.Axes;
using OxyPlot.Series;
using AxisPosition = OxyPlot.Axes.AxisPosition;
using Microsoft.Win32;
using WpfAppScraper.Helpers;
using WpfAppScraper.Models.Constraints;

namespace WpfAppScraper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MongoService _mongoService;
        private List<GeneExpression> _geneExpressions;
        private Dictionary<string, List<string>> _cancerToPatientsMap;

        public SeriesCollection SeriesCollection { get; set; }
        public List<string> Labels { get; set; }
        public PlotModel HeatmapModel { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            // Hide MainWindow until StartupChoiceWindow is done
            this.Visibility = Visibility.Hidden;

            var startupWindow = new StartupChoiceWindow
            {
                Owner = this, // Optional: centers dialog over MainWindow
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var result = startupWindow.ShowDialog();

            if (result != true)
            {
                // User cancelled or closed the dialog, exit app
                Application.Current.Shutdown();
                return;
            }

            this.Visibility = Visibility.Visible;

            _mongoService = new MongoService();
            _cancerToPatientsMap = new Dictionary<string, List<string>>();
            SeriesCollection = new SeriesCollection();
            HeatmapModel = new PlotModel { Title = "Gene Expression Heatmap" };

            DataContext = this;
            LoadData();
        }

        private async void LoadData()
        {
            try
            {
                _geneExpressions = await _mongoService.GetGeneExpressionsAsync();

                // Group patients by cancer cohort
                _cancerToPatientsMap = _geneExpressions
                    .GroupBy(g => g.CancerCohort)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.PatientId).Distinct().ToList()
                    );

                // Populate dropdowns
                CancerTypeDropdown.ItemsSource = _cancerToPatientsMap.Keys.ToList();
                HeatmapCancerTypeDropdown.ItemsSource = _cancerToPatientsMap.Keys.ToList();

                // Optionally select the first cohort
                if (CancerTypeDropdown.Items.Count > 0)
                    CancerTypeDropdown.SelectedIndex = 0;
                if (HeatmapCancerTypeDropdown.Items.Count > 0)
                    HeatmapCancerTypeDropdown.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}");
            }
        }

        private void CancerTypeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PatientDropdown.ItemsSource = null;
            var selectedCancerType = CancerTypeDropdown.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedCancerType) && _cancerToPatientsMap.ContainsKey(selectedCancerType))
            {
                PatientDropdown.ItemsSource = _cancerToPatientsMap[selectedCancerType];
                if (PatientDropdown.Items.Count > 0)
                    PatientDropdown.SelectedIndex = 0;
            }
        }

        private void PatientDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedPatient = PatientDropdown.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedPatient))
            {
                UpdateChart(selectedPatient);
                UpdateClinicalInfo(selectedPatient);
            }
        }

        private void UpdateChart(string patientId)
        {
            SeriesCollection.Clear();

            var patientData = _geneExpressions.FirstOrDefault(g => g.PatientId == patientId);
            if (patientData == null) return;

            Labels = patientData.GeneValues.Keys.ToList();
            SeriesCollection.Add(new ColumnSeries
            {
                Title = "Expression Level",
                Values = new ChartValues<double>(patientData.GeneValues.Values)
            });

            chart.AxisX[0].Labels = Labels;
            chart.Series = SeriesCollection;
        }

        private void UpdateClinicalInfo(string patientId)
        {
            var patient = _geneExpressions.FirstOrDefault(g => g.PatientId == patientId);
            if (patient == null) return;

            txtCohort.Text = patient.CancerCohort;
            txtStage.Text = patient.Clinical?.ClinicalStage ?? "Unknown";
            txtDSS.Text = patient.Clinical?.DiseaseSpecificSurvival.HasValue == true
                ? (patient.Clinical.DiseaseSpecificSurvival.Value == 1 ? "Survived" : "Did not survive")
                : "Unknown";
            txtOS.Text = patient.Clinical?.OverallSurvival.HasValue == true
                ? (patient.Clinical.OverallSurvival.Value == 1 ? "Survived" : "Did not survive")
                : "Unknown";
        }
        private async void btnDownloadData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetUIState(false);
                var xenaService = new XenaDataService();
                var minioService = new MinioService(Constraints.ENDPOINT,
                                                  Constraints.ACCESS_KEY,
                                                  Constraints.SECRET_KEY,
                                                  Constraints.BucketName);

                // Your download and processing logic here
                // Example:
                var datasets = await xenaService.GetDatasetUrlsAsync();
                progressBar.Maximum = datasets.Count;

                foreach (var (url, cohort) in datasets)
                {
                    using var stream = await xenaService.DownloadDatasetAsync(url);
                    await minioService.UploadFileAsync($"{cohort}.tsv.gz", stream);
                    progressBar.Value++;
                    txtLog.AppendText($"Processed {cohort}\n");
                }
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

        private async void btnImportClinical_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "TSV Files (*.tsv)|*.tsv",
                Title = "Select Clinical Survival Data File"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    SetUIState(false);
                    txtLog.AppendText("Starting clinical data import...\n");

                    // 1. Parse clinical data
                    var clinicalData = TsvParser.ParseClinicalData(openDialog.FileName);
                    txtLog.AppendText($"Parsed {clinicalData.Count} clinical records\n");

                    // 2. Merge with existing gene expressions
                    int matchedRecords = 0;
                    foreach (var expression in _geneExpressions)
                    {
                        // Extract base patient ID (TCGA-XX-XXXX)
                        var basePatientId = string.Join("-", expression.PatientId.Split('-').Take(3));

                        if (clinicalData.TryGetValue(basePatientId, out var clinical))
                        {
                            expression.Clinical = clinical;
                            matchedRecords++;
                        }
                    }
                    txtLog.AppendText($"Matched clinical data for {matchedRecords} patients\n");

                    // 3. Update MongoDB
                    await _mongoService.UpdateClinicalDataAsync(_geneExpressions);
                    txtLog.AppendText("Successfully updated clinical data in database\n");

                    // 4. Refresh UI
                    if (PatientDropdown.SelectedItem != null)
                    {
                        UpdateClinicalInfo(PatientDropdown.SelectedItem.ToString());
                    }
                }
                catch (Exception ex)
                {
                    txtLog.AppendText($"Error importing clinical data: {ex.Message}\n");
                }
                finally
                {
                    SetUIState(true);
                }
            }
        }

        private void SetUIState(bool enabled)
        {
            btnDownloadData.IsEnabled = enabled;
            btnImportClinical.IsEnabled = enabled;
            progressBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }


        private void ShowHeatmap_Click(object sender, RoutedEventArgs e)
        {
            var selectedCancerType = HeatmapCancerTypeDropdown.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedCancerType) || !_cancerToPatientsMap.ContainsKey(selectedCancerType))
            {
                MessageBox.Show("Please select a cancer type.");
                return;
            }

            var patients = _cancerToPatientsMap[selectedCancerType];
            if (patients.Count == 0) return;

            var genes = _geneExpressions.First().GeneValues.Keys.ToList();
            double[,] matrix = new double[genes.Count, patients.Count];

            for (int g = 0; g < genes.Count; g++)
            {
                for (int p = 0; p < patients.Count; p++)
                {
                    var patientData = _geneExpressions.FirstOrDefault(ge => ge.PatientId == patients[p]);
                    matrix[g, p] = patientData?.GeneValues.ContainsKey(genes[g]) == true
                        ? patientData.GeneValues[genes[g]]
                        : 0;
                }
            }

            HeatmapModel = new PlotModel { Title = $"Gene Expression Heatmap - {selectedCancerType}" };
            HeatmapModel.Axes.Add(new CategoryAxis
            {
                Position = AxisPosition.Left,
                ItemsSource = genes,
                Key = "Genes"
            });
            HeatmapModel.Axes.Add(new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = patients,
                Key = "Patients",
                LabelField = "PatientId",
                Angle = -45,
                IsZoomEnabled = false
            });
            var heatmapSeries = new HeatMapSeries
            {
                X0 = 0,
                X1 = patients.Count - 1,
                Y0 = 0,
                Y1 = genes.Count - 1,
                Data = matrix,
                Interpolate = false,
                RenderMethod = HeatMapRenderMethod.Rectangles
            };
            HeatmapModel.Series.Clear();
            HeatmapModel.Series.Add(heatmapSeries);
            HeatmapPlot.Model = HeatmapModel;


        }
    }
}
