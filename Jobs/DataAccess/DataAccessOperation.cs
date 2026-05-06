namespace JobSchedulerPrototype.Jobs;

public enum DataAccessOperation
{
    Read,
    Mutate,
    ClaimJob,
    RenewLease,
    CompleteJob
}
