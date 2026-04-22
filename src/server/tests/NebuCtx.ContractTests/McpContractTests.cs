namespace NebuCtx.ContractTests;

using NebuCtx.Contracts.Mcp;

/// <summary>
/// Tests that MCP contract types serialize and deserialize correctly.
/// </summary>
public class McpContractTests
{
    /// <summary>
    /// Verifies ToolCallRequest round-trips through JSON correctly.
    /// </summary>
    [Fact]
    public void ToolCallRequest_RoundTrips()
    {
        var request = new ToolCallRequest
        {
            Name = "ctx_brain",
            Arguments = new Dictionary<string, object?> { ["action"] = "status" },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ToolCallRequest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("ctx_brain", deserialized.Name);
        Assert.True(deserialized.Arguments.ContainsKey("action"));
    }

    /// <summary>
    /// Verifies ManifestResponse contains expected fields.
    /// </summary>
    [Fact]
    public void ManifestResponse_HasRequiredFields()
    {
        var manifest = new ManifestResponse
        {
            Name = "nebu-ctx",
            Version = "0.2.6",
            Tools =
            [
                new ToolDefinition
                {
                    Name = "ctx_brain",
                    Description = "Brain tool",
                    InputSchema = new Dictionary<string, object?> { ["type"] = "object" },
                },
            ],
        };

        Assert.Equal("nebu-ctx", manifest.Name);
        Assert.Single(manifest.Tools);
        Assert.Equal("ctx_brain", manifest.Tools[0].Name);
    }
}
