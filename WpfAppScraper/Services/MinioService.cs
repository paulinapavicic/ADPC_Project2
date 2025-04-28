using Minio.DataModel.Args;
using Minio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfAppScraper.Services
{
    public class MinioService
    {
        private readonly IMinioClient _client;
        private readonly string _bucketName;

        public MinioService(string endpoint, string accessKey, string secretKey, string bucket)
        {
            _client = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(false)
                .Build();

            _bucketName = bucket;
            InitializeBucket().Wait();
        }

        private async Task InitializeBucket()
        {
            var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucketName));
            if (!exists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));
                Console.WriteLine($"Created bucket: {_bucketName}");
            }
        }

        public async Task UploadFileAsync(string objectName, Stream data)
        {
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithStreamData(data)
                .WithObjectSize(data.Length));
        }
    }
}
