using MediaRelay.Options;
using MediaRelay.Health;
using MediaRelay.Uploads;
using MediaRelay.Storage;

var builder = WebApplication.CreateBuilder(args);

// Preserve the public environment-variable names from the deployment contract.
var environmentConfiguration = new Dictionary<string, string?>
{
    ["Discord:Token"] = builder.Configuration["DISCORD_TOKEN"],
    ["Discord:ApplicationId"] = builder.Configuration["DISCORD_APPLICATION_ID"],
    ["Discord:AllowedGuildIds"] = builder.Configuration["DISCORD_ALLOWED_GUILD_IDS"],
    ["Minio:Endpoint"] = builder.Configuration["MINIO_ENDPOINT"],
    ["Minio:PublicEndpoint"] = builder.Configuration["MINIO_PUBLIC_ENDPOINT"],
    ["Minio:Bucket"] = builder.Configuration["MINIO_BUCKET"],
    ["Minio:AccessKey"] = builder.Configuration["MINIO_ACCESS_KEY"],
    ["Minio:SecretKey"] = builder.Configuration["MINIO_SECRET_KEY"],
    ["Minio:UseSsl"] = builder.Configuration["MINIO_USE_SSL"],
    ["PublicUrls:MediaBaseUrl"] = builder.Configuration["PUBLIC_MEDIA_BASE_URL"],
    ["PublicUrls:AppBaseUrl"] = builder.Configuration["PUBLIC_APP_BASE_URL"],
    ["Upload:ApiKey"] = builder.Configuration["UPLOAD_API_KEY"],
    ["Upload:MaxUploadSize"] = builder.Configuration["MAX_UPLOAD_SIZE"],
    ["Upload:SessionTtlMinutes"] = builder.Configuration["UPLOAD_SESSION_TTL_MINUTES"],
    ["Upload:PresignedUploadTtlMinutes"] = builder.Configuration["PRESIGNED_UPLOAD_TTL_MINUTES"]
}.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value);
builder.Configuration.AddInMemoryCollection(environmentConfiguration);

builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.ApplicationId > 0, "Discord application ID must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<MinioOptions>()
    .Bind(builder.Configuration.GetSection(MinioOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<PublicUrlOptions>()
    .Bind(builder.Configuration.GetSection(PublicUrlOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => IsAbsoluteHttpUrl(options.MediaBaseUrl) && IsAbsoluteHttpUrl(options.AppBaseUrl),
        "Public URL base values must be absolute HTTP or HTTPS URLs.")
    .ValidateOnStart();
builder.Services.AddOptions<UploadOptions>()
    .Bind(builder.Configuration.GetSection(UploadOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.MaxUploadSize > 0, "Maximum upload size must be greater than zero.")
    .Validate(options => options.SessionTtlMinutes > 0, "Upload session TTL must be greater than zero.")
    .Validate(options => options.PresignedUploadTtlMinutes > 0, "Presigned upload TTL must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<UploadSessionService>();
builder.Services.AddSingleton<MediaValidator>();
builder.Services.AddSingleton<ObjectIdGenerator>();
builder.Services.AddSingleton<IMediaStorage, MinioMediaStorage>();
builder.Services.AddSingleton<UploadService>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthEndpoints();
app.MapUploadEndpoints();
app.Run();

static bool IsAbsoluteHttpUrl(string value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

public partial class Program;
