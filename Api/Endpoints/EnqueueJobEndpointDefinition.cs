using JobSchedulerPrototype.Jobs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobSchedulerPrototype.Api;

public sealed class EnqueueJobEndpointDefinition : JobEndpointDefinition<
    EnqueueJobRequest,
    JobEnqueueResult,
    Results<Accepted<JobResponse>, BadRequest<JobValidationError>>>
{
    protected override JobEndpointMethod Method => JobEndpointMethod.Post;

    protected override string Pattern => "";

    protected override IJobActionRequest<JobEnqueueResult> ToActionRequest(EnqueueJobRequest request)
    {
        return new EnqueueJobActionRequest(
            request.Type,
            request.Payload,
            request.DelaySeconds);
    }

    protected override Results<Accepted<JobResponse>, BadRequest<JobValidationError>> ToHttpResponse(
        JobEnqueueResult result)
    {
        if (!result.Accepted)
        {
            return TypedResults.BadRequest(new JobValidationError(
                result.ErrorMessage ?? "Job request is invalid."));
        }

        var response = JobsApi.ToResponse(result.Job!);
        return TypedResults.Accepted(response.StatusUrl, response);
    }
}
