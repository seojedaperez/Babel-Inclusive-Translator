using Microsoft.Extensions.Hosting;
using Serilog;
using ICH.BackgroundService.Workers;
using ICH.Shared.Configuration;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/ich-service-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("═══════════════════════════════════════════════════════════");
    Log.Information("  Inclusive Communication Hub - Background Service v1.0");
    Log.Information("═══════════════════════════════════════════════════════════");

    var builder = Host.CreateApplicationBuilder(args);

    // Windows service support
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "ICH Audio Processing Service";
    });

    builder.Services.AddSerilog();

    // Configuration
    builder.Services.Configure<AudioSettings>(builder.Configuration.GetSection("Audio"));
    builder.Services.Configure<SignalRSettings>(builder.Configuration.GetSection("SignalR"));
    builder.Services.Configure<AzureSpeechSettings>(builder.Configuration.GetSection("AzureSpeech"));
    builder.Services.Configure<AzureTranslatorSettings>(builder.Configuration.GetSection("AzureTranslator"));

    // Register Core Services
    builder.Services.AddSingleton<ICH.AudioEngine.Devices.AudioDeviceManager>();
    builder.Services.AddSingleton<ICH.AudioEngine.Processing.AudioFormatConverter>();
    
    builder.Services.AddSingleton<ICH.AIPipeline.Speech.SpeechRecognitionService>();
    builder.Services.AddSingleton<ICH.AIPipeline.Speech.SpeechSynthesisService>();
    builder.Services.AddSingleton<ICH.AIPipeline.Translation.TranslationService>();
    builder.Services.AddSingleton<ICH.AIPipeline.Pipeline.AudioPipelineOrchestrator>();

    // Register worker
    builder.Services.AddHostedService<AudioProcessingWorker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
