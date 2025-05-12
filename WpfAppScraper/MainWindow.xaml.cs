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
using MongoDB.Bson;

namespace WpfAppScraper
{
    
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

                
                _cancerToPatientsMap = _geneExpressions
                    .GroupBy(g => g.CancerCohort)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.PatientId).Distinct().ToList()
                    );

                
                CancerTypeDropdown.ItemsSource = _cancerToPatientsMap.Keys.ToList();
                HeatmapCancerTypeDropdown.ItemsSource = _cancerToPatientsMap.Keys.ToList();

                
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

            if (patient.Clinical== null)
            {
                txtStage.Text = "Unknown";
                txtDSS.Text = "Unknown";
                txtOS.Text = "Unknown";
                Console.WriteLine($"No clinical data for {patient.PatientId}");
            }
            else
            {
                txtStage.Text = string.IsNullOrWhiteSpace(patient.Clinical.ClinicalStage)
                    ? "Unknown"
                    : patient.Clinical.ClinicalStage;

                txtDSS.Text = patient.Clinical.DiseaseSpecificSurvival switch
                {
                    1 => "Survived",
                    0 => "Did not survive",
                    _ => "Unknown"
                };

                txtOS.Text = patient.Clinical.OverallSurvival switch
                {
                    1 => "Survived",
                    0 => "Did not survive",
                    _ => "Unknown"
                };

                Console.WriteLine($"Clinical for {patient.PatientId}: DSS={patient.Clinical.DiseaseSpecificSurvival}, OS={patient.Clinical.OverallSurvival}, Stage={patient.Clinical.ClinicalStage}");
            }

        }
        private async void btnDownloadData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetUIState(false);
                txtLog.AppendText("Starting scraping and download...\n");

                var xenaService = new XenaDataService();

                
                await xenaService.ScrapeAndDownloadFilesAsync();
                txtLog.AppendText("Files downloaded and uploaded to MinIO.\n");

                
                await xenaService.ProcessFilesFromMinIO();
                txtLog.AppendText("Files processed and loaded into MongoDB.\n");

                
                tabHeatmap.IsEnabled = true;
                tabPatient.IsEnabled = true;
                txtLog.AppendText("Data loaded. Visualization tabs are now enabled.\n");
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
                Filter = "Tab-Delimited Files (*.tsv;*.txt)|*.tsv;*.txt",
                Title = "Select Clinical Survival Data File"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    SetUIState(false);
                    txtLog.AppendText("Starting clinical data import...\n");

                    
                    var clinicalData = TsvParser.ParseClinicalData(openDialog.FileName);
                    txtLog.AppendText($"Parsed {clinicalData.Count} clinical records\n");

                    
                    int matchedRecords = 0;
                    foreach (var expression in _geneExpressions)
                    {
                       
                        
                        var basePatientId = expression.PatientId.Trim().ToUpper();


                        if (clinicalData.TryGetValue(basePatientId, out var clinical))
                        {
                            expression.Clinical = clinical;
                            matchedRecords++;
                        }
                        else
                        {
                            Console.WriteLine($"No clinical match for {basePatientId}");
                        }
                    }
                    txtLog.AppendText($"Matched clinical data for {matchedRecords} patients\n");

                 
                    await _mongoService.UpdateClinicalDataAsync(_geneExpressions);
                    txtLog.AppendText("Successfully updated clinical data in database\n");

                    
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
            // Limit the number of patients to display for readability
            int maxPatients = 50;

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
    Angle = -45,
    IsZoomEnabled = true,
      // Show only every 5th patient label for clarity
        GapWidth = 0,
    MajorStep = 5
});

HeatmapModel.Axes.Add(new LinearColorAxis
{
    Position = AxisPosition.Right,
    Palette = OxyPalettes.Jet(200), 
    Title = "Expression"
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

        //scraping clinical data
        private async void btnClinicalData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetUIState(false);
                txtLog.AppendText("Starting clinical data scraping and download...\n");

                var clinicalParser = new ClinicalParser();

                // Scrape Xena, download clinical files, and upload to MinIO clinical bucket
                await clinicalParser.ScrapeAndDownloadClinicalFilesAsync();
                txtLog.AppendText("Clinical files downloaded and uploaded to MinIO clinical bucket.\n");

                

                txtLog.AppendText("Clinical data scraping complete.\n");
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


        
        private async void btnMergeClinical_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetUIState(false);
                txtLog.AppendText("Starting clinical data merge...\n");

                var clinicalParser = new ClinicalParser();
                await clinicalParser.MergeClinicalWithGeneExpressionAsync(_mongoService);

                txtLog.AppendText("Clinical data merged with gene expression data in MongoDB.\n");
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
}
