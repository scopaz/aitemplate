using System.Text.Json;
using Microsoft.Extensions.AI;
using System.Collections.Generic;

namespace anomalieDetectionLog.Services.Loki;

/// <summary>
/// Service for AI-powered analysis of logs from Grafana Loki
/// </summary>
public class LogAnalysisService
{
    private readonly LokiClient _lokiClient;
    private readonly IChatClient _chatClient;
    private readonly SemanticSearch _semanticSearch;

    public LogAnalysisService(LokiClient lokiClient, IChatClient chatClient, SemanticSearch semanticSearch)
    {
        _lokiClient = lokiClient;
        _chatClient = chatClient;
        _semanticSearch = semanticSearch;
    }

    /// <summary>
    /// Query logs and analyze them with AI
    /// </summary>
    /// <param name="logQuery">LogQL query to retrieve logs</param>
    /// <param name="analysisPrompt">Specific analysis to perform on logs</param>
    /// <param name="startTime">Start time (defaults to 1 hour ago)</param>
    /// <param name="endTime">End time (defaults to now)</param>
    /// <param name="limit">Maximum number of logs to analyze</param>
    /// <returns>Analysis result</returns>
    public async Task<LogAnalysisResult> AnalyzeLogsAsync(
        string logQuery, 
        string analysisPrompt, 
        DateTimeOffset? startTime = null, 
        DateTimeOffset? endTime = null, 
        int limit = 100)
    {
        // Default time range: last hour
        startTime ??= DateTimeOffset.UtcNow.AddHours(-1);
        endTime ??= DateTimeOffset.UtcNow;

        // Query logs from Loki
        var lokiResponse = await _lokiClient.QueryAsync(
            logQuery,
            startTime.Value.ToUnixTimeSeconds(),
            endTime.Value.ToUnixTimeSeconds(),
            limit
        );

        if (lokiResponse.Status != "success" || lokiResponse.Data?.Result == null || !lokiResponse.Data.Result.Any())
        {
            return new LogAnalysisResult
            {
                Success = false,
                ErrorMessage = "No logs found or query failed",
                LogSampleCount = 0
            };
        }

        // Extract log entries from response and format them
        var logEntries = lokiResponse.Data.Result
            .SelectMany(r => r.Values.Select(v => 
            {
                // Parse Loki timestamp (nanoseconds)
                var nanoseconds = long.Parse(v.Timestamp);
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(nanoseconds / 1_000_000);
                
                // Add stream labels to log entry
                var labelsStr = string.Join(" ", r.Stream.Select(kv => $"{kv.Key}={kv.Value}"));
                
                return $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] {labelsStr} | {v.LogLine}";
            }))
            .ToList();

        if (!logEntries.Any())
        {
            return new LogAnalysisResult
            {
                Success = false,
                ErrorMessage = "No log entries found",
                LogSampleCount = 0
            };
        }

        // Format the logs for AI analysis
        var logsText = string.Join("\n", logEntries);

        // Create messages for the chat API
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, @"You are an AI log analysis assistant. Analyze the log entries below and respond to the user's request. 
Always be factual and base your analysis only on the provided logs. If you can't determine something from the logs, say so.
Format your response in a clear, structured manner. Use markdown for readability as needed."),
            
            new ChatMessage(ChatRole.User, @$"Here are the logs I want you to analyze:
```
{logsText}
```

{analysisPrompt}")
        };

        // Options for the chat completion
        var options = new ChatOptions 
        { 
            Temperature = 0.0f  // Lower temperature for more analytical, factual responses
        };

        // Call the chat API with the correct parameters
        var response = await _chatClient.GetResponseAsync(messages, options);
        
        // Extract the content from the ChatResponse object
        string analysisContent;
        if (response != null)
        {
            // Try to access content based on the typical ChatResponse structure
            analysisContent = response.ToString();
        }
        else
        {
            analysisContent = "No analysis was generated.";
        }
            
        return new LogAnalysisResult
        {
            Success = true,
            Analysis = analysisContent,
            LogSampleCount = logEntries.Count,
            TimeRange = $"{startTime.Value:yyyy-MM-dd HH:mm:ss} to {endTime.Value:yyyy-MM-dd HH:mm:ss}",
            Query = logQuery
        };
    }

    /// <summary>
    /// Detect anomalies in logs using AI
    /// </summary>
    /// <param name="logQuery">LogQL query to retrieve logs</param>
    /// <param name="startTime">Start time (defaults to 1 hour ago)</param>
    /// <param name="endTime">End time (defaults to now)</param>
    /// <param name="limit">Maximum number of logs to analyze</param>
    /// <returns>Analysis with detected anomalies</returns>
    public async Task<LogAnalysisResult> DetectAnomaliesAsync(
        string logQuery, 
        DateTimeOffset? startTime = null, 
        DateTimeOffset? endTime = null, 
        int limit = 500)
    {
        return await AnalyzeLogsAsync(
            logQuery,
            "Analyze these logs for anomalies, errors, or unusual patterns. " +
            "Identify any potential issues, their severity, and suggest possible causes and solutions. " +
            "If there are error messages, explain what they mean and how to address them. " +
            "Also highlight any suspicious activity or security concerns.",
            startTime,
            endTime,
            limit
        );
    }

    /// <summary>
    /// Summarize logs using AI
    /// </summary>
    /// <param name="logQuery">LogQL query to retrieve logs</param>
    /// <param name="startTime">Start time (defaults to 1 hour ago)</param>
    /// <param name="endTime">End time (defaults to now)</param>
    /// <param name="limit">Maximum number of logs to analyze</param>
    /// <returns>Log summary</returns>
    public async Task<LogAnalysisResult> SummarizeLogsAsync(
        string logQuery, 
        DateTimeOffset? startTime = null, 
        DateTimeOffset? endTime = null, 
        int limit = 500)
    {
        return await AnalyzeLogsAsync(
            logQuery,
            "Provide a concise summary of these logs. Include main activities, " +
            "key events, error rates, and overall system health. " +
            "Organize the summary by categories or components if appropriate.",
            startTime,
            endTime,
            limit
        );
    }
}

/// <summary>
/// Result of a log analysis operation
/// </summary>
public class LogAnalysisResult
{
    public bool Success { get; set; }
    public string Analysis { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int LogSampleCount { get; set; }
    public string TimeRange { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
}