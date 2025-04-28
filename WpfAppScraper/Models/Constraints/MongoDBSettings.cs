using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfAppScraper.Models.Constraints
{
    public class MongoDBSettings
    {
        public const string ConnectionString = "mongodb+srv://ppavicic:Bax1407pp@cluster0.un5ewq6.mongodb.net/?retryWrites=true&w=majority&appName=Cluster0";
        public const string DatabaseName = "GeneExpressionDB";
        public const string CollectionName_GeneExpressions = "GeneExpressions";
    }
}
