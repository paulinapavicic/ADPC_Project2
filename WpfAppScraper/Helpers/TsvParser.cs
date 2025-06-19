using CsvHelper.Configuration;
using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WpfAppScraper.Models;
using System.IO.Compression;

namespace WpfAppScraper.Helpers
{
    public class TsvParser
    {
        
        private static readonly HashSet<string> TargetGenes = new()
        {
            "C6orf150", "CCL5", "CXCL10", "TMEM173", "CXCL9", "CXCL11",
            "NFKB1", "IKBKE", "IRF3", "TREX1", "ATM", "IL6", "IL8"
        };

      
        //Parses a gzipped gene expression TSV file from a stream.
     
        public static List<GeneExpression> ParseGeneExpressions(Stream gzipStream, string cohortName)
        {
            var patientExpressions = new Dictionary<string, GeneExpression>();

            try
            {
                using var decompressionStream = new GZipStream(gzipStream, CompressionMode.Decompress);
                using var reader = new StreamReader(decompressionStream);
                using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = "\t",
                    BadDataFound = null
                });

                csv.Read();
                csv.ReadHeader();

                var patientIds = csv.HeaderRecord.Skip(1).Select(id => id?.Trim().ToUpper()).ToList();

                while (csv.Read())
                {
                    var geneName = csv.GetField<string>(0);

                    if (TargetGenes.Contains(geneName))
                    {
                        for (int i = 0; i < patientIds.Count; i++)
                        {
                            var patientId = patientIds[i];
                            if (string.IsNullOrEmpty(patientId)) continue;

                            if (!patientExpressions.ContainsKey(patientId))
                            {
                                patientExpressions[patientId] = new GeneExpression
                                {
                                    PatientId = patientId,
                                    CancerCohort = cohortName,
                                    GeneValues = new Dictionary<string, double>()
                                };
                            }

                            if (double.TryParse(csv.GetField(i + 1), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                            {
                                patientExpressions[patientId].GeneValues[geneName] = value;
                            }
                        }
                    }
                }

                Console.WriteLine($"Parsed {patientExpressions.Count} patients with target gene data");
                return patientExpressions.Values.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing gene expression data: {ex.Message}");
                return new List<GeneExpression>();
            }
        }

       
        public static Dictionary<string, ClinicalSurvival> ParseClinicalData(string filePath)
        {
            var clinicalData = new Dictionary<string, ClinicalSurvival>();

            try
            {
                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = "\t",
                    BadDataFound = null
                });

                csv.Read();
                csv.ReadHeader();

               
                int colBarcode = -1;
                if (csv.HeaderRecord.Contains("sample"))
                    colBarcode = csv.GetFieldIndex("sample");
                else if (csv.HeaderRecord.Contains("bcr_patient_barcode"))
                    colBarcode = csv.GetFieldIndex("bcr_patient_barcode");
                else
                    colBarcode = 0; 

                int colDSS = csv.GetFieldIndex("DSS", isTryGet: true);
                int colOS = csv.GetFieldIndex("OS", isTryGet: true);
                int colStage = csv.GetFieldIndex("clinical_stage", isTryGet: true);

                while (csv.Read())
                {
                    var patientId = csv.GetField(colBarcode)?.Trim().ToUpper();
                    if (string.IsNullOrEmpty(patientId)) continue;

                    int? dss = colDSS >= 0 ? ParseNullableInt(csv.GetField(colDSS)) : null;
                    int? os = colOS >= 0 ? ParseNullableInt(csv.GetField(colOS)) : null;
                    string stage = colStage >= 0 ? csv.GetField(colStage) : null;

                    clinicalData[patientId] = new ClinicalSurvival
                    {
                        PatientId = patientId,
                        DiseaseSpecificSurvival = dss,
                        OverallSurvival = os,
                        ClinicalStage = stage
                    };
                }

                Console.WriteLine($"Parsed {clinicalData.Count} clinical records");
                return clinicalData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing clinical data: {ex.Message}");
                return new Dictionary<string, ClinicalSurvival>();
            }
        }

        private static int? ParseNullableInt(string value)
        {
            if (int.TryParse(value, out var result))
                return result;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return (int)d;
            return null;
        }

    }
}
