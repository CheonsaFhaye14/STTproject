using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using STTproject.Components;
using STTproject.Data;
using STTproject.Services;
using STTproject.Features.User.MapItem.Services;
using STTproject.Features.Login.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure the app has an HTTPS endpoint for redirection when launched outside the https profile.
builder.WebHost.UseUrls("https://sttproject.dev.localhost:7105;http://sttproject.dev.localhost:5165");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContextFactory<SttprojectContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<IMapItemService, MapItemService>();
builder.Services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<AddUomService>();
builder.Services.AddScoped<MapItemDraftService>();
builder.Services.AddScoped<DownloadTemplateService>();

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
app.UseAntiforgery();

app.MapPost("/login", async (HttpContext httpContext, ILoginService loginService) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var requestedRole = form["role"].ToString().Trim();
    var rememberMe = string.Equals(form["rememberMe"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(form["rememberMe"].ToString(), "1", StringComparison.OrdinalIgnoreCase);

    var (success, user, errorCode) = await loginService.AuthenticateAsync(username, password);

    if (!success)
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
        return Results.Redirect($"/{query}");
    }

    var normalizedRole = string.Equals(requestedRole, "Admin", StringComparison.OrdinalIgnoreCase)
        ? "Admin"
        : "Encoder";

    if (!string.Equals(user!.Role, normalizedRole, StringComparison.OrdinalIgnoreCase))
    {
        var query = $"?error=role&username={Uri.EscapeDataString(username)}";
        if (rememberMe)
        {
            query += "&rememberMe=1";
        }
        if (!string.IsNullOrWhiteSpace(requestedRole))
        {
            query += $"&role={Uri.EscapeDataString(requestedRole)}";
        }
        return Results.Redirect($"/{query}");
    }

    var cookieOptions = new CookieOptions
    {
        HttpOnly = true,
        Secure = httpContext.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    };

    if (rememberMe)
    {
        cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(7);
    }

    httpContext.Response.Cookies.Append(UserContextService.UserIdCookieName, user!.UserId.ToString(), cookieOptions);

    return Results.Redirect(normalizedRole == "Admin" ? "/admin" : "/home");
});

app.MapGet("/logout", (HttpContext httpContext) =>
{
    httpContext.Response.Cookies.Delete(UserContextService.UserIdCookieName, new CookieOptions
    {
        HttpOnly = true,
        Secure = httpContext.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    });

    return Results.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
