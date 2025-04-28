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
        private readonly IMongoCollection<GeneExpression> _geneExpressions;
        private readonly IMongoClient _mongoClient;

        public MongoService()
        {
            _mongoClient = new MongoClient(MongoDBSettings.ConnectionString);
            var database = _mongoClient.GetDatabase(MongoDBSettings.DatabaseName);
            _geneExpressions = database.GetCollection<GeneExpression>(MongoDBSettings.CollectionName_GeneExpressions);
        }

        public async Task<List<GeneExpression>> GetGeneExpressionsAsync()
        {
            return await _geneExpressions.Find(_ => true).ToListAsync();
        }

        public async Task<List<GeneExpression>> GetExpressionsByCohortAsync(string cohort)
        {
            var filter = Builders<GeneExpression>.Filter.Eq(g => g.CancerCohort, cohort);
            return await _geneExpressions.Find(filter).ToListAsync();
        }

        public async Task<GeneExpression> GetExpressionByPatientIdAsync(string patientId)
        {
            var filter = Builders<GeneExpression>.Filter.Eq(g => g.PatientId, patientId);
            return await _geneExpressions.Find(filter).FirstOrDefaultAsync();
        }

        public async Task InsertGeneExpressionsAsync(List<GeneExpression> expressions)
        {
            try
            {
                await _geneExpressions.InsertManyAsync(expressions);
                Console.WriteLine($"Inserted {expressions.Count} gene expression records");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting gene expressions: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateClinicalDataAsync(List<GeneExpression> expressions)
        {
            var bulkOps = new List<WriteModel<GeneExpression>>();

            foreach (var expr in expressions)
            {
                var filter = Builders<GeneExpression>.Filter.Eq(g => g.PatientId, expr.PatientId);
                var update = Builders<GeneExpression>.Update.Set(g => g.Clinical, expr.Clinical);
                bulkOps.Add(new UpdateOneModel<GeneExpression>(filter, update) { IsUpsert = true });
            }

            if (bulkOps.Count > 0)
            {
                await _geneExpressions.BulkWriteAsync(bulkOps);
                Console.WriteLine($"Updated clinical data for {bulkOps.Count} patients");
            }
        }

        public async Task ClearCollectionAsync()
        {
            await _geneExpressions.DeleteManyAsync(FilterDefinition<GeneExpression>.Empty);
            Console.WriteLine("Cleared gene expression collection");
        }
    }
}
