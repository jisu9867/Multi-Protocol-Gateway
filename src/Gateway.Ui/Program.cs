using Gateway.Ui.Services;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Configure HttpClient for API calls
var apiBaseUrl = builder.Configuration["GatewayApi:BaseUrl"] ?? "http://localhost:5011";
// Ensure BaseAddress doesn't have trailing slash
if (apiBaseUrl.EndsWith("/"))
{
    apiBaseUrl = apiBaseUrl.TrimEnd('/');
}

builder.Logging.AddConsole();

// Register HttpClient for TelemetryDataService
// AddHttpClient automatically registers TelemetryDataService as scoped
builder.Services.AddHttpClient<TelemetryDataService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register SignalR Service as singleton (one connection per app instance)
builder.Services.AddSingleton<SignalRService>();

// UI data services - TelemetryDataService is already registered via AddHttpClient above

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Only use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

public partial class Program { }
