namespace NebuCtx.IntegrationTests;

using NebuCtx.Application;

/// <summary>
/// Unit tests for per-project aggregation and file-access tracking in TelemetryStore.
/// </summary>
public class TelemetryStoreTests
{
    private static TelemetryStore CreateStore() => new();

    [Fact]
    public void RecordToolCall_PopulatesPerProjectCounters()
    {
        var store = CreateStore();
        var ctxA = new ToolExecutionContext { ProjectId = "proj-a", ProjectRoot = "/a" };
        var ctxB = new ToolExecutionContext { ProjectId = "proj-b", ProjectRoot = "/b" };

        store.RecordToolCall("ctx_read", new Dictionary<string, object?>(), "result-a", ctxA);
        store.RecordToolCall("ctx_read", new Dictionary<string, object?>(), "result-b", ctxB);
        store.RecordToolCall("ctx_edit", new Dictionary<string, object?>(), "result-a2", ctxA);

        var snapshot = store.GetSnapshot();

        Assert.True(snapshot.PerProject.ContainsKey("proj-a"));
        Assert.True(snapshot.PerProject.ContainsKey("proj-b"));
        Assert.Equal(2, snapshot.PerProject["proj-a"].TotalToolCalls);
        Assert.Equal(1, snapshot.PerProject["proj-b"].TotalToolCalls);
        Assert.True(snapshot.PerProject["proj-a"].Commands.ContainsKey("ctx_read"));
        Assert.Equal(1, snapshot.PerProject["proj-b"].Commands["ctx_read"].Count);
    }

    [Fact]
    public void RecordToolCall_TracksFileAccess_ForFileAccessTools()
    {
        var store = CreateStore();
        var ctx = new ToolExecutionContext { ProjectId = "proj-a", ProjectRoot = "/a" };

        store.RecordToolCall("ctx_read",
            new Dictionary<string, object?> { ["path"] = "/a/src/foo.cs" }, "r", ctx);
        store.RecordToolCall("ctx_read",
            new Dictionary<string, object?> { ["path"] = "/a/src/foo.cs" }, "r", ctx);
        store.RecordToolCall("ctx_edit",
            new Dictionary<string, object?> { ["path"] = "/a/src/bar.cs" }, "r", ctx);

        var snapshot = store.GetSnapshot();
        var fa = snapshot.GetFileAccess("proj-a");

        Assert.Equal(2, fa["/a/src/foo.cs"]);
        Assert.Equal(1, fa["/a/src/bar.cs"]);
    }

    [Fact]
    public void RecordToolCall_DoesNotTrackFileAccess_ForNonFileTools()
    {
        var store = CreateStore();
        var ctx = new ToolExecutionContext { ProjectId = "proj-a" };

        store.RecordToolCall("ctx_brain",
            new Dictionary<string, object?> { ["path"] = "/a/src/foo.cs" }, "r", ctx);

        var snapshot = store.GetSnapshot();
        Assert.Empty(snapshot.GetFileAccess("proj-a"));
    }

    [Fact]
    public void RecordToolCall_DoesNotTrackFileAccess_WhenPathMissing()
    {
        var store = CreateStore();
        var ctx = new ToolExecutionContext { ProjectId = "proj-a" };

        store.RecordToolCall("ctx_read", new Dictionary<string, object?>(), "r", ctx);

        var snapshot = store.GetSnapshot();
        Assert.Empty(snapshot.GetFileAccess("proj-a"));
    }

    [Fact]
    public void IngestEvent_PopulatesPerProjectCounters()
    {
        var store = CreateStore();
        var request = new NebuCtx.Contracts.Mcp.TelemetryIngestRequest
        {
            ToolName = "ctx_read",
            TokensOriginal = 500,
            TokensSaved = 100,
        };

        store.IngestEvent(request, "proj-ingest");
        store.IngestEvent(request, "proj-ingest");

        var snapshot = store.GetSnapshot();

        Assert.True(snapshot.PerProject.ContainsKey("proj-ingest"));
        Assert.Equal(2, snapshot.PerProject["proj-ingest"].TotalToolCalls);
        Assert.True(snapshot.PerProject["proj-ingest"].Commands.ContainsKey("ctx_read"));
        Assert.Equal(2, snapshot.PerProject["proj-ingest"].Commands["ctx_read"].Count);
    }
}
