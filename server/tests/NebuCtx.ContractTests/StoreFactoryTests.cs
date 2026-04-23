namespace NebuCtx.ContractTests;

using NebuCtx.Contracts.Configuration;
using NebuCtx.Storage;

/// <summary>
/// Tests storage connection string normalization.
/// </summary>
public class StoreFactoryTests
{
    /// <summary>
    /// Verifies that postgres URLs are converted into Npgsql-compatible key/value strings.
    /// </summary>
    [Fact]
    public void BuildPostgresConnectionString_ConvertsUrlFormat()
    {
        var options = new ServerOptions
        {
            Store = "postgres",
            DatabaseUrl = "postgres://postgres:secret@192.168.1.135:5432/nebula",
        };

        var connectionString = StoreFactory.BuildPostgresConnectionString(options);

        Assert.Contains("Host=192.168.1.135", connectionString, StringComparison.Ordinal);
        Assert.Contains("Port=5432", connectionString, StringComparison.Ordinal);
        Assert.Contains("Username=postgres", connectionString, StringComparison.Ordinal);
        Assert.Contains("Password=secret", connectionString, StringComparison.Ordinal);
        Assert.Contains("Database=nebula", connectionString, StringComparison.Ordinal);
    }
}