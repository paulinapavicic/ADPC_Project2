using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WpfAppScraper.Models;
using WpfAppScraper.Models.Constraints;

namespace WpfAppScraper.Services
{
   public class MongoService
    {
        private readonly IMongoCollection<GeneExpressionWithClinical> _geneExpressionCollection;
        private readonly IMongoClient _mongoClient;

        public MongoService()
        {
            _mongoClient = new MongoClient(MongoDBSettings.ConnectionString);
            var database = _mongoClient.GetDatabase(MongoDBSettings.DatabaseName);

            _geneExpressionCollection = database.GetCollection<GeneExpressionWithClinical>(MongoDBSettings.CollectionName_GeneExpressions);
        }

        public async Task<List<GeneExpressionWithClinical>> GetGeneExpressionsAsync()
        {
            return await _geneExpressionCollection.Find(_ => true).ToListAsync();
        }
    }
}
