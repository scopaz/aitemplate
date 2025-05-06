using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using anomalieDetectionLog.Components;
using anomalieDetectionLog.Services;
using anomalieDetectionLog.Services.Ingestion;
using anomalieDetectionLog.Services.Loki;
using OpenAI;
using System.ClientModel;
using NLog;
using NLog.Web;

// Initialize NLog
var logger = NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();
try
{
    logger.Info("Application Starting Up");
    var builder = WebApplication.CreateBuilder(args);

    // Add NLog services
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    builder.Services.AddRazorComponents().AddInteractiveServerComponents();

    // You will need to set the GITHUB_TOKEN environment variable with your GitHub token
    var credential = new ApiKeyCredential(builder.Configuration["GitHub:Token"] ?? throw new InvalidOperationException("GitHub token is not set in configuration."));
    var openAIOptions = new OpenAIClientOptions()
    {
        Endpoint = new Uri(builder.Configuration["AI:Endpoint"] ?? "https://models.inference.ai.azure.com")
    };

    var ghModelsClient = new OpenAIClient(credential, openAIOptions);
    var chatClient = ghModelsClient.GetChatClient(builder.Configuration["AI:ChatModel"] ?? "gpt-4o").AsIChatClient();
    var embeddingGenerator = ghModelsClient.GetEmbeddingClient(builder.Configuration["AI:EmbeddingModel"] ?? "text-embedding-3-small").AsIEmbeddingGenerator();

    var vectorStorePath = builder.Configuration["Storage:VectorStore:Path"] ?? "vector-store";
    var vectorStore = new JsonVectorStore(Path.Combine(AppContext.BaseDirectory, vectorStorePath));

    builder.Services.AddSingleton<IVectorStore>(vectorStore);
    builder.Services.AddScoped<DataIngestor>();
    builder.Services.AddSingleton<SemanticSearch>();
    builder.Services.AddChatClient(chatClient).UseFunctionInvocation().UseLogging();
    builder.Services.AddEmbeddingGenerator(embeddingGenerator);

    // Register Loki services
    builder.Services.AddSingleton<SampleLogDataService>();
    builder.Services.AddSingleton<LokiClient>();
    builder.Services.AddScoped<LogAnalysisService>();

    var sqliteConnectionString = builder.Configuration["Storage:SQLite:ConnectionString"] ?? "Data Source=ingestioncache.db";
    builder.Services.AddDbContext<IngestionCacheDbContext>(options =>
        options.UseSqlite(sqliteConnectionString));

    var app = builder.Build();
    IngestionCacheDbContext.Initialize(app.Services);

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseAntiforgery();

    app.UseStaticFiles();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Get ingestion directories from configuration
    var pdfDirectory = builder.Configuration["Ingestion:PDF:Directory"] ?? "wwwroot/Data";
    var logsDirectory = builder.Configuration["Ingestion:Logs:Directory"] ?? "wwwroot/Logs";

    // Ingest PDF files
    await DataIngestor.IngestDataAsync(
        app.Services,
        new PDFDirectorySource(Path.Combine(app.Environment.ContentRootPath, pdfDirectory)));

    // Ingest JSON log files
    await DataIngestor.IngestDataAsync(
        app.Services,
        new JsonLogDirectorySource(Path.Combine(app.Environment.ContentRootPath, logsDirectory)));

    // Example of ingesting logs from Loki (commented out until configured)
    /*
    if (!string.IsNullOrEmpty(builder.Configuration["Loki:Endpoint"]) && 
        builder.Configuration["Loki:Endpoint"] != "http://your-grafana-loki-server:3100")
    {
        logger.Info("Ingesting logs from Loki");
        var lokiClient = app.Services.GetRequiredService<LokiClient>();
        
        // Create a Loki log source with a query for the last 24 hours
        var lokiLogSource = new LokiLogSource(
            lokiClient,
            "{app=\"anomalie-detection\"}",  // LogQL query - customize as needed
            TimeSpan.FromHours(24),
            app.Services,                    // Pass the service provider for database access
            "loki-app-logs"
        );
        
        await DataIngestor.IngestDataAsync(app.Services, lokiLogSource);
    }
    */

    logger.Info("Application started successfully");
    app.Run();
}
catch (Exception ex)
{
    // Log any startup exceptions
    logger.Error(ex, "Application stopped due to exception");
    throw;
}
finally
{
    // Ensure proper shutdown of NLog
    LogManager.Shutdown();
}