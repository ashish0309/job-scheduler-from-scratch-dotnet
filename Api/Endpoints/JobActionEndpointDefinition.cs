using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Api;

public abstract class JobActionEndpointDefinition<THttpRequest, TActionResponse, THttpResponse> : IJobEndpointDefinition
{
    void IJobEndpointDefinition.Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var endpoint = group.MapActionPost<THttpRequest, TActionResponse, THttpResponse>(
            Pattern,
            ToActionRequest,
            ToHttpResponse);

        Configure(endpoint);
    }

    protected abstract string Pattern { get; }

    protected abstract IJobActionRequest<TActionResponse> ToActionRequest(THttpRequest request);

    protected abstract THttpResponse ToHttpResponse(TActionResponse response);

    protected virtual void Configure(RouteHandlerBuilder endpoint)
    {
    }
}
