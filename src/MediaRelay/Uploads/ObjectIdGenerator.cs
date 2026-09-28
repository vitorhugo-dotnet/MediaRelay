using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MediaRelay.Uploads;

public sealed class ObjectIdGenerator
{
    private static readonly Regex SafeExtension = new("^\\.[a-z0-9]{1,8}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string Create(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        if (!SafeExtension.IsMatch(extension))
        {
            throw new ArgumentException("Extension must be a lowercase, dot-prefixed file extension.", nameof(extension));
        }

        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{random}{extension}";
    }
}
