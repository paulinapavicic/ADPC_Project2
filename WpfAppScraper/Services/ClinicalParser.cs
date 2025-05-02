using Minio.DataModel.Args;
using Minio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper.Configuration;
using CsvHelper;
using System.Globalization;
using System.Net.Http;
using WpfAppScraper.Models.Constraints;
using WpfAppScraper.Models;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium;
using Minio.ApiEndpoints;
using MongoDB.Driver;
using System.Reactive.Linq;
using WpfAppScraper.Helpers;

namespace WpfAppScraper.Services
{
    public  class ClinicalParser
    {
        private readonly string mainUrl = "https://xenabrowser.net/datapages/?hub=https://tcga.xenahubs.net:443";
        private readonly string downloadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "ClinicalFiles");
        private readonly IMinioClient _minioClient;
        private readonly string _bucketName = Constraints.ClinicalBucketName;


        public ClinicalParser()
        {
            _minioClient = new MinioClient()
                .WithEndpoint(Constraints.ENDPOINT)
                .WithCredentials(Constraints.ACCESS_KEY, Constraints.SECRET_KEY)
                .WithSSL(false)
                .Build();

            Directory.CreateDirectory(downloadDirectory);
        }

       


        public async Task ScrapeAndDownloadClinicalFilesAsync()
        {
            await ClearBucketAsync();

            using (IWebDriver driver = new ChromeDriver())
            {
                driver.Navigate().GoToUrl(mainUrl);
                await Task.Delay(5000);

                // Find all cohort links
                var cohortLinks = driver.FindElements(By.XPath("//a[contains(@href, 'cohort=TCGA')]"));
                var cohortUrls = cohortLinks.Select(el => el.GetAttribute("href")).ToList();

                foreach (var cohortUrl in cohortUrls)
                {
                    driver.Navigate().GoToUrl(cohortUrl);
                    await Task.Delay(3000);

                    // Find "Curated survival data" link
                    var survivalLinks = driver.FindElements(By.XPath("//a[contains(text(), 'Curated survival data')]"));
                    if (survivalLinks.Count == 0)
                    {
                        Console.WriteLine($"No curated survival data for cohort: {cohortUrl}");
                        continue;
                    }

                    // Click the first "Curated survival data" link
                    survivalLinks[0].Click();
                    await Task.Delay(3000);

                    // Now on the dataset details page, find the download link for the .txt file
                    var downloadLinks = driver.FindElements(By.XPath("//a[contains(@href, '.txt')]"));
                    if (downloadLinks.Count == 0)
                    {
                        Console.WriteLine("No .txt download link found on survival data page.");
                        driver.Navigate().Back();
                        await Task.Delay(2000);
                        continue;
                    }

                    string fileUrl = downloadLinks[0].GetAttribute("href");
                    string fileName = Path.GetFileName(fileUrl);
                    string filePath = Path.Combine(downloadDirectory, fileName);

                    using (HttpClient client = new HttpClient())
                    {
                        await DownloadFile(client, fileUrl, filePath);
                        await UploadFileToMinIO(filePath);
                    }
                    Console.WriteLine($"Downloaded and uploaded: {fileName}");

                    // Go back to the cohort page for the next iteration
                    driver.Navigate().Back();
                    await Task.Delay(2000);
                }
            }
        }


        // 2. Merge clinical data with gene expression data in MongoDB
        public async Task MergeClinicalWithGeneExpressionAsync(MongoService mongoService)
        {
            // List all clinical .txt files in MinIO
            var clinicalFileNames = await ListClinicalFilesInMinIO();

            // Parse and merge all clinical data
            var allClinicalDicts = new List<Dictionary<string, ClinicalSurvival>>();
            foreach (var fileName in clinicalFileNames)
            {
                var localPath = await DownloadClinicalFileFromMinIO(fileName);
                var dict = TsvParser.ParseClinicalData(localPath);
                allClinicalDicts.Add(dict);
            }
            var mergedClinicalDict = new Dictionary<string, ClinicalSurvival>();
            foreach (var dict in allClinicalDicts)
            {
                foreach (var kvp in dict)
                {
                    // Overwrite if duplicate, or keep first occurrence if you prefer
                    mergedClinicalDict[kvp.Key] = kvp.Value;
                }
            }


            // Get all gene expression docs from MongoDB
            var allGeneExpr = await mongoService.GetGeneExpressionsAsync();

            // Join and update
            foreach (var expr in allGeneExpr)
            {
                var barcode = expr.PatientId.Trim().ToUpper();
                if (mergedClinicalDict.TryGetValue(barcode, out var clinical))
                    expr.Clinical = clinical;
            }
            await mongoService.UpdateClinicalDataAsync(allGeneExpr);

            Console.WriteLine("Clinical data successfully merged and updated in MongoDB.");
        }


        private async Task DownloadFile(HttpClient client, string fileUrl, string filePath)
        {
            client.Timeout = TimeSpan.FromMinutes(15);
            using (var response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var fs = new FileStream(filePath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }
        }

        private async Task UploadFileToMinIO(string filePath)
        {
            string objectName = Path.GetFileName(filePath);
            await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucketName) // Use clinical bucket here!
                .WithObject(objectName)
                .WithFileName(filePath)
                .WithObjectSize(new FileInfo(filePath).Length)
                .WithContentType("text/tab-separated-values"));
        }

        private async Task ClearBucketAsync()
        {
            var fileUrls = new List<string>();
            await _minioClient.ListObjectsAsync(new ListObjectsArgs()
                .WithBucket(_bucketName)
                .WithRecursive(true))
                .ForEachAsync(item => fileUrls.Add(item.Key));

            foreach (var fileUrl in fileUrls)
            {
                await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(fileUrl));
            }
        }

        private async Task<List<string>> ListClinicalFilesInMinIO()
        {
            var fileNames = new List<string>();
            await _minioClient.ListObjectsAsync(new ListObjectsArgs()
                .WithBucket(_bucketName)
                .WithRecursive(true))
                .ForEachAsync(item => fileNames.Add(item.Key));
            return fileNames;
        }

        private async Task<string> DownloadClinicalFileFromMinIO(string fileName)
        {
            var localPath = Path.Combine(downloadDirectory, fileName);
            using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write);
            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCallbackStream(stream => stream.CopyTo(fs)));
            return localPath;
        }


    }
}
