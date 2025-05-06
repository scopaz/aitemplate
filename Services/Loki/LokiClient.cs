using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace anomalieDetectionLog.Services.Loki;

public class LokiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string? _username;
    private readonly string? _password;
    private readonly bool _useSampleData;
    private readonly SampleLogDataService? _sampleLogDataService;

    public LokiClient(IConfiguration configuration, SampleLogDataService? sampleLogDataService = null)
    {
        _httpClient = new HttpClient();
        _endpoint = configuration["Loki:Endpoint"] ?? "http://localhost:3100";
        _username = configuration["Loki:Username"];
        _password = configuration["Loki:Password"];
        
        // Determine if we should use sample data (for development/testing)
        _useSampleData = string.Equals(configuration["Loki:UseSampleData"], "true", StringComparison.OrdinalIgnoreCase) || 
                        _endpoint == "http://your-grafana-loki-server:3100";
        
        _sampleLogDataService = _useSampleData ? (sampleLogDataService ?? new SampleLogDataService()) : null;

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        // Add Basic Authentication if credentials are provided
        if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
        {
            var authBytes = Encoding.ASCII.GetBytes($"{_username}:{_password}");
            var base64Auth = Convert.ToBase64String(authBytes);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);
        }
    }

    /// <summary>
    /// Query Loki logs using LogQL
    /// </summary>
    /// <param name="query">LogQL query string</param>
    /// <param name="start">Start time in Unix timestamp (seconds)</param>
    /// <param name="end">End time in Unix timestamp (seconds)</param>
    /// <param name="limit">Maximum number of logs to return</param>
    /// <param name="direction">Query direction: forward or backward</param>
    /// <returns>LokiQueryResponse containing query results</returns>
    public async Task<LokiQueryResponse> QueryAsync(
        string query, 
        long start, 
        long end, 
        int limit = 100, 
        string direction = "backward")
    {
        // Return sample data if configured for development/testing
        if (_useSampleData && _sampleLogDataService != null)
        {
            // If query contains "anomaly" or "error", return sample anomalous logs
            if (query.Contains("anomaly", StringComparison.OrdinalIgnoreCase) || 
                query.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return _sampleLogDataService.GenerateAnomalousLogs(limit);
            }
            
            return _sampleLogDataService.GenerateSampleLogs(limit);
        }

        var queryUrl = $"{_endpoint.TrimEnd('/')}/loki/api/v1/query_range";

        var queryParams = new Dictionary<string, string>
        {
            { "query", query },
            { "start", start.ToString() },
            { "end", end.ToString() },
            { "limit", limit.ToString() },
            { "direction", direction }
        };

        var requestUrl = queryUrl + "?" + string.Join("&", queryParams.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));

        var response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LokiQueryResponse>() 
            ?? throw new InvalidOperationException("Failed to deserialize Loki query response.");
    }

    /// <summary>
    /// Get labels from Loki
    /// </summary>
    /// <returns>List of label names</returns>
    public async Task<LokiLabelsResponse> GetLabelsAsync()
    {
        // Return sample data if configured for development/testing
        if (_useSampleData && _sampleLogDataService != null)
        {
            return _sampleLogDataService.GenerateSampleLabels();
        }
        
        var requestUrl = $"{_endpoint.TrimEnd('/')}/loki/api/v1/labels";
        var response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LokiLabelsResponse>() 
            ?? throw new InvalidOperationException("Failed to deserialize Loki labels response.");
    }

    /// <summary>
    /// Get label values for a specific label
    /// </summary>
    /// <param name="labelName">Name of the label</param>
    /// <returns>List of label values</returns>
    public async Task<LokiLabelValuesResponse> GetLabelValuesAsync(string labelName)
    {
        // Return sample data if configured for development/testing
        if (_useSampleData && _sampleLogDataService != null)
        {
            return _sampleLogDataService.GenerateSampleLabelValues(labelName);
        }
        
        var requestUrl = $"{_endpoint.TrimEnd('/')}/loki/api/v1/label/{labelName}/values";
        var response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LokiLabelValuesResponse>() 
            ?? throw new InvalidOperationException("Failed to deserialize Loki label values response.");
    }
}

/// <summary>
/// Response from Loki /loki/api/v1/query_range endpoint
/// </summary>
public class LokiQueryResponse
{
    public string Status { get; set; } = string.Empty;
    public QueryData? Data { get; set; }
}

public class QueryData
{
    public string ResultType { get; set; } = string.Empty;
    public List<QueryResult> Result { get; set; } = new();
}

public class QueryResult
{
    public Dictionary<string, string> Stream { get; set; } = new();
    public List<LogEntry> Values { get; set; } = new();
}

public class LogEntry
{
    public string Timestamp { get; set; } = string.Empty;
    public string LogLine { get; set; } = string.Empty;

    // Factory method to create from Loki's string array format
    public static LogEntry FromLokiFormat(string[] entry)
    {
        if (entry.Length != 2)
        {
            throw new ArgumentException("Log entry must have exactly 2 elements (timestamp and log line)");
        }

        return new LogEntry
        {
            Timestamp = entry[0],
            LogLine = entry[1]
        };
    }
}

/// <summary>
/// Response from Loki /loki/api/v1/labels endpoint
/// </summary>
public class LokiLabelsResponse
{
    public string Status { get; set; } = string.Empty;
    public List<string> Data { get; set; } = new();
}

/// <summary>
/// Response from Loki /loki/api/v1/label/{name}/values endpoint
/// </summary>
public class LokiLabelValuesResponse
{
    public string Status { get; set; } = string.Empty;
    public List<string> Data { get; set; } = new();
}