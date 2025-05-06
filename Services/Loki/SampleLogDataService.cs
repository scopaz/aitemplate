using System.Text.Json;

namespace anomalieDetectionLog.Services.Loki;

/// <summary>
/// Service that provides sample log data for demonstration and testing
/// </summary>
public class SampleLogDataService
{
    private readonly Random _random = new Random();
    
    /// <summary>
    /// Generate a sample Loki query response with realistic log data
    /// </summary>
    public LokiQueryResponse GenerateSampleLogs(int count = 50, bool includeErrors = true)
    {
        var timestamp = DateTimeOffset.UtcNow.AddHours(-1);
        var values = new List<LogEntry>();
        
        // Generate a mix of normal logs and occasional errors
        for (int i = 0; i < count; i++)
        {
            // Increment timestamp for each log entry (between 1-5 seconds)
            timestamp = timestamp.AddSeconds(_random.Next(1, 6));
            
            // Add a log entry (occasionally an error if includeErrors is true)
            if (includeErrors && _random.Next(10) < 2)  // 20% chance of error
            {
                values.Add(GenerateErrorLogEntry(timestamp));
            }
            else
            {
                values.Add(GenerateNormalLogEntry(timestamp));
            }
        }
        
        // Format the response like a real Loki response
        var response = new LokiQueryResponse
        {
            Status = "success",
            Data = new QueryData
            {
                ResultType = "streams",
                Result = new List<QueryResult>
                {
                    new QueryResult
                    {
                        Stream = new Dictionary<string, string>
                        {
                            ["app"] = "anomalie-detection",
                            ["env"] = "demo",
                            ["level"] = "info"
                        },
                        Values = values
                    }
                }
            }
        };
        
        return response;
    }
    
    /// <summary>
    /// Generate a normal log entry
    /// </summary>
    private LogEntry GenerateNormalLogEntry(DateTimeOffset timestamp)
    {
        var nanoseconds = timestamp.ToUnixTimeMilliseconds() * 1_000_000;
        
        var actions = new[]
        {
            "User login successful",
            "Data processed successfully",
            "API request completed",
            "Database query executed",
            "Cache refreshed",
            "Configuration loaded",
            "File uploaded",
            "Email notification sent",
            "Background task completed",
            "Health check passed"
        };
        
        var users = new[] { "user123", "admin", "service_account", "guest_user", "john.doe" };
        var components = new[] { "AuthService", "DataProcessor", "ApiController", "DatabaseManager", "CacheService" };
        
        var action = actions[_random.Next(actions.Length)];
        var user = users[_random.Next(users.Length)];
        var component = components[_random.Next(components.Length)];
        var duration = _random.Next(5, 1500);
        
        var logMessage = $"{component} - {action} for {user} in {duration}ms";
        
        return new LogEntry
        {
            Timestamp = nanoseconds.ToString(),
            LogLine = logMessage
        };
    }
    
    /// <summary>
    /// Generate an error log entry
    /// </summary>
    private LogEntry GenerateErrorLogEntry(DateTimeOffset timestamp)
    {
        var nanoseconds = timestamp.ToUnixTimeMilliseconds() * 1_000_000;
        
        var errors = new[]
        {
            "Connection timeout",
            "Database query failed",
            "Validation error",
            "Authentication failed",
            "Permission denied",
            "Resource not found",
            "Out of memory",
            "Unexpected exception"
        };
        
        var components = new[] { "AuthService", "DataProcessor", "ApiController", "DatabaseManager", "CacheService" };
        var error = errors[_random.Next(errors.Length)];
        var component = components[_random.Next(components.Length)];
        var errorCode = _random.Next(400, 600);
        
        var logMessage = $"ERROR in {component}: {error} (Code: {errorCode})";
        
        if (_random.Next(3) == 0)  // 1 in 3 chance to include a stack trace
        {
            logMessage += $"\nStack trace:\n  at {component}.ProcessRequest() in {component}.cs:line {_random.Next(50, 500)}\n" +
                         $"  at RequestHandler.Execute() in RequestHandler.cs:line {_random.Next(20, 300)}";
        }
        
        return new LogEntry
        {
            Timestamp = nanoseconds.ToString(),
            LogLine = logMessage
        };
    }
    
    /// <summary>
    /// Generate sample anomalous logs to simulate an issue
    /// </summary>
    public LokiQueryResponse GenerateAnomalousLogs(int count = 50)
    {
        var response = GenerateSampleLogs(count, true);
        
        if (response.Data?.Result != null && response.Data.Result.Count > 0)
        {
            var result = response.Data.Result[0];
            
            // Add some suspicious login attempts
            var timestamp = DateTimeOffset.UtcNow.AddHours(-1);
            
            for (int i = 0; i < 5; i++)
            {
                timestamp = timestamp.AddMinutes(_random.Next(1, 10));
                var nanoseconds = timestamp.ToUnixTimeMilliseconds() * 1_000_000;
                
                result.Values.Add(new LogEntry
                {
                    Timestamp = nanoseconds.ToString(),
                    LogLine = $"WARNING: Failed login attempt from IP {_random.Next(1, 255)}.{_random.Next(1, 255)}.{_random.Next(1, 255)}.{_random.Next(1, 255)} for user admin"
                });
            }
            
            // Add some memory spikes
            for (int i = 0; i < 3; i++)
            {
                timestamp = timestamp.AddMinutes(_random.Next(1, 5));
                var nanoseconds = timestamp.ToUnixTimeMilliseconds() * 1_000_000;
                
                result.Values.Add(new LogEntry
                {
                    Timestamp = nanoseconds.ToString(),
                    LogLine = $"WARNING: Memory usage spike detected: {_random.Next(85, 99)}% used"
                });
            }
            
            // Add some database timeout errors
            for (int i = 0; i < 4; i++)
            {
                timestamp = timestamp.AddSeconds(_random.Next(30, 90));
                var nanoseconds = timestamp.ToUnixTimeMilliseconds() * 1_000_000;
                
                result.Values.Add(new LogEntry
                {
                    Timestamp = nanoseconds.ToString(),
                    LogLine = $"ERROR: Database query timeout after {_random.Next(28, 35)}s for query 'SELECT * FROM large_table WHERE complex_condition'"
                });
            }
            
            // Sort the values by timestamp
            result.Values = result.Values
                .OrderBy(v => v.Timestamp)
                .ToList();
        }
        
        return response;
    }
    
    /// <summary>
    /// Generate a sample list of Loki labels
    /// </summary>
    public LokiLabelsResponse GenerateSampleLabels()
    {
        return new LokiLabelsResponse
        {
            Status = "success",
            Data = new List<string>
            {
                "app",
                "env",
                "level",
                "host",
                "namespace",
                "pod",
                "container",
                "job",
                "service",
                "region"
            }
        };
    }
    
    /// <summary>
    /// Generate sample values for a given label
    /// </summary>
    public LokiLabelValuesResponse GenerateSampleLabelValues(string labelName)
    {
        var values = labelName switch
        {
            "app" => new List<string> { "anomalie-detection", "authentication-service", "payment-processor", "api-gateway", "frontend" },
            "env" => new List<string> { "production", "staging", "development", "testing", "demo" },
            "level" => new List<string> { "debug", "info", "warning", "error", "critical" },
            "host" => new List<string> { "server-01", "server-02", "server-03", "worker-01", "worker-02" },
            "namespace" => new List<string> { "default", "kube-system", "monitoring", "logging", "application" },
            "service" => new List<string> { "api", "auth", "database", "cache", "worker", "scheduler" },
            _ => new List<string> { "value1", "value2", "value3" }
        };
        
        return new LokiLabelValuesResponse
        {
            Status = "success",
            Data = values
        };
    }
}