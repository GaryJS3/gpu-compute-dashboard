using GpuComputeDashboard.Components;
using GpuComputeDashboard.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<WorkloadManager>();
builder.Services.AddSingleton<TelemetryMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelemetryMonitor>());

var access = new DashboardAccess(builder.Configuration.GetSection("DashboardAccess"));
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.Use(access.InvokeAsync);
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
