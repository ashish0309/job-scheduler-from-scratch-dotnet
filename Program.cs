using JobSchedulerPrototype.Api;
using JobSchedulerPrototype.Jobs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSingleton<IJobStore, InMemoryJobStore>();
builder.Services.AddSingleton<IJobDefinition, SendWelcomeEmailJobDefinition>();
builder.Services.AddSingleton<IJobDefinitionRegistry, JobDefinitionRegistry>();
builder.Services.AddSingleton<IJobHandler, SendWelcomeEmailJobHandler>();
builder.Services.AddSingleton<IJobHandlerRegistry, JobHandlerRegistry>();
builder.Services.AddSingleton<IJobDispatcher, JobDispatcher>();
builder.Services.AddHostedService<QueuedJobWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapJobsApi();
app.MapRazorPages();

app.Run();

public partial class Program;
