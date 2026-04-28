namespace NebuCtx.Application.Routing;

/// <summary>
/// Immutable description of an HTTP route exposed by the .NET host.
/// </summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Path">Route path.</param>
/// <param name="Handler">Logical handler name.</param>
/// <param name="File">Owning file path.</param>
/// <param name="Line">Representative line number.</param>
public sealed record RouteDescriptor(string Method, string Path, string Handler, string File, int Line);