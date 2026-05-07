using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace JobSchedulerPrototype.Api;

public static class JobActionEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapActionPost<THttpRequest, TActionResponse, THttpResponse>(
        this RouteGroupBuilder group,
        string pattern,
        Func<THttpRequest, IJobActionRequest<TActionResponse>> requestMapper,
        Func<TActionResponse, THttpResponse> responseMapper)
    {
        return group.MapPost(pattern, async (
            THttpRequest request,
            IJobActionDispatcher actions,
            CancellationToken cancellationToken) =>
        {
            var result = await actions.DispatchAsync(
                requestMapper(request),
                cancellationToken);

            return responseMapper(result);
        })
        .WithMetadata(DispatchedJobActionEndpointMetadata.Instance);
    }

    public static RouteHandlerBuilder MapActionGet<THttpRequest, TActionResponse, THttpResponse>(
        this RouteGroupBuilder group,
        string pattern,
        Func<THttpRequest, IJobActionRequest<TActionResponse>> requestMapper,
        Func<TActionResponse, THttpResponse> responseMapper)
    {
        return group.MapGet(pattern, async (
            [AsParameters] THttpRequest request,
            IJobActionDispatcher actions,
            CancellationToken cancellationToken) =>
        {
            var result = await actions.DispatchAsync(
                requestMapper(request),
                cancellationToken);

            return responseMapper(result);
        })
        .WithMetadata(DispatchedJobActionEndpointMetadata.Instance);
    }
}
