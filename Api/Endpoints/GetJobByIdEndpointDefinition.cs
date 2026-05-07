using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobSchedulerPrototype.Api;

public sealed class GetJobByIdEndpointDefinition : JobEndpointDefinition<
    GetJobByIdRouteRequest,
    GetJobByIdActionResult,
    Results<Ok<JobResponse>, NotFound, StatusCodeHttpResult>>
{
    protected override JobEndpointMethod Method => JobEndpointMethod.Get;

    protected override string Pattern => "/{id:guid}";

    protected override IJobActionRequest<GetJobByIdActionResult> ToActionRequest(GetJobByIdRouteRequest request)
    {
        return new GetJobByIdActionRequest(request.Id);
    }

    protected override Results<Ok<JobResponse>, NotFound, StatusCodeHttpResult> ToHttpResponse(
        GetJobByIdActionResult response)
    {
        if (!response.IsAuthorized)
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        return response.Job is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(JobsApi.ToResponse(response.Job));
    }
}

public sealed record GetJobByIdRouteRequest(Guid Id);
