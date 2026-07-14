using FrislEams.Web.Components;
using FrislEams.Web.Configuration;
using FrislEams.Web.Data;
using FrislEams.Web.Middleware;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

AppDbContext.RegisterClassMaps();

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
var listenAddress = string.IsNullOrWhiteSpace(port) ? "127.0.0.1" : "0.0.0.0";
port ??= "5000";
// Bind to localhost during local runs, but keep Render-compatible wildcard binding when PORT is provided.
builder.WebHost.UseUrls($"http://{listenAddress}:{port}");

builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

var isProduction = !builder.Environment.IsDevelopment();
builder.Services.AddDistributedMemoryCache();
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.HttpOnly = HttpOnlyPolicy.Always;
});
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

builder.Services.Configure<MongoDbOptions>(builder.Configuration.GetSection(MongoDbOptions.SectionName));
var mongoOptionsAtStartup = builder.Configuration.GetSection(MongoDbOptions.SectionName).Get<MongoDbOptions>();
MongoStartupDiagnostics.LogConfiguration(mongoOptionsAtStartup);
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("MongoDb:ConnectionString is not configured.");
    }

    return new MongoClient(options.ConnectionString);
});
builder.Services.AddScoped<AppDbContext>();

builder.Services.AddScoped<TagCodeGenerator>();
builder.Services.AddScoped<AssetLifecycleService>();
builder.Services.AddScoped<RfidMonitoringService>();
builder.Services.AddScoped<FeatureHubService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<RoleGuard>();
builder.Services.AddScoped<WorkflowNotificationService>();
builder.Services.AddScoped<SystemAuditService>();
builder.Services.AddScoped<RfidTagService>();
builder.Services.AddScoped<StockVerificationService>();
builder.Services.AddScoped<AuditScanService>();
builder.Services.AddScoped<IntegrationOrchestrator>();
builder.Services.AddSingleton<IIntegrationQueue, IntegrationQueue>();
builder.Services.AddHostedService<IntegrationWorker>();
builder.Services.AddSingleton<MongoWorkbookImportService>();
builder.Services.AddScoped<MongoVendorService>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (args.Length > 0 && string.Equals(args[0], "import-workbook", StringComparison.OrdinalIgnoreCase))
{
    var workbookPath = args.Length > 1 ? args[1] : throw new InvalidOperationException("Please provide the workbook path.");
    var importer = app.Services.GetRequiredService<MongoWorkbookImportService>();
    var summary = await importer.ImportAsync(workbookPath);
    Console.WriteLine($"Imported {summary.ImportedRows} rows across {summary.SheetCount} sheets into MongoDB database '{summary.DatabaseName}' with batch id {summary.BatchId}.");
    return;
}

Console.WriteLine("FRISL EAMS startup: initializing MongoDB and seed data...");
try
{
    using var scope = app.Services.CreateScope();
    var mongoOptions = scope.ServiceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value;

    if (string.IsNullOrWhiteSpace(mongoOptions.ConnectionString))
    {
        Console.WriteLine("FRISL EAMS startup: MongoDB skipped because no connection string is configured.");
    }
    else
    {
        var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();

        await mongoClient.GetDatabase(mongoOptions.DatabaseName).RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1));
        Console.WriteLine("FRISL EAMS startup: MongoDB ping successful.");

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedData.InitializeAsync(db);
        Console.WriteLine("FRISL EAMS startup: database ready.");
    }
}
catch (Exception ex)
{
    Console.WriteLine("FRISL EAMS startup error: database initialization failed.");
    Console.WriteLine($"  Message: {ex.Message}");
    if (ex.InnerException is not null)
    {
        Console.WriteLine($"  Inner: {ex.InnerException.Message}");
    }

    Console.WriteLine(ex.StackTrace);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseRouting();
app.UseCookiePolicy();
app.UseSession();
app.UseMiddleware<PortalAccessMiddleware>();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine("FRISL EAMS startup: starting web host...");
app.Run();
