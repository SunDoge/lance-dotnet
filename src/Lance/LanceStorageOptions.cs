namespace SunDoge.Lance;

/// <summary>Storage settings converted to native key/value options when opening a dataset.</summary>
public class LanceStorageOptions
{
    /// <summary>Provider-specific options not covered by typed properties.</summary>
    /// <remarks>Typed properties take precedence when the same native key is specified here.</remarks>
    public IReadOnlyDictionary<string, string>? AdditionalOptions { get; init; }

    internal virtual Dictionary<string, string> ToNativeOptions() =>
        AdditionalOptions is null ? [] : new Dictionary<string, string>(AdditionalOptions);
}

/// <summary>AWS S3 and S3-compatible object storage settings.</summary>
/// <remarks>Unset properties are omitted so native defaults and credential providers remain available.</remarks>
public sealed class S3StorageOptions : LanceStorageOptions
{
    public string? Region { get; init; }
    public Uri? Endpoint { get; init; }
    public bool? UseVirtualHostedStyle { get; init; }
    public bool? AllowHttp { get; init; }
    public string? AccessKeyId { get; init; }
    public string? SecretAccessKey { get; init; }
    public string? SessionToken { get; init; }

    internal override Dictionary<string, string> ToNativeOptions()
    {
        var options = base.ToNativeOptions();
        if (Endpoint is not null)
        {
            if (!Endpoint.IsAbsoluteUri || (Endpoint.Scheme != Uri.UriSchemeHttp && Endpoint.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("S3 endpoint must be an absolute HTTP or HTTPS URI.", nameof(Endpoint));
            options["aws_endpoint"] = Endpoint.AbsoluteUri;
        }
        AddString(options, "aws_region", Region, nameof(Region));
        AddString(options, "aws_access_key_id", AccessKeyId, nameof(AccessKeyId));
        AddString(options, "aws_secret_access_key", SecretAccessKey, nameof(SecretAccessKey));
        AddString(options, "aws_session_token", SessionToken, nameof(SessionToken));
        if (UseVirtualHostedStyle is { } virtualHosted) options["aws_virtual_hosted_style_request"] = virtualHosted ? "true" : "false";
        if (AllowHttp is { } http) options["aws_allow_http"] = http ? "true" : "false";
        return options;
    }

    private static void AddString(Dictionary<string, string> options, string key, string? value, string property)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
            throw new ArgumentException("Storage setting must be nonempty and cannot contain NUL characters.", property);
        options[key] = value;
    }
}
