using Newtonsoft.Json.Linq;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WpfAppScraper.Services
{
    public class XenaDataService
    {
        private readonly HttpClient _client = new();
        private const string XENA_API_URL = "https://xenabrowser.net/datapages/?hub=https://tcga.xenahubs.net:443";

        public async Task<List<(string Url, string Cohort)>> GetDatasetUrlsAsync()
        {
            try
            {
                var response = await _client.GetStringAsync(XENA_API_URL);
                var datasets = JArray.Parse(response);

                return datasets
                    .Where(d => d["name"]?.ToString().Contains("IlluminaHiSeq pancan normalized") == true)
                    .Select(d => (
                        Url: d["url"]?.ToString(),
                        Cohort: d["name"]?.ToString().Split(' ').First()
                    ))
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching datasets: {ex.Message}");
                return new List<(string, string)>();
            }
        }

        public async Task<Stream> DownloadDatasetAsync(string url)
        {
            try
            {
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStreamAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading dataset: {ex.Message}");
                throw;
            }
        }
    }
}
