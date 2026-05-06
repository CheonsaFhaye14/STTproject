using Microsoft.EntityFrameworkCore;
using STTproject.Components;
using STTproject.Models.Context;
using STTproject.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure the app has an HTTPS endpoint for redirection when launched outside the https profile.
builder.WebHost.UseUrls("https://sttproject.dev.localhost:7105;http://sttproject.dev.localhost:5165");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<SttprojectContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<IMapItemService, MapItemService>();
builder.Services.AddScoped<ISalesInvoiceService, SalesInvoiceService>();
builder.Services.AddScoped<IUserContextService, UserContextService>();

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
