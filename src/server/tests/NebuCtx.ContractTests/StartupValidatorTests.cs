namespace NebuCtx.ContractTests;

using NebuCtx.Hosting.Validation;

/// <summary>
/// Tests startup configuration validation.
/// </summary>
public class StartupValidatorTests
{
    /// <summary>
    /// Verifies loopback detection for common addresses.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("192.168.1.1", false)]
    public void IsLoopback_DetectsCorrectly(string host, bool expected)
    {
        Assert.Equal(expected, StartupValidator.IsLoopback(host));
    }

    /// <summary>
    /// Verifies that non-loopback binding without token produces an error.
    /// </summary>
    [Fact]
    public void Validate_NonLoopbackWithoutToken_ReturnsError()
    {
        var options = new Contracts.Configuration.ServerOptions
        {
            McpHost = "0.0.0.0",
            AuthToken = null,
        };

        var errors = StartupValidator.Validate(options);
        Assert.Single(errors);
        Assert.Contains("Auth token is required", errors[0]);
    }

    /// <summary>
    /// Verifies that loopback binding without token is valid.
    /// </summary>
    [Fact]
    public void Validate_LoopbackWithoutToken_IsValid()
    {
        var options = new Contracts.Configuration.ServerOptions
        {
            McpHost = "127.0.0.1",
            AuthToken = null,
        };

        var errors = StartupValidator.Validate(options);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that postgres store without DATABASE_URL produces an error.
    /// </summary>
    [Fact]
    public void Validate_PostgresWithoutDatabaseUrl_ReturnsError()
    {
        var options = new Contracts.Configuration.ServerOptions
        {
            Store = "postgres",
            DatabaseUrl = null,
        };

        var errors = StartupValidator.Validate(options);
        Assert.Contains(errors, e => e.Contains("DATABASE_URL"));
    }
}
