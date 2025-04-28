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

namespace WpfAppScraper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MongoService _mongoService;
        private List<GeneExpressionWithClinical> _geneExpressions;
        private Dictionary<string, List<string>> _cancerToPatientsMap;

        public SeriesCollection SeriesCollection { get; set; }
        public List<string> Labels { get; set; }

        public PlotModel HeatmapModel { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            var actionChoiceWindow = new ActionChoiceWindow();
            actionChoiceWindow.ShowDialog();

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

                Dispatcher.Invoke(() =>
                {
                    _cancerToPatientsMap = _geneExpressions
                        .GroupBy(g => g.CancerCohort)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(e => e.PatientId).Distinct().ToList()
                        );

                    CancerTypeDropdown.ItemsSource = _cancerToPatientsMap.Keys;
                    HeatmapCancerTypeDropdown.ItemsSource = _cancerToPatientsMap.Keys;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}");
            }
        }

        // Existing LiveCharts functionality
        private void CancerTypeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PatientDropdown.ItemsSource = null;
            var selectedCancerType = CancerTypeDropdown.SelectedItem as string;

            if (!string.IsNullOrEmpty(selectedCancerType) && _cancerToPatientsMap.ContainsKey(selectedCancerType))
            {
                PatientDropdown.ItemsSource = _cancerToPatientsMap[selectedCancerType];
            }
        }

        private void PatientDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedPatient = PatientDropdown.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedPatient))
            {
                UpdateChart(selectedPatient);
            }
        }

        private void UpdateChart(string patientId)
        {
            SeriesCollection.Clear();
            Labels.Clear();

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

        // New Heatmap functionality
        private void ShowHeatmap_Click(object sender, RoutedEventArgs e)
        {
            var selectedCancerType = HeatmapCancerTypeDropdown.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedCancerType) || !_cancerToPatientsMap.ContainsKey(selectedCancerType))
            {
                MessageBox.Show("Please select a cancer type.");
                return;
            }

            var patients = _cancerToPatientsMap[selectedCancerType];
            var genes = _geneExpressions.First().GeneValues.Keys.ToList();

            double[,] matrix = new double[genes.Count, patients.Count];
            for (int p = 0; p < patients.Count; p++)
            {
                var patientData = _geneExpressions.FirstOrDefault(g => g.PatientId == patients[p]);
                for (int g = 0; g < genes.Count; g++)
                {
                    matrix[g, p] = patientData?.GeneValues.ContainsKey(genes[g]) == true
                        ? patientData.GeneValues[genes[g]]
                        : 0;
                }
            }

            HeatmapModel = new PlotModel { Title = $"Gene Expression Heatmap - {selectedCancerType}" };

            // Add axes
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

            // Add heatmap series
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

            HeatmapModel.Series.Add(heatmapSeries);
            HeatmapPlot.Model = HeatmapModel;
        }
    }
}
