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




        public async Task UploadConsolidatedClinicalFileAsync(string localFilePath)
        {
            await ClearBucketAsync();
            string objectName = "TCGA_clinical_survival_data.tsv";
            await UploadFileToMinIO(localFilePath, objectName);
        }


        // Merge clinical data with gene expression data in MongoDB
        public async Task MergeClinicalWithGeneExpressionAsync(MongoService mongoService)
        {
            const string fileName = "TCGA_clinical_survival_data.tsv";

            // Download from MinIO
            var localPath = await DownloadClinicalFileFromMinIO(fileName);

            // Parse clinical data
            var clinicalData = TsvParser.ParseClinicalData(localPath);

            // Get all gene expressions
            var allGeneExpr = await mongoService.GetGeneExpressionsAsync();

            // Join and update
            foreach (var expr in allGeneExpr)
            {
                var barcodeParts = expr.PatientId.Trim().ToUpper().Split('-');
                var baseBarcode = string.Join("-", barcodeParts.Take(3));
                if (clinicalData.TryGetValue(baseBarcode, out var clinical))
                    expr.Clinical = clinical;
            }

            await mongoService.UpdateClinicalDataAsync(allGeneExpr);
            Console.WriteLine("Merged clinical data using consolidated file");
        }

        // Upload file to MinIO with optional custom object name
        public async Task UploadFileToMinIO(string filePath, string objectName = null)
        {
            objectName ??= Path.GetFileName(filePath);
            await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucketName)
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

    

