namespace NebuCtx.Server.Core.Validation;

using Microsoft.Extensions.Options;
using NebuCtx.Contracts.Configuration;

/// <summary>
/// Validates <see cref="ServerOptions"/> through the .NET options pipeline.
/// </summary>
public sealed class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        var errors = StartupValidator.Validate(options);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
