namespace JobSchedulerPrototype.Api;

public sealed class DispatchedJobActionEndpointMetadata
{
    public static DispatchedJobActionEndpointMetadata Instance { get; } = new();

    private DispatchedJobActionEndpointMetadata()
    {
    }
}
