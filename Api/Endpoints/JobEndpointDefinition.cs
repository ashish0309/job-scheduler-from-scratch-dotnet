using JobSchedulerPrototype.Jobs;

namespace JobSchedulerPrototype.Api;

public abstract class JobEndpointDefinition<THttpRequest, TActionResponse, THttpResponse> : IJobEndpointDefinition
{
    void IJobEndpointDefinition.Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var endpoint = Method switch
        {
            JobEndpointMethod.Get => group.MapActionGet<THttpRequest, TActionResponse, THttpResponse>(
                Pattern,
                ToActionRequest,
                ToHttpResponse),
            JobEndpointMethod.Post => group.MapActionPost<THttpRequest, TActionResponse, THttpResponse>(
                Pattern,
                ToActionRequest,
                ToHttpResponse),
            _ => throw new InvalidOperationException($"Unsupported endpoint method '{Method}'.")
        };

        Configure(endpoint);
    }

    protected abstract JobEndpointMethod Method { get; }

    protected abstract string Pattern { get; }

    protected abstract IJobActionRequest<TActionResponse> ToActionRequest(THttpRequest request);

    protected abstract THttpResponse ToHttpResponse(TActionResponse response);

    protected virtual void Configure(RouteHandlerBuilder endpoint)
    {
    }
}
