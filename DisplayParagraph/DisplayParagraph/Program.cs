using DisplayParagraph.Components;
using DisplayParagraph.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SamDataService>();
builder.Services.AddSingleton<DocsService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// Eagerly load SAM data at startup so the first request isn't slow and missing/corrupt
// files cause a fail-fast crash rather than a deferred 500.
app.Services.GetRequiredService<SamDataService>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// Honor X-Forwarded-Proto / -For from Caddy so generated URLs use https://
// and remote-IP logging is correct. Caddy runs on 127.0.0.1 (loopback), which
// the default KnownProxies/Networks already trust.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DisplayParagraph.Client._Imports).Assembly);

app.Run();
