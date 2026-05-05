using System.ComponentModel.DataAnnotations;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobStateChangeEntity
{
    public const int StatusMaxLength = 50;

    public const int ReasonMaxLength = 1000;

    public Guid Id { get; init; }

    public Guid JobId { get; set; }

    public JobEntity? Job { get; set; }

    public JobStatus Status { get; init; }

    public DateTimeOffset ChangedAt { get; init; }

    [MaxLength(ReasonMaxLength)]
    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset? ScheduledAt { get; init; }
}
