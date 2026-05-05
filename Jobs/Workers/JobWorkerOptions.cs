namespace JobSchedulerPrototype.Jobs;

public sealed class JobWorkerOptions
{
    public const string SectionName = "JobScheduler:Workers";

    public int WorkerCount { get; set; } = 1;

    public int PollIntervalSeconds { get; set; } = 1;

    public int SimulatedWorkDurationSeconds { get; set; } = 2;

    public int LeaseSeconds { get; set; } = 60;

    public int ValidWorkerCount => Math.Max(1, WorkerCount);

    public TimeSpan ValidPollInterval => TimeSpan.FromSeconds(Math.Max(0, PollIntervalSeconds));

    public TimeSpan ValidSimulatedWorkDuration =>
        TimeSpan.FromSeconds(Math.Max(0, SimulatedWorkDurationSeconds));

    public TimeSpan ValidLeaseDuration => TimeSpan.FromSeconds(Math.Max(1, LeaseSeconds));
}
