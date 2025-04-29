using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace anomalieDetectionLog.Services.Ingestion;

public class JsonLogDirectorySource(string sourceDirectory) : IIngestionSource
{
    public static string SourceFileId(string path) => Path.GetFileName(path);

    public string SourceId => $"{nameof(JsonLogDirectorySource)}:{sourceDirectory}";

    public async Task<IEnumerable<IngestedDocument>> GetNewOrModifiedDocumentsAsync(IQueryable<IngestedDocument> existingDocuments)
    {
        var results = new List<IngestedDocument>();
        var sourceFiles = Directory.GetFiles(sourceDirectory, "*.json");

        foreach (var sourceFile in sourceFiles)
        {
            var sourceFileId = SourceFileId(sourceFile);
            var sourceFileVersion = File.GetLastWriteTimeUtc(sourceFile).ToString("o");

            var existingDocument = await existingDocuments.Where(d => d.SourceId == SourceId && d.Id == sourceFileId).FirstOrDefaultAsync();
            if (existingDocument is null)
            {
                results.Add(new() { Id = sourceFileId, Version = sourceFileVersion, SourceId = SourceId });
            }
            else if (existingDocument.Version != sourceFileVersion)
            {
                existingDocument.Version = sourceFileVersion;
                results.Add(existingDocument);
            }
        }

        return results;
    }

    public async Task<IEnumerable<IngestedDocument>> GetDeletedDocumentsAsync(IQueryable<IngestedDocument> existingDocuments)
    {
        var sourceFiles = Directory.GetFiles(sourceDirectory, "*.json");
        var sourceFileIds = sourceFiles.Select(SourceFileId).ToList();
        return await existingDocuments
            .Where(d => d.SourceId == SourceId && !sourceFileIds.Contains(d.Id))
            .ToListAsync();
    }

    public async Task<IEnumerable<SemanticSearchRecord>> CreateRecordsForDocumentAsync(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, string documentId)
    {
        var filePath = Path.Combine(sourceDirectory, documentId);
        var jsonContent = await File.ReadAllTextAsync(filePath);
        var logEntries = JsonSerializer.Deserialize<List<JsonElement>>(jsonContent);

        if (logEntries == null)
        {
            return Array.Empty<SemanticSearchRecord>();
        }

        var records = new List<SemanticSearchRecord>();
        var currentPage = 1;
        var currentText = new List<string>();

        foreach (var entry in logEntries)
        {
            var logText = entry.ToString();
            currentText.Add(logText);

            // Create a new record every 10 log entries or when the text gets too long
            if (currentText.Count >= 10 || string.Join("\n", currentText).Length > 1000)
            {
                var text = string.Join("\n", currentText);
                var embeddings = await embeddingGenerator.GenerateAsync([text]);
                var embedding = embeddings.First();
                
                records.Add(new SemanticSearchRecord
                {
                    Key = $"{Path.GetFileNameWithoutExtension(documentId)}_{currentPage}",
                    FileName = documentId,
                    PageNumber = currentPage,
                    Text = text,
                    Vector = embedding.Vector
                });

                currentText.Clear();
                currentPage++;
            }
        }

        // Add any remaining text
        if (currentText.Count > 0)
        {
            var text = string.Join("\n", currentText);
            var embeddings = await embeddingGenerator.GenerateAsync([text]);
            var embedding = embeddings.First();
            
            records.Add(new SemanticSearchRecord
            {
                Key = $"{Path.GetFileNameWithoutExtension(documentId)}_{currentPage}",
                FileName = documentId,
                PageNumber = currentPage,
                Text = text,
                Vector = embedding.Vector
            });
        }

        return records;
    }
} 