using JobSchedulerPrototype.Api;
using JobSchedulerPrototype.Jobs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

var jobStoreConnectionString = builder.Configuration.GetConnectionString("JobStore")
    ?? "Data Source=jobscheduler.db";

builder.Services.AddDbContextFactory<JobSchedulerDbContext>(options =>
    options.UseSqlite(jobStoreConnectionString));
builder.Services.Configure<JobWorkerOptions>(
    builder.Configuration.GetSection(JobWorkerOptions.SectionName));
builder.Services.AddSingleton<SqliteJobStore>();
builder.Services.AddSingleton<IJobStore>(services => services.GetRequiredService<SqliteJobStore>());
builder.Services.AddSingleton<IJobDefinition, SendWelcomeEmailJobDefinition>();
builder.Services.AddSingleton<IJobDefinitionRegistry, JobDefinitionRegistry>();
builder.Services.Configure<DevelopmentActorOptions>(
    builder.Configuration.GetSection(DevelopmentActorOptions.SectionName));
builder.Services.Configure<WorkerActorOptions>(
    builder.Configuration.GetSection(WorkerActorOptions.SectionName));
builder.Services.AddSingleton<IJobActorProvider, DevelopmentHeaderJobActorProvider>();
builder.Services.AddSingleton<IJobAuthorizationRuleEvaluator, JobAuthorizationRuleEvaluator>();
builder.Services.AddJobActions();
builder.Services.AddJobApiEndpoints();
builder.Services.AddSingleton<IDataAccessScopeProvider, DataAccessScopeProvider>();
builder.Services.AddSingleton<IDataAccessPolicy, JobDataAccessPolicy>();
builder.Services.AddSingleton<IDataAccessPolicyFilterBuilder, DataAccessPolicyFilterBuilder>();
builder.Services.AddSingleton<IJobLifecycleService, JobLifecycleService>();
builder.Services.AddSingleton<IJobHandler, SendWelcomeEmailJobHandler>();
builder.Services.AddSingleton<IJobHandlerRegistry, JobHandlerRegistry>();
builder.Services.AddSingleton<IJobDispatcher, JobDispatcher>();
builder.Services.AddSingleton<IJobWorker, QueuedJobWorker>();
builder.Services.AddHostedService<JobWorkerPoolHostedService>();

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
