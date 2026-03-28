using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using ICH.API.Hubs;
using ICH.Infrastructure.Data;
using ICH.Infrastructure.Data.Repositories;
using ICH.Infrastructure.Storage;
using ICH.Infrastructure.Auth;
using ICH.AIPipeline.Copilot;
using ICH.AIPipeline.Speech;
using ICH.AIPipeline.Translation;
using ICH.Domain.Interfaces;
using ICH.Shared.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ─── Serilog ─────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/ich-api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ─── Configuration ───────────────────────────────────────
builder.Services.Configure<AzureSpeechSettings>(builder.Configuration.GetSection("AzureSpeech"));
builder.Services.Configure<AzureTranslatorSettings>(builder.Configuration.GetSection("AzureTranslator"));
builder.Services.Configure<AzureOpenAISettings>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<AzureStorageSettings>(builder.Configuration.GetSection("AzureStorage"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<ResponsibleAISettings>(builder.Configuration.GetSection("ResponsibleAI"));

// ─── Database ────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        b => b.MigrationsAssembly("ICH.Infrastructure")));

// ─── Repositories ────────────────────────────────────────
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<ITranscriptRepository, TranscriptRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

// ─── Auth ────────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero
    };

    // SignalR JWT support
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ─── AI Services ─────────────────────────────────────────
builder.Services.AddSingleton<CopilotService>();
builder.Services.AddSingleton<TranslationService>();
builder.Services.AddSingleton<SpeechRecognitionService>();
builder.Services.AddSingleton<SpeechSynthesisService>();
builder.Services.AddSingleton<AccentConversionService>();
builder.Services.AddSingleton<SpeakerDiarizationService>();

// ─── Audio Processing ────────────────────────────────────
builder.Services.AddSingleton<ICH.AudioEngine.Processing.NoiseCancellationProcessor>();
builder.Services.AddSingleton<ICH.AudioEngine.Processing.AudioEnhancementProcessor>();
builder.Services.AddSingleton<ICH.AudioEngine.Processing.AudioFormatConverter>();

// ─── SignalR ─────────────────────────────────────────────
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB for audio chunks
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

// ─── CORS ────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });

    options.AddPolicy("WebPortal", policy =>
    {
        policy.WithOrigins("https://localhost:5173", "https://localhost:3000", "https://localhost:49938", "https://localhost:49939", "http://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// ─── Controllers & Swagger ───────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Inclusive Communication Hub API",
        Version = "v1",
        Description = "Real-time audio processing and accessibility API"
    });

    c.AddSecurityDefinition("Bearer", new()
    {
        Description = "JWT Authorization header. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ─── Health Checks ───────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();

// ─── Pipeline ────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AudioHub>("/hub/audio");
app.MapHealthChecks("/health");

// ─── Auto-migrate in development ─────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

Log.Information("Inclusive Communication Hub API starting...");
app.Run();
