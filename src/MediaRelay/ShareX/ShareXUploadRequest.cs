namespace MediaRelay.ShareX;

public sealed record ShareXUploadRequest(string FileName, string ContentType, Stream Content);
