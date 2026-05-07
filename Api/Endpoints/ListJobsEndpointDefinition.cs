using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobSchedulerPrototype.Api;

public sealed class ListJobsEndpointDefinition : JobEndpointDefinition<
    NoRequest,
    ListJobsActionResult,
    Results<Ok<IReadOnlyCollection<JobResponse>>, StatusCodeHttpResult>>
{
    protected override JobEndpointMethod Method => JobEndpointMethod.Get;

    protected override string Pattern => "";

    protected override IJobActionRequest<ListJobsActionResult> ToActionRequest(NoRequest request)
    {
        return new ListJobsActionRequest();
    }

    protected override Results<Ok<IReadOnlyCollection<JobResponse>>, StatusCodeHttpResult> ToHttpResponse(
        ListJobsActionResult response)
    {
        if (!response.IsAuthorized)
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        IReadOnlyCollection<JobResponse> jobs = response.Jobs
            .Select(JobsApi.ToResponse)
            .ToArray();

        return TypedResults.Ok(jobs);
    }
}
