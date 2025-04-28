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
        // Target genes as specified in requirements
        private static readonly HashSet<string> TargetGenes = new()
        {
            "C6orf150", "CCL5", "CXCL10", "TMEM173", "CXCL9", "CXCL11",
            "NFKB1", "IKBKE", "IRF3", "TREX1", "ATM", "IL6", "IL8"
        };

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

                // Read header
                csv.Read();
                csv.ReadHeader();

                // Get patient IDs from header (skip first column which is gene name)
                var patientIds = csv.HeaderRecord.Skip(1).ToList();

                // Process each gene row
                while (csv.Read())
                {
                    var geneName = csv.GetField<string>(0);

                    // Check if this is one of our target genes
                    if (TargetGenes.Contains(geneName))
                    {
                        // Process expression values for each patient
                        for (int i = 0; i < patientIds.Count; i++)
                        {
                            var patientId = patientIds[i];

                            // Create patient record if it doesn't exist
                            if (!patientExpressions.ContainsKey(patientId))
                            {
                                patientExpressions[patientId] = new GeneExpression
                                {
                                    PatientId = patientId,
                                    CancerCohort = cohortName,
                                    GeneValues = new Dictionary<string, double>()
                                };
                            }

                            // Add this gene's expression value
                            if (double.TryParse(csv.GetField(i + 1), out var value))
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

                while (csv.Read())
                {
                    var patientId = csv.GetField("bcr_patient_barcode");

                    if (!string.IsNullOrEmpty(patientId))
                    {
                        clinicalData[patientId] = new ClinicalSurvival
                        {
                            PatientId = patientId,
                            DiseaseSpecificSurvival = ParseNullableInt(csv.GetField("DSS")),
                            OverallSurvival = ParseNullableInt(csv.GetField("OS")),
                            ClinicalStage = csv.GetField("clinical_stage") ?? "Unknown"
                        };
                    }
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
            {
                return result;
            }
            return null;
        }
    
}
}
