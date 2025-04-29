using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using anomalieDetectionLog.Components;
using anomalieDetectionLog.Services;
using anomalieDetectionLog.Services.Ingestion;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);
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

app.Run(); 