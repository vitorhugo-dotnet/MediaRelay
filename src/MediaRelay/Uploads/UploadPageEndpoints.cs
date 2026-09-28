using Microsoft.AspNetCore.Mvc;
using MediaRelay.Options;
using Microsoft.Extensions.Options;

namespace MediaRelay.Uploads;

public static class UploadPageEndpoints
{
    public static IEndpointRouteBuilder MapUploadPageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/u/{token}", async (
            string token,
            UploadSessionService sessions,
            IWebHostEnvironment environment,
            IOptions<UploadOptions> options,
            CancellationToken ct) =>
        {
            if (await sessions.FindAsync(token, ct) is null)
                return Results.NotFound();

            var page = environment.WebRootFileProvider.GetFileInfo("upload.html");
            if (!page.Exists)
                return Results.Problem(statusCode: StatusCodes.Status404NotFound);

            await using var stream = page.CreateReadStream();
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync(ct);
            html = html.Replace("{{MAX_UPLOAD_SIZE}}", options.Value.MaxUploadSize.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        return endpoints;
    }
}
