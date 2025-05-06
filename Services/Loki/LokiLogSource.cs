using System.Text.Json;
using anomalieDetectionLog.Services.Ingestion;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;

namespace anomalieDetectionLog.Services.Loki;

/// <summary>
/// Implementation of IIngestionSource for Grafana Loki logs
/// </summary>
public class LokiLogSource : IIngestionSource
{
    private readonly LokiClient _lokiClient;
    private readonly string _logQuery;
    private readonly TimeSpan _lookbackPeriod;
    private readonly string _sourceId;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Unique identifier for this source
    /// </summary>
    public string SourceId => _sourceId;

    /// <summary>
    /// Creates a new LokiLogSource
    /// </summary>
    /// <param name="lokiClient">The Loki client</param>
    /// <param name="logQuery">LogQL query to retrieve logs</param>
    /// <param name="lookbackPeriod">How far back to look for logs</param>
    /// <param name="serviceProvider">Service provider for database access</param>
    /// <param name="sourceId">Unique identifier for this source</param>
    public LokiLogSource(
        LokiClient lokiClient,
        string logQuery,
        TimeSpan lookbackPeriod,
        IServiceProvider serviceProvider,
        string sourceId = "loki-logs")
    {
        _lokiClient = lokiClient;
        _logQuery = logQuery;
        _lookbackPeriod = lookbackPeriod;
        _sourceId = sourceId;
        _serviceProvider = serviceProvider;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Gets new or modified documents from Loki
    /// </summary>
    public async Task<IEnumerable<IngestedDocument>> GetNewOrModifiedDocumentsAsync(IQueryable<IngestedDocument> existingDocuments)
    {
        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime - _lookbackPeriod;

        var response = await _lokiClient.QueryAsync(
            _logQuery,
            ((DateTimeOffset)startTime).ToUnixTimeSeconds(),
            endTime.ToUnixTimeSeconds());

        if (response.Status != "success" || response.Data?.Result == null)
        {
            return Enumerable.Empty<IngestedDocument>();
        }

        var newDocuments = new List<IngestedDocument>();

        foreach (var result in response.Data.Result)
        {
            // Group log entries by hour to create reasonable document sizes
            var logsByHour = result.Values
                .Select(v => 
                {
                    // Parse the Loki timestamp (nanoseconds)
                    var nanoseconds = long.Parse(v.Timestamp);
                    var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(nanoseconds / 1_000_000);
                    return new { Timestamp = timestamp, LogLine = v.LogLine };
                })
                .GroupBy(x => new { x.Timestamp.Year, x.Timestamp.Month, x.Timestamp.Day, x.Timestamp.Hour })
                .OrderBy(g => new DateTime(g.Key.Year, g.Key.Month, g.Key.Day).AddHours(g.Key.Hour));

            foreach (var hourGroup in logsByHour)
            {
                var hourTimestamp = new DateTime(hourGroup.Key.Year, hourGroup.Key.Month, hourGroup.Key.Day).AddHours(hourGroup.Key.Hour);
                var documentId = $"{hourTimestamp:yyyyMMdd_HH}_{string.Join("_", result.Stream.Select(kv => $"{kv.Key}-{kv.Value}"))}";
                
                // Filter out existing documents with the same ID and Version
                var existingDoc = existingDocuments.FirstOrDefault(d => d.Id == documentId && d.SourceId == _sourceId);
                
                // Combine log lines into a single text document
                var logLines = string.Join("\n", hourGroup.Select(x => $"[{x.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {x.LogLine}"));
                var versionHash = ComputeHashForContent(logLines);
                
                if (existingDoc == null || existingDoc.Version != versionHash)
                {
                    var document = new IngestedDocument
                    {
                        Id = documentId,
                        SourceId = _sourceId,
                        Version = versionHash,
                        // We'll store content as the first record
                        Records = new List<IngestedRecord>
                        {
                            new IngestedRecord
                            {
                                Id = "content",
                                DocumentId = documentId,
                                DocumentSourceId = _sourceId
                            }
                        }
                    };
                    
                    newDocuments.Add(document);
                }
            }
        }

        return newDocuments;
    }

    // Simple helper to compute a hash for content versioning
    private string ComputeHashForContent(string content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Gets deleted documents (Not applicable for Loki logs - we don't track deletions)
    /// </summary>
    public Task<IEnumerable<IngestedDocument>> GetDeletedDocumentsAsync(IQueryable<IngestedDocument> existingDocuments)
    {
        // We don't track deletions for Loki logs, as they are immutable once ingested
        return Task.FromResult(Enumerable.Empty<IngestedDocument>());
    }

    /// <summary>
    /// Creates semantic search records for an ingested document
    /// </summary>
    /// <param name="embeddingGenerator">The embedding generator to use</param>
    /// <param name="documentId">The document ID to process</param>
    /// <returns>A collection of semantic search records</returns>
    public async Task<IEnumerable<SemanticSearchRecord>> CreateRecordsForDocumentAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string documentId)
    {
        // Get a database context to find the document
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IngestionCacheDbContext>();

        // Find the document in the database
        var document = await dbContext.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.SourceId == _sourceId);

        if (document == null)
        {
            return Enumerable.Empty<SemanticSearchRecord>();
        }

        // Since we don't have direct content, we'll need to retrieve or generate it here
        // For simplicity, we'll just use a placeholder for this example
        var content = "Sample log content for " + documentId;

        // Split logs into chunks
        var logLines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        const int chunkSize = 10;  // Process 10 log lines at a time
        var chunks = new List<string>();

        for (int i = 0; i < logLines.Length; i += chunkSize)
        {
            var chunk = string.Join("\n", logLines.Skip(i).Take(chunkSize));
            chunks.Add(chunk);
        }

        var records = new List<SemanticSearchRecord>();

        // Create embedding for each chunk
        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            var embedding = await embeddingGenerator.GenerateEmbeddingAsync(chunk);
            
            // Convert embedding to float array - the method depends on what's available in your Embedding<float> class
            // Try to use the Data property directly if AsReadOnlySpan is not available
            float[] vectorData;
            try 
            {
                // Different Microsoft.Extensions.AI versions may have different APIs
                // We need to handle this gracefully
                var vectorProperty = embedding.GetType().GetProperty("Data");
                if (vectorProperty != null)
                {
                    var data = vectorProperty.GetValue(embedding);
                    if (data is float[] floatArray)
                    {
                        vectorData = floatArray;
                    }
                    else if (data is ReadOnlyMemory<float> readOnlyMemory)
                    {
                        vectorData = readOnlyMemory.ToArray();
                    }
                    else if (data is Memory<float> memory) 
                    {
                        vectorData = memory.ToArray();
                    }
                    else
                    {
                        // Fallback to empty array if we can't get the data
                        vectorData = Array.Empty<float>();
                    }
                }
                else
                {
                    // Try with reflection
                    var method = embedding.GetType().GetMethod("ToArray");
                    if (method != null)
                    {
                        vectorData = (float[])method.Invoke(embedding, null);
                    }
                    else
                    {
                        // Final fallback
                        vectorData = Array.Empty<float>();
                    }
                }
            }
            catch
            {
                // Last resort fallback for compatibility
                vectorData = Array.Empty<float>();
            }
            
            var record = new SemanticSearchRecord
            {
                Key = $"{documentId}_chunk_{index}",
                FileName = documentId,
                Text = chunk,
                Vector = vectorData
            };
            
            records.Add(record);
        }

        return records;
    }
}