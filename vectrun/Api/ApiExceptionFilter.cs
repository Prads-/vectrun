namespace vectrun.Api;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using vectrun.Models.Api;

internal class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            WorkspaceMissingException => new NotFoundObjectResult(new { missing = true }),
            PipelineAlreadyRunningException ex => new ConflictObjectResult(new { error = ex.Message }),
            _ => new BadRequestObjectResult(new { error = context.Exception.Message })
        };

        context.ExceptionHandled = true;
    }
}