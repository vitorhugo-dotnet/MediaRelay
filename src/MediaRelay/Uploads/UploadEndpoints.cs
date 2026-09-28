using Microsoft.AspNetCore.Mvc;

namespace MediaRelay.Uploads;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/uploads/prepare", async (
            PrepareUploadRequest request, UploadService service, HttpContext context) =>
            ToHttpResult(await service.PrepareAsync(request, context.RequestAborted)));

        endpoints.MapPost("/api/uploads/complete", async (
            CompleteUploadRequest request, UploadService service, HttpContext context) =>
            ToHttpResult(await service.CompleteAsync(request.SessionToken, context.RequestAborted)));
        return endpoints;
    }

    private static IResult ToHttpResult(UploadOperationResult result) => result.Value is not null
        ? Results.Json(result.Value, statusCode: result.StatusCode)
        : Results.Problem(statusCode: result.StatusCode, title: result.Code, detail: result.Message);
}
