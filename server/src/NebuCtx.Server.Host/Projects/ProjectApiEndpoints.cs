namespace NebuCtx.Server.Host.Projects;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Contracts.Projects;
using NebuCtx.Server.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Maps project identity and binding endpoints for the server-aware client flow.
/// </summary>
public static class ProjectApiEndpoints
{
    /// <summary>
    /// Maps project identity endpoints used by the Rust client.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapProjectApi(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/v1/projects");

        projects.MapPost("/resolve", ResolveProjectAsync);
        projects.MapGet("/", ListProjectsAsync);
        projects.MapGet("/{projectId}/bindings", GetBindingsAsync);
        projects.MapPost("/{projectId}/bindings", BindCheckoutAsync);

        return app;
    }

    /// <summary>
    /// Resolves a canonical project from a repository fingerprint and optionally persists a workspace binding.
    /// </summary>
    private static async Task<IResult> ResolveProjectAsync(ProjectResolutionRequest request, ProjectRegistry projectRegistry, CancellationToken cancellationToken)
    {
        var suggestedSlug = ResolveSuggestedSlug(request.SuggestedSlug, request.Fingerprint);
        var project = await projectRegistry.ResolveOrCreateAsync(request.Fingerprint, suggestedSlug, request.ProjectMetadata, cancellationToken);
        if (project is null)
        {
            return Results.Conflict(new ToolCallErrorResponse { Error = "Project fingerprint is ambiguous. Explicit binding is required." });
        }

        var checkoutBound = false;
        if (request.CheckoutBinding is not null)
        {
            await projectRegistry.BindCheckoutAsync(CloneBinding(request.CheckoutBinding, project.ProjectId), cancellationToken);
            checkoutBound = true;
        }

        return Results.Ok(new ProjectResolutionResponse
        {
            Project = project,
            CheckoutBound = checkoutBound,
        });
    }

    /// <summary>
    /// Lists registered projects.
    /// </summary>
    private static async Task<IResult> ListProjectsAsync(ProjectRegistry projectRegistry, CancellationToken cancellationToken)
    {
        var projects = await projectRegistry.ListAsync(cancellationToken);
        return Results.Ok(projects);
    }

    /// <summary>
    /// Lists workspace bindings for a single project.
    /// </summary>
    private static async Task<IResult> GetBindingsAsync(string projectId, ProjectRegistry projectRegistry, CancellationToken cancellationToken)
    {
        var bindings = await projectRegistry.GetBindingsAsync(projectId, cancellationToken);
        return Results.Ok(bindings);
    }

    /// <summary>
    /// Persists or updates a checkout binding for a resolved project.
    /// </summary>
    private static async Task<IResult> BindCheckoutAsync(string projectId, CheckoutBinding request, ProjectRegistry projectRegistry, CancellationToken cancellationToken)
    {
        await projectRegistry.BindCheckoutAsync(CloneBinding(request, projectId), cancellationToken);
        return Results.Ok();
    }

    /// <summary>
    /// Resolves a tool execution context from an MCP tool call request.
    /// </summary>
    /// <param name="request">Incoming tool call request.</param>
    /// <param name="projectRegistry">Project registry service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved tool execution context for the call.</returns>
    public static async Task<ToolExecutionContext> ResolveToolExecutionContextAsync(ToolCallRequest request, ProjectRegistry projectRegistry, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ProjectId))
        {
            await projectRegistry.SyncProjectMetadataAsync(request.ProjectId, request.ProjectMetadata, cancellationToken);
            if (request.CheckoutBinding is not null)
            {
                await projectRegistry.BindCheckoutAsync(CloneBinding(request.CheckoutBinding, request.ProjectId), cancellationToken);
            }

            return CreateToolExecutionContext(request.ProjectId, request.CheckoutBinding);
        }

        if (request.RepositoryFingerprint is null)
        {
            return CreateToolExecutionContext("default", request.CheckoutBinding);
        }

        var suggestedSlug = ResolveSuggestedSlug(request.ProjectSlug, request.RepositoryFingerprint);
        var project = await projectRegistry.ResolveOrCreateAsync(request.RepositoryFingerprint, suggestedSlug, request.ProjectMetadata, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("Project fingerprint is ambiguous. Resolve the project explicitly before calling tools.");
        }

        if (request.CheckoutBinding is not null)
        {
            await projectRegistry.BindCheckoutAsync(CloneBinding(request.CheckoutBinding, project.ProjectId), cancellationToken);
        }

        return CreateToolExecutionContext(project.ProjectId, request.CheckoutBinding);
    }

    /// <summary>
    /// Clones a client checkout binding and forces the resolved project identifier.
    /// </summary>
    private static CheckoutBinding CloneBinding(CheckoutBinding source, string projectId)
    {
        return new CheckoutBinding
        {
            ProjectId = projectId,
            LocalRoot = source.LocalRoot,
            Branch = source.Branch,
            LastCommit = source.LastCommit,
            ClientLabel = source.ClientLabel,
            LastSync = source.LastSync ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Creates a tool execution context from resolved project and checkout metadata.
    /// </summary>
    private static ToolExecutionContext CreateToolExecutionContext(string projectId, CheckoutBinding? checkoutBinding)
    {
        return new ToolExecutionContext
        {
            ProjectId = projectId,
            Cwd = checkoutBinding?.LocalRoot,
            ProjectRoot = checkoutBinding?.LocalRoot,
            ActorLabel = checkoutBinding?.ClientLabel,
        };
    }

    /// <summary>
    /// Picks the slug used for project creation when the client did not provide one explicitly.
    /// </summary>
    private static string ResolveSuggestedSlug(string? suggestedSlug, RepositoryFingerprint fingerprint)
    {
        if (!string.IsNullOrWhiteSpace(suggestedSlug))
        {
            return suggestedSlug;
        }

        if (!string.IsNullOrWhiteSpace(fingerprint.RepoName))
        {
            return fingerprint.RepoName;
        }

        return "project";
    }
}
