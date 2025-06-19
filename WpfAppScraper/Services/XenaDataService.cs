using CsvHelper.Configuration;
using CsvHelper;
using Minio.DataModel.Args;
using Minio;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WpfAppScraper.Models.Constraints;
using WpfAppScraper.Models;
using System.IO.Compression;
using Minio.ApiEndpoints;
using System.Reactive.Linq;

namespace WpfAppScraper.Services
{
    public class XenaDataService
    {
        private readonly string mainUrl = "https://xenabrowser.net/datapages/?hub=https://tcga.xenahubs.net:443";
        private readonly string downloadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DownloadedFiles");
        private readonly IMinioClient _minioClient;
        private readonly IMongoClient _mongoClient;

        public XenaDataService()
        {
            _minioClient = new MinioClient()
                .WithEndpoint(Constraints.ENDPOINT)
                .WithCredentials(Constraints.ACCESS_KEY, Constraints.SECRET_KEY)
                .WithSSL(false)
                .Build();

            _mongoClient = new MongoClient(MongoDBSettings.ConnectionString);
            Directory.CreateDirectory(downloadDirectory);
        }

        
        // Scrape Xena, download IlluminaHiSeq pancan normalized files, upload to MinIO.
        
        public async Task ScrapeAndDownloadFilesAsync()
        {
            await ClearBucketAsync();

            using (IWebDriver driver = new ChromeDriver())
            {
                driver.Navigate().GoToUrl(mainUrl);
                await Task.Delay(5000);

                var cohortLinks = driver.FindElements(By.XPath("//a[contains(@href, 'cohort=TCGA')]"));
                var cohortUrls = cohortLinks.Select(el => el.GetAttribute("href")).ToList();

                foreach (var cohortUrl in cohortUrls)
                {
                    driver.Navigate().GoToUrl(cohortUrl);
                    await Task.Delay(5000);

                    var illuminaLinks = driver.FindElements(By.XPath("//a[contains(text(), 'IlluminaHiSeq pancan normalized')]"));
                    foreach (var link in illuminaLinks)
                    {
                        link.Click();
                        await Task.Delay(5000);

                        var downloadLink = driver.FindElements(By.XPath("//a[contains(@href, '.gz')]")).FirstOrDefault();
                        if (downloadLink != null)
                        {
                            string fileUrl = downloadLink.GetAttribute("href");
                            string fileName = Path.GetFileName(fileUrl);
                            string filePath = Path.Combine(downloadDirectory, fileName);

                            using (HttpClient client = new HttpClient())
                            {
                                await DownloadFile(client, fileUrl, filePath);
                                await UploadFileToMinIO(filePath);
                            }
                        }
                    }
                }
            }
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
                .WithBucket(Constraints.BucketName)
                .WithObject(objectName)
                .WithFileName(filePath)
                .WithObjectSize(new FileInfo(filePath).Length)
                .WithContentType("application/octet-stream"));
        }

        private async Task ClearBucketAsync()
        {
            var fileUrls = new List<string>();
            await _minioClient.ListObjectsAsync(new ListObjectsArgs()
                .WithBucket(Constraints.BucketName)
                .WithRecursive(true))
                .ForEachAsync(item => fileUrls.Add(item.Key));

            foreach (var fileUrl in fileUrls)
            {
                await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(Constraints.BucketName)
                    .WithObject(fileUrl));
            }
        }

       
       
        
        public async Task ProcessFilesFromMinIO()
        {
            var fileUrls = await ListFilesInMinIO();

            foreach (var fileUrl in fileUrls)
            {
                using var fileStream = await GetFileFromMinIO(fileUrl);
                var geneExpressions = ParseCsvFromStream(fileStream, fileUrl);
                await SaveGeneExpressionsToMongo(geneExpressions);
            }
        }

        private async Task<List<string>> ListFilesInMinIO()
        {
            var fileUrls = new List<string>();
            await _minioClient.ListObjectsAsync(new ListObjectsArgs()
                .WithBucket(Constraints.BucketName)
                .WithRecursive(true))
                .ForEachAsync(item => fileUrls.Add(item.Key));
            return fileUrls;
        }

        private async Task<Stream> GetFileFromMinIO(string fileName)
        {
            var memoryStream = new MemoryStream();
            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(Constraints.BucketName)
                .WithObject(fileName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)));

            memoryStream.Position = 0;

            
            var decompressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
            {
                await gzipStream.CopyToAsync(decompressedStream);
            }
            decompressedStream.Position = 0;
            return decompressedStream;
        }

        private List<GeneExpression> ParseCsvFromStream(Stream stream, string fileName)
        {
            var expressions = new List<GeneExpression>();
            var relevantGenes = new HashSet<string>
            {
                "C6orf150", "CCL5", "CXCL10", "TMEM173", "CXCL9", "CXCL11", "NFKB1",
                "IKBKE", "IRF3", "TREX1", "ATM", "IL6", "IL8"
            };

            using (var reader = new StreamReader(stream))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = "\t",
                BadDataFound = null
            }))
            {
                csv.Read();
                csv.ReadHeader();
                var patientIds = csv.HeaderRecord.Skip(1).ToList();

                var patientGeneExpressions = new Dictionary<string, GeneExpression>();

                while (csv.Read())
                {
                    var geneName = csv.GetField<string>(0);
                    for (int i = 0; i < patientIds.Count; i++)
                    {
                        var patientId = patientIds[i];
                        if (!patientGeneExpressions.ContainsKey(patientId))
                        {
                            patientGeneExpressions[patientId] = new GeneExpression
                            {
                                PatientId = patientId,
                                CancerCohort = ExtractCancerCohortFromFileName(fileName),
                                GeneValues = new Dictionary<string, double>()
                            };
                        }
                        if (relevantGenes.Contains(geneName))
                        {
                            var geneValue = csv.GetField<double>(i + 1);
                            patientGeneExpressions[patientId].GeneValues[geneName] = geneValue;
                        }
                    }
                }
                expressions = patientGeneExpressions.Values.ToList();
            }
            return expressions;
        }

        private string ExtractCancerCohortFromFileName(string fileName)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var cohortParts = fileNameWithoutExtension.Split('.');
            if (cohortParts.Length >= 2)
                return $"{cohortParts[0]}.{cohortParts[1]}";
            return "Unknown";
        }

        private async Task SaveGeneExpressionsToMongo(List<GeneExpression> geneExpressions)
        {
            var database = _mongoClient.GetDatabase(MongoDBSettings.DatabaseName);
            var collection = database.GetCollection<GeneExpression>(MongoDBSettings.CollectionName_GeneExpressions);
            await collection.InsertManyAsync(geneExpressions);
        }
    }
}

