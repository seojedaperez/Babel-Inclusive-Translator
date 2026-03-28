using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Azure;
using Azure.AI.OpenAI;
using ICH.WebPortal.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. ADD MSAL/ENTRA ID AUTHENTICATION
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        options.Audience = "fc4e2866-d347-4eb8-a4fc-c5a64aeac4b9";
    }, options =>
    {
        options.ClientId = "fc4e2866-d347-4eb8-a4fc-c5a64aeac4b9";
        options.TenantId = "abc14712-40a7-4fc5-9f3e-f23014d13c0e";
        options.Instance = "https://login.microsoftonline.com/";
    });
builder.Services.AddAuthorization();

// 2. ADD AZURE COSMOS DB
string cosmosEndpoint = "https://ich-cosmosdb-01.documents.azure.com:443/";
string cosmosKey = builder.Configuration["CosmosKey"] ?? "PLACEHOLDER_KEY_WAITING_FOR_AZURE_CLI";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseCosmos(
        cosmosEndpoint,
        cosmosKey,
        databaseName: "ICHDb"
    )
);

// 3. ADD OPENAI ASSISTANT
builder.Services.AddSingleton(new OpenAIClient(
    new Uri("https://youropenai.openai.azure.com/"), 
    new AzureKeyCredential(builder.Configuration["OpenAIKey"] ?? "DEMO_KEY")
));

builder.Services.AddRazorPages();
var app = builder.Build();

// Auto-create Cosmos DB at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // db.Database.EnsureCreated(); // Disabled until Azure key is fetched
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// ─── ENDPOINTS ───────────────────────────────────────
app.MapPost("/api/signs/sync", async (AppDbContext db, HttpContext http, SignModel dto) => {
    var oid = http.User.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
    if (oid == null) return Results.Unauthorized();
    
    var existing = await db.Signs.FirstOrDefaultAsync(s => s.UserId == oid);
    if (existing != null) {
        existing.WeightsBase64 = dto.WeightsBase64;
    } else {
        db.Signs.Add(new SignModel { UserId = oid, WeightsBase64 = dto.WeightsBase64 });
    }
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/signs", async (AppDbContext db, HttpContext http) => {
    var oid = http.User.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
    if (oid == null) return Results.Unauthorized();
    
    var sign = await db.Signs.FirstOrDefaultAsync(s => s.UserId == oid);
    if (sign == null) return Results.NotFound();
    return Results.Ok(sign);
}).RequireAuthorization();

app.MapPost("/api/transcripts", async (AppDbContext db, HttpContext http, TranscriptSession dto) => {
    var oid = http.User.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
    if (oid == null) return Results.Unauthorized();
    
    dto.UserId = oid;
    dto.CreatedAt = DateTime.UtcNow;
    db.Transcripts.Add(dto);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/api/transcripts/query", async (AppDbContext db, HttpContext http, OpenAIClient ai, string query) => {
    var oid = http.User.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
    if (oid == null) return Results.Unauthorized();
    
    var history = await db.Transcripts.Where(t => t.UserId == oid).OrderByDescending(t => t.CreatedAt).Take(10).ToListAsync();
    
    // Simplistic RAG / Context Injection
    string context = "PREVIOUS TRANSCRIPTS:\n";
    foreach (var session in history) {
        context += $"[{session.CreatedAt:yyyy-MM-dd HH:mm}] {session.Content}\n";
    }
    
    // Chat completion code goes here. Dummy response for now if Key is not set.
    return Results.Ok(new { response = $"Consultaste el modelo sobre: {query}. (La clave OpenAI no está conectada en AppSettings, pero veo tus {history.Count} transcripciones anteriores)." });
}).RequireAuthorization();

app.MapFallbackToFile("index.html");
app.Run();
