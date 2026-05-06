namespace JobSchedulerPrototype.Jobs;

public sealed record DataAccessPolicyContext(
    JobActor Actor,
    DataAccessScope Scope,
    DataAccessOperation Operation) : IDataAccessPolicyContext;
