using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;
using ChordApi.Services;
using ChordApi.Models;

var builder = WebApplication.CreateBuilder(args);

var analyzerConfig = builder.Configuration.GetSection("Analyzer").Get<ChordApi.Models.AnalyzerConfig>() ?? new ChordApi.Models.AnalyzerConfig();
var storageConfig = builder.Configuration.GetSection("Storage").Get<ChordApi.Models.StorageConfig>() ?? new ChordApi.Models.StorageConfig();

var app = builder.Build();

var storageDir = Path.Combine(Directory.GetCurrentDirectory(), "storage");

if (storageConfig.CleanOnStartup) StorageService.CleanStorage(storageDir);
else StorageService.EnsureStorage(storageDir);

var existingStore = StorageService.LoadExistingStore(storageDir);

app.UseDefaultFiles();
app.UseStaticFiles();

var store = existingStore; 

app.MapPost("/upload", async (HttpRequest req) =>
{
    try
    {
        if (!req.HasFormContentType) return Results.BadRequest("Expected multipart/form-data");

        var form = await req.ReadFormAsync();
        var file = form.Files["file"];

        if (file == null) return Results.BadRequest("Missing file field");

        var (id, filePath) = StorageService.SaveUploadedFile(file, storageDir);

        try
        {
            var result = ChordAnalyzer.AnalyzeFile(filePath, analyzerConfig);
            var timeline = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            StorageService.SaveTimelineJson(id, timeline, storageDir);

            store[id] = (filePath, timeline);

            return Results.Ok(new { id });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Analyzer error: {ex.Message}");
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/audio/{id}", (string id) =>
{
    if (!store.ContainsKey(id)) return Results.NotFound();

    var path = store[id].filePath;

    var contentType = Path.GetExtension(path).ToLower() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        _ => "application/octet-stream"
    };

    var fs = File.OpenRead(path);
    
    return Results.File(fs, contentType);
});

app.MapGet("/timeline/{id}", (string id) =>
{
    if (!store.ContainsKey(id)) return Results.NotFound();

    return Results.Content(store[id].timelineJson, "application/json");
});

// list stored audio entries (id, filename)
app.MapGet("/storage/list", () =>
{
    var items = store.Select(kv => new {
        id = kv.Key,
        file = Path.GetFileName(kv.Value.filePath),
        audioUrl = $"/audio/{kv.Key}",
        timelineUrl = $"/timeline/{kv.Key}"
    }).ToArray();

    return Results.Json(items);
});

app.Run();
