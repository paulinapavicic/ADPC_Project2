using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WpfAppScraper.Models.Constraints;
using WpfAppScraper.Models;
using Minio;
using MongoDB.Driver;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium;
using Minio.DataModel.Args;
using CsvHelper;
using CsvHelper.Configuration;
using Minio.ApiEndpoints;
using System.Reactive.Linq;

namespace WpfAppScraper.Services
{
    public class CohortDataService
    {
        private readonly string mainUrl = "https://xenabrowser.net/datapages/?hub=https://tcga.xenahubs.net:443";
        private readonly string downloadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DownloadedFiles");
        private readonly string clinicalDataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "ClinicalSurvival");
        private readonly IMinioClient _minioClient;
        private readonly IMongoClient _mongoClient;


        public CohortDataService()
        {
            
            _minioClient = new MinioClient()
                                .WithEndpoint(Constraints.ENDPOINT)
                                .WithCredentials(Constraints.ACCESS_KEY, Constraints.SECRET_KEY)
                                .Build();

            _mongoClient = new MongoClient(MongoDBSettings.ConnectionString);

            InitializeBucketAsync().Wait();
        }

        private async Task InitializeBucketAsync()
        {
            var exists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(Constraints.BucketName));
            if (!exists)
            {
                await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(Constraints.BucketName));
                Console.WriteLine($"Bucket {Constraints.BucketName} created successfully");
            }
        }

        public async Task ScrapeAndDownloadFilesAsync()
        {
            // Сначала очищаем бакет
            await ClearBucketAsync();

            IWebDriver driver = new ChromeDriver();

            try
            {
                driver.Navigate().GoToUrl(mainUrl);
                await Task.Delay(5000); // Подождать, пока загрузится страница

                var cohortLinkElements = driver.FindElements(By.XPath("//a[contains(@href, 'cohort=TCGA')]"));

                List<string> cohortUrls = new List<string>();

                foreach (var cohortLinkElement in cohortLinkElements)
                {
                    string cohortUrl = cohortLinkElement.GetAttribute("href");
                    cohortUrls.Add(cohortUrl);
                }

                Console.WriteLine($"\nFound {cohortUrls.Count} cohort links.\n");

                foreach (var cohortUrl in cohortUrls)
                {
                    Console.WriteLine($"\nNavigating to cohort page: {cohortUrl}\n");

                    driver.Navigate().GoToUrl(cohortUrl);
                    await Task.Delay(5000); // Подождать, пока загрузится страница

                    var IlluminaHiSeqLinks = driver.FindElements(By.XPath("//a[contains(text(), 'IlluminaHiSeq pancan normalized')]"));

                    Console.WriteLine("***************************************************");
                    Console.WriteLine($"\nFound {IlluminaHiSeqLinks.Count} IlluminaHiSeq pancan normalized files\n");
                    Console.WriteLine("***************************************************");

                    if (IlluminaHiSeqLinks.Count > 0)
                    {
                        foreach (var link in IlluminaHiSeqLinks)
                        {
                            Console.WriteLine("Found the link. Navigating...");

                            link.Click();
                            await Task.Delay(5000); // Подождать, пока загрузится страница

                            var downloadLink = driver.FindElement(By.XPath("//a[contains(@href, '.gz')]"));

                            if (downloadLink != null)
                            {
                                string fileUrl = downloadLink.GetAttribute("href");
                                Console.WriteLine($"Download link found: {fileUrl}");

                                using (HttpClient client = new HttpClient())
                                {
                                    await DownloadFile(client, fileUrl);
                                }
                            }
                            else
                            {
                                Console.WriteLine("There is no .gz file at this link...");
                            }
                        }

                        Console.WriteLine("\nAll downloads are done for this cohort.\n");
                    }
                    else
                    {
                        Console.WriteLine("***************************************************");
                        Console.WriteLine("\nNo IlluminaHiSeq pancan normalized files found for this cohort.\n");
                        Console.WriteLine("***************************************************");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                driver.Quit(); // Закрываем драйвер после завершения работы
            }
        }
        private async Task ClearBucketAsync()
        {
            try
            {
                // Получаем все объекты из бакета с использованием рекурсивного поиска
                List<string> fileUrls = new List<string>();

                await _minioClient.ListObjectsAsync(new ListObjectsArgs()
                    .WithBucket(Constraints.BucketName)
                    .WithRecursive(true)) // Получаем объекты рекурсивно
                    .ForEachAsync(item =>
                    {
                        fileUrls.Add(item.Key);  // Добавляем объект в список
                    });

                // Проверяем, есть ли файлы для удаления
                if (fileUrls.Any())
                {
                    Console.WriteLine("Clearing all files from the bucket...");

                    // Удаляем все объекты, полученные из бакета
                    foreach (var fileUrl in fileUrls)
                    {
                        Console.WriteLine($"Deleting file: {fileUrl}");

                        // Используем RemoveObjectArgs для удаления объекта
                        await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                            .WithBucket(Constraints.BucketName)  // Указываем бакет
                            .WithObject(fileUrl));  // Указываем объект для удаления
                    }

                    Console.WriteLine("Bucket cleared successfully.");
                }
                else
                {
                    Console.WriteLine("No files found in the bucket to delete.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while clearing the bucket: {ex.Message}");
            }
        }

        private static async Task DownloadFile(HttpClient client, string fileUrl)
        {
            string fileName = Path.GetFileName(fileUrl);
            string gzFilePath = Path.Combine(Directory.GetCurrentDirectory(), "DownloadedFiles", fileName);

            client.Timeout = TimeSpan.FromMinutes(10);

            Console.WriteLine($"\nStarting download: {fileName}\n");

            try
            {
                using (var response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(gzFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        var isMoreToRead = true;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        {
                            while (isMoreToRead)
                            {
                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    await fs.WriteAsync(buffer, 0, read);
                                    totalRead += read;

                                    if (totalBytes != -1)
                                    {
                                        Console.WriteLine($"Downloaded {totalRead} of {totalBytes} bytes ({(totalRead * 100.0 / totalBytes):0.00}%).");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Downloaded {totalRead} bytes.");
                                    }
                                }
                            }
                        }
                    }

                    Console.WriteLine("\nDownload is successful\n");

                    // Загружаем файл в MinIO
                    CohortDataService cohortDataService = new CohortDataService();
                    await cohortDataService.UploadFileToMinIO(gzFilePath);  // Вызываем через экземпляр
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                Console.WriteLine("Download timed out. The file might be too large or the server is slow.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while downloading the file: {ex.Message}");
            }
        }

        private async Task UploadFileToMinIO(string filePath)
        {
            try
            {
                string objectName = Path.GetFileName(filePath); // Имя объекта в MinIO - это имя файла

                // Используем WithFileName для загрузки файла с диска
                await _minioClient.PutObjectAsync(new PutObjectArgs()
                    .WithBucket(Constraints.BucketName)
                    .WithObject(objectName)
                    .WithFileName(filePath)  // Загружаем файл с диска
                    .WithObjectSize(new FileInfo(filePath).Length)  // Указываем размер объекта
                    .WithContentType("application/octet-stream"));  // Указываем тип контента

                Console.WriteLine($"Successfully uploaded {objectName} to MinIO.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading file to MinIO: {ex.Message}");
            }
        }
        public async Task ProcessFilesFromMinIO()
        {
            List<string> fileUrls = await ListFilesInMinIO(); // Получаем список файлов в MinIO

            DeleteAllRecordsFromCollection(MongoDBSettings.CollectionName_GeneExpressions);

            var clinicalData = ParseClinicalSurvivalData();

            foreach (var fileUrl in fileUrls)
            {
                Console.WriteLine($"Processing file: {fileUrl}");

                // Загружаем файл из MinIO в память
                var fileStream = await GetFileFromMinIO(fileUrl);

                // Парсим содержимое файла
                var geneExpressions = ParseCsvFromStream(fileStream, fileUrl);

                var mergedData = MergeGeneExpressionsWithClinical(geneExpressions, clinicalData);

                Console.WriteLine("File parsing is done for file:" + fileUrl);

                // Сохраняем данные в MongoDB
                await SaveGeneExpressionsToMongo(mergedData);
            }
        }

        private async Task<List<string>> ListFilesInMinIO()
        {
            List<string> fileUrls = new List<string>();
            try
            {
                // Subscribe to the observable and collect items
                await _minioClient.ListObjectsAsync(new ListObjectsArgs()
                    .WithBucket(Constraints.BucketName)
                    .WithRecursive(true))
                    .ForEachAsync(item =>
                    {
                        fileUrls.Add(item.Key);  // Add item to list of file URLs
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing objects in MinIO: {ex.Message}");
            }
            return fileUrls;
        }

        private async Task<Stream> GetFileFromMinIO(string fileName)
        {
            MemoryStream decompressedStream = new MemoryStream();
            try
            {
                // Загружаем файл из MinIO в промежуточный MemoryStream
                var memoryStream = new MemoryStream();
                await _minioClient.GetObjectAsync(new GetObjectArgs()
                    .WithBucket(Constraints.BucketName)
                    .WithObject(fileName)
                    .WithCallbackStream((stream) =>
                    {
                        // Копируем поток MinIO в MemoryStream
                        stream.CopyTo(memoryStream);
                    }));

                // Сбрасываем позицию потока в начало перед его использованием
                memoryStream.Seek(0, SeekOrigin.Begin);

                // Теперь создаем GZipStream для распаковки
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                {
                    // Копируем распакованный поток в основной MemoryStream
                    await gzipStream.CopyToAsync(decompressedStream);
                }

                // Возвращаемся к началу распакованного потока
                decompressedStream.Seek(0, SeekOrigin.Begin);
                Console.WriteLine($"Successfully decompressed file {fileName}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting or decompressing file {fileName}: {ex.Message}");
            }
            return decompressedStream;
        }

        private List<GeneExpression> ParseCsvFromStream(Stream stream, string fileName)
        {
            List<GeneExpression> expressions = new List<GeneExpression>();
            // Список генов, которые нас интересуют
            var relevantGenes = new HashSet<string>
            {
                "C6orf150", "CCL5", "CXCL10", "TMEM173", "CXCL9", "CXCL11", "NFKB1",
                "IKBKE", "IRF3", "TREX1", "ATM", "IL6", "IL8"
            };
            try
            {
                using (var reader = new StreamReader(stream))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = "\t", // Используем табуляцию как разделитель
                    BadDataFound = null // Игнорируем поврежденные данные
                }))
                {
                    // Пропускаем заголовок
                    csv.Read();
                    csv.ReadHeader();

                    // Извлекаем имена пациентов из заголовков (все после первой колонки)
                    var patientIds = csv.HeaderRecord.Skip(1).ToList(); // Пропускаем "sample" и оставляем только ID пациентов

                    // Словарь для хранения всех данных по пациентам
                    var patientGeneExpressions = new Dictionary<string, GeneExpression>();

                    // Читаем каждую строку (имя гена)
                    while (csv.Read())
                    {
                        var geneName = csv.GetField<string>(0); // Имя гена (первая колонка)

                        // Для каждого пациента добавляем ген и его значение
                        for (int i = 0; i < patientIds.Count; i++)
                        {
                            var patientId = patientIds[i];

                            // Если объект для пациента еще не существует, создаем его
                            if (!patientGeneExpressions.ContainsKey(patientId))
                            {
                                patientGeneExpressions[patientId] = new GeneExpression
                                {
                                    PatientId = patientId,
                                    CancerCohort = ExtractCancerCohortFromFileName(fileName), // Извлекаем из имени файла
                                    GeneValues = new Dictionary<string, double>() // Словарь для генов
                                };
                            }

                            if (relevantGenes.Contains(geneName))
                            {
                                // Получаем значение для гена у пациента
                                var geneValue = csv.GetField<double>(i + 1); // Получаем значение для пациента
                                patientGeneExpressions[patientId].GeneValues[geneName] = geneValue; // Сохраняем в словарь
                            }
                        }
                    }

                    // Добавляем все объекты в список
                    expressions = patientGeneExpressions.Values.ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing file from stream: {ex.Message}");
            }
            return expressions;
        }

        private string ExtractCancerCohortFromFileName(string fileName)
        {
            // Извлекаем имя файла без расширения
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            // Разделяем строку по точкам и берем первые две части
            var cohortParts = fileNameWithoutExtension.Split('.');

            // Проверяем, чтобы в строке было хотя бы два сегмента
            if (cohortParts.Length >= 2)
            {
                return $"{cohortParts[0]}.{cohortParts[1]}"; // Формируем строку "TCGA.ACC"
            }

            // Если частей меньше, возвращаем "Unknown"
            return "Unknown";
        }

        public async Task SaveGeneExpressionsToMongo(List<GeneExpressionWithClinical> mergedData)
        {
            try
            {
                var database = _mongoClient.GetDatabase(MongoDBSettings.DatabaseName);
                var collection = database.GetCollection<GeneExpressionWithClinical>(MongoDBSettings.CollectionName_GeneExpressions);

                await collection.InsertManyAsync(mergedData);
                Console.WriteLine("Gene expressions with clinical data saved to MongoDB.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving to MongoDB: {ex.Message}");
            }
        }

        public void DeleteAllRecordsFromCollection(string collectionName)
        {
            var database = _mongoClient.GetDatabase(MongoDBSettings.DatabaseName);
            var collection = database.GetCollection<BsonDocument>(collectionName);

            // Удалить все документы из коллекции
            collection.DeleteMany(FilterDefinition<BsonDocument>.Empty);

            Console.WriteLine($"All records from collection '{collectionName}' have been deleted.");
        }

        private List<GeneExpressionWithClinical> MergeGeneExpressionsWithClinical(
            List<GeneExpression> geneExpressions,
            List<ClinicalSurvival> clinicalDataList)
        {
            var mergedData = new List<GeneExpressionWithClinical>();

            foreach (var geneExpression in geneExpressions)
            {
                // Получаем PatientId для текущего пациента
                var patientIdGeneExpression = geneExpression.PatientId;

                // Обрезаем суффикс в PatientId (удаляем последние символы после дефиса и числа)
                var basePatientIdGeneExpression = RemoveSuffix(patientIdGeneExpression);

                // Ищем клинические данные для этого PatientId
                var clinicalData = clinicalDataList.FirstOrDefault(cd =>
                    cd.PatientId == basePatientIdGeneExpression);

                if (clinicalData != null)
                {
                    // Добавляем объединенные данные
                    mergedData.Add(new GeneExpressionWithClinical
                    {
                        PatientId = geneExpression.PatientId,
                        CancerCohort = geneExpression.CancerCohort,
                        GeneValues = geneExpression.GeneValues,
                        DiseaseSpecificSurvival = clinicalData.DiseaseSpecificSurvival,
                        OverallSurvival = clinicalData.OverallSurvival,
                        ClinicalStage = clinicalData.ClinicalStage
                    });
                }
            }

            Console.WriteLine("Merged gene expression data with clinical data.");
            return mergedData;
        }

        // Метод для удаления суффикса из PatientId
        private string RemoveSuffix(string patientId)
        {
            // Ищем последний дефис и обрезаем все, что идет после него
            var lastDashIndex = patientId.LastIndexOf('-');
            if (lastDashIndex > 0)
            {
                return patientId.Substring(0, lastDashIndex);
            }

            return patientId; // Если дефиса нет, возвращаем сам id
        }

        public List<ClinicalSurvival> ParseClinicalSurvivalData()
        {
            var clinicalDataList = new List<ClinicalSurvival>();

            // Чтение файла
            string filePath = Path.Combine(clinicalDataDirectory, "TCGA_clinical_survival_data.tsv");

            try
            {
                var lines = File.ReadAllLines(filePath); // Читаем все строки из файла

                bool isHeader = true;
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue; // Пропускаем пустые строки

                    // Пропускаем заголовок
                    if (isHeader)
                    {
                        isHeader = false;
                        continue;
                    }

                    // Разделяем строку по табуляции
                    var columns = line.Split('\t');

                    if (columns.Length < 5) continue; // Проверка на количество колонок

                    // Создаем новый объект ClinicalSurvival и заполняем данные
                    var clinicalSurvival = new ClinicalSurvival
                    {
                        PatientId = columns[1].Trim(),
                        ClinicalStage = columns[6].Trim() == "[Not Applicable]" ? "Unknown" : columns[6].Trim(),
                        OverallSurvival = ParseNullableInt(columns[29]),  // Overall Survival
                        DiseaseSpecificSurvival = ParseNullableInt(columns[31])  // Disease-Specific Survival
                    };

                    // Добавляем объект в список
                    clinicalDataList.Add(clinicalSurvival);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing clinical survival data: {ex.Message}");
            }

            return clinicalDataList;
        }

        private int? ParseNullableInt(string value)
        {
            if (int.TryParse(value, out var result))
            {
                return result;
            }
            return null;
        }

    

}
}
