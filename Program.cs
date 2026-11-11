using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using STTproject.Components;
using STTproject.Data;
using STTproject.Services;
using STTproject.Features.User.MapItem.Services;
using STTproject.Features.Login.Services;
using STTproject.Features.User.Profile.Services;
using STTproject.Features.User.Customer.Services;
using STTproject.Features.Admin.Customers.Services;
using STTproject.Features.Admin.Users.Services;
using STTproject.Features.Admin.Dashboard.Services;
using STTproject.Features.Admin.CompanyItem.Services;
using STTproject.Features.Admin.SalesInvoice.Services;
using STTproject.Features.Admin.Subdistributor.Services;
using STTproject.Features.Admin.PriceIncrease.Services;
using STTproject.Features.Admin.ImportTemplate.Services;
using STTproject.Features.Admin.ImportErrors.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure the app has an HTTPS endpoint for redirection when launched outside the https profile.
builder.WebHost.UseUrls("https://sttproject.dev.localhost:7105;http://sttproject.dev.localhost:5165");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

// =========================
// AUTHENTICATION (signed + encrypted cookie)
// =========================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = UserContextService.AuthCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        // How long a login ticket is valid. With "Remember Me" the cookie itself
        // also survives a browser restart; without it, it's a session cookie.
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;

        options.LoginPath = "/";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddDbContextFactory<EntrielContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<IMapItemService, MapItemService>();
builder.Services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<AddUomService>();
builder.Services.AddScoped<MapItemDraftService>();
builder.Services.AddScoped<DownloadTemplateService>();
builder.Services.AddScoped<ImportMapItemService>();
builder.Services.AddScoped<STTproject.Features.User.SalesInvoice.Services.DownloadTemplateService>();
builder.Services.AddScoped<STTproject.Features.User.SalesInvoice.Services.ImportSalesInvoiceService>();
builder.Services.AddHostedService<PriceIncreaseWorker>();
// Admin services
builder.Services.AddScoped<IAdminCustomerService, AdminCustomerService>();
builder.Services.AddScoped<IGeographicDataService, GeographicDataService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminCompanyItemService, AdminCompanyItemService>();
builder.Services.AddScoped<IAdminSalesInvoiceService, AdminSalesInvoiceService>();
builder.Services.AddScoped<IAdminSubDistributorService, AdminSubDistributorService>();
builder.Services.AddScoped<IAdminPriceIncreaseService, AdminPriceIncreaseService>();
builder.Services.AddScoped<IImportTemplateService, ImportTemplateService>();
builder.Services.AddScoped<IImportTemplateTestService, ImportTemplateTestService>();
builder.Services.AddScoped<IImportErrorsService, ImportErrorsService>();
builder.Services.AddScoped<IImportErrorsExportService, ImportErrorsExportService>();
builder.Services.AddScoped<ImportCustomersService>();
builder.Services.AddScoped<ImportCompanyItemsService>();
builder.Services.AddScoped<TemplateDraftService>();
builder.Services.AddScoped<ImportLogService>();
builder.Services.AddScoped<ImportFileStore>();
builder.Services.AddScoped<ImportFileService>();
builder.Services.AddScoped<IImportFileStore, DiskImportFileStore>();
builder.Services.AddScoped<IImportErrorDetailsService, ImportErrorDetailsService>();
builder.Services.AddScoped<IUserService, UserServices>();
builder.Services.AddScoped<IImportBatchDetailsService, ImportBatchDetailsService>();
builder.Services.AddScoped<IImportErrorVerificationService, ImportErrorVerificationService>();
builder.Services.AddScoped<IInvoiceFileValidator, SalesInvoiceFileValidator>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IUomSynonymService, UomSynonymService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapPost("/login", async (HttpContext httpContext, ILoginService loginService) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var requestedRole = form["role"].ToString().Trim();
    var rememberMe = string.Equals(form["rememberMe"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(form["rememberMe"].ToString(), "1", StringComparison.OrdinalIgnoreCase);

    string BuildErrorRedirect(string errorCode)
    {
        var query = $"?error={errorCode}&username={Uri.EscapeDataString(username)}";
        if (rememberMe)
        {
            query += "&rememberMe=1";
        }
        if (!string.IsNullOrWhiteSpace(requestedRole))
        {
            query += $"&role={Uri.EscapeDataString(requestedRole)}";
        }
        return $"/{query}";
    }

    var (success, user, errorCode) = await loginService.AuthenticateAsync(username, password);

    if (!success)
    {
        return Results.Redirect(BuildErrorRedirect(errorCode ?? "invalid"));
    }

    var normalizedRole = string.Equals(requestedRole, "Admin", StringComparison.OrdinalIgnoreCase)
        ? "Admin"
        : "Encoder";

    if (!string.Equals(user!.Role, normalizedRole, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Redirect(BuildErrorRedirect("role"));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, normalizedRole)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    var authProperties = new AuthenticationProperties
    {
        // true  -> persistent cookie, survives closing the browser (Remember Me)
        // false -> session cookie, gone when the browser closes
        IsPersistent = rememberMe,
        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null,
        AllowRefresh = true
    };

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

    // Clean up the old unsigned cookies from the previous login system.
    UserContextService.DeleteLegacyCookies(httpContext);

    return Results.Redirect(normalizedRole == "Admin" ? "/admin" : "/home");
});

app.MapGet("/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    UserContextService.DeleteLegacyCookies(httpContext);

    return Results.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();