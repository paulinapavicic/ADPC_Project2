using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfAppScraper.Models
{
    public class ClinicalSurvival
    {
        [BsonElement("patient_id")]
        public string PatientId { get; set; }

        [BsonElement("dss")]
        public int? DiseaseSpecificSurvival { get; set; }

        [BsonElement("os")]
        public int? OverallSurvival { get; set; }

        [BsonElement("clinical_stage")]
        public string ClinicalStage { get; set; }
    }
}
