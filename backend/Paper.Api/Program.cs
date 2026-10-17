using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Paper.Api;
using static Paper.Api.ApiHelpers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Render terminates TLS and overwrites this header at its public ingress.
    // Only trust the nearest proxy's scheme, never client-supplied IP/host headers.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
var keyDirectory = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys"));
keyDirectory.Create();
builder.Services.AddDataProtection().SetApplicationName("PaperProductTool").PersistKeysToFileSystem(keyDirectory);
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
    var connection = builder.Configuration.GetConnectionString("Database");
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(connection ?? throw new InvalidOperationException("Set ConnectionStrings__Database for SQL Server."));
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
        options.UseSqlite(connection ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "paper.db")}");
    }
    else throw new InvalidOperationException("Database:Provider must be SqlServer or Sqlite.");
});
builder.Services.AddSingleton<ExcelImporter>();
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<WriteGate>();
var local = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "paper.csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "paper.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context =>
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || !await db.Users.AnyAsync(x => x.Id == id && x.Active))
        { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(); }
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireRole("Admin")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 8 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 9 * 1024 * 1024);
var app = builder.Build();
if (app.Configuration.GetValue<bool>("RENDER")) app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    if (context.Request.Path.StartsWithSegments("/api")) { context.Response.Headers.CacheControl = "no-cache, no-store"; context.Response.Headers.Pragma = "no-cache"; }
    if (!app.Environment.IsDevelopment()) context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    try { await next(); }
    catch (InputException exception) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = exception.Message }); }
    catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = "Η συνεδρία άλλαξε. Ανανέωσε τη σελίδα και δοκίμασε ξανά." }); }
    catch (DbUpdateConcurrencyException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { code = "conflict", message = "Η επίσκεψη άλλαξε από άλλον χρήστη. Άνοιξε τη νεότερη καταχώριση πριν αποθηκεύσεις." }); }
    catch (DbUpdateException exception)
    { app.Logger.LogWarning(exception, "Database constraint rejected a write"); context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { message = "Η καταχώριση συγκρούεται με αποθηκευμένα στοιχεία. Ανανέωσε τη σελίδα." }); }
    catch (Exception exception) when (exception is not OperationCanceledException)
    { app.Logger.LogError(exception, "Request failed"); context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { message = "Δεν ολοκληρώθηκε η ενέργεια. Δοκίμασε ξανά." }); }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next();
});
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapCustomerEndpoints();
app.MapAdminEndpoints();
app.MapVisitEndpoints();
app.MapVisitDeletion();
app.MapQuestionEditing();
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SurveyCatalog.UpgradeAsync(db);
    await VisitUpgrade.Run(db);
    if (app.Configuration.GetValue("Survey:InstallDefault", true))
        await SurveyCatalog.InstallAsync(db, Path.Combine(app.Environment.ContentRootPath, "Surveys", "store-visit-v1.json"));
    var config = app.Configuration;
    if (!await db.Users.AnyAsync() && config["Bootstrap:Email"] is { Length: > 0 } email && config["Bootstrap:Password"] is { Length: > 0 } password)
    {
        db.Users.Add(MakeUser(new CreateUserInput(email, "Διαχειριστής", password, "Admin"), scope.ServiceProvider.GetRequiredService<PasswordHasher<AppUser>>()));
        await db.SaveChangesAsync();
    }
    if (!await db.Branches.AnyAsync() && config["Import:Path"] is { Length: > 0 } importPath)
    {
        await using var source = new FileStream(importPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using var stream = new MemoryStream();
        await source.CopyToAsync(stream);
        stream.Position = 0;
        var summary = await scope.ServiceProvider.GetRequiredService<ExcelImporter>().ImportAsync(db, stream, config.GetValue("Import:Year", 2026));
        app.Logger.LogInformation("Imported {Count} branches for {Year}", summary.Branches, summary.Year);
    }
}
app.Run();
public partial class Program;
