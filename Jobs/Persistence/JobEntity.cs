using System.ComponentModel.DataAnnotations;

namespace JobSchedulerPrototype.Jobs;

public sealed class JobEntity
{
    public const int TypeMaxLength = 200;

    public const int StatusMaxLength = 50;

    public const int FailureReasonMaxLength = 1000;

    public Guid Id { get; init; }

    [MaxLength(TypeMaxLength)]
    public string Type { get; init; } = string.Empty;

    [Required]
    public string PayloadJson { get; init; } = string.Empty;

    public JobStatus Status { get; set; }

    public Guid CurrentStateChangeId { get; set; }

    public int MaxAttempts { get; init; }

    [MaxLength(FailureReasonMaxLength)]
    public string? FailureReason { get; set; }

    public List<JobStateChangeEntity> History { get; } = [];
}
