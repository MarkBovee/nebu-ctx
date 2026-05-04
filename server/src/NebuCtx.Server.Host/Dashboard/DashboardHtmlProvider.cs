namespace NebuCtx.Server.Host.Dashboard;

/// <summary>
/// Loads the shipped dashboard HTML that is copied beside the published host.
/// </summary>
public static class DashboardHtmlProvider
{
    private const string DashboardHtmlFileName = "dashboard.html";
    private const string DashboardLogoFileName = "logo.png";
    private const string DashboardFaviconFileName = "favicon.ico";

    /// <summary>
    /// Loads the dashboard HTML payload from the application base directory.
    /// </summary>
    /// <returns>The dashboard HTML, or a small fallback page if the asset is unavailable.</returns>
    public static string LoadHtml()
    {
        foreach (var htmlPath in BuildCandidatePaths(DashboardHtmlFileName))
        {
            if (File.Exists(htmlPath))
            {
                return File.ReadAllText(htmlPath);
            }
        }

        return """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>nebu-ctx Observatory</title>
</head>
<body>
  <h1>nebu-ctx Observatory</h1>
  <p>The server is running, but dashboard.html was not copied to the output directory.</p>
</body>
</html>
""";
    }

    /// <summary>
    /// Resolves the shipped dashboard logo path from the application base directory.
    /// </summary>
    /// <returns>The first existing logo path, or <see langword="null"/> when the asset is unavailable.</returns>
    public static string? ResolveLogoPath() => ResolveAssetPath(DashboardLogoFileName);

    /// <summary>
    /// Resolves the shipped dashboard favicon path from the application base directory.
    /// </summary>
    /// <returns>The first existing favicon path, or <see langword="null"/> when the asset is unavailable.</returns>
    public static string? ResolveFaviconPath() => ResolveAssetPath(DashboardFaviconFileName);

    /// <summary>
    /// Resolves a dashboard asset path from the application base directory.
    /// </summary>
    /// <param name="assetFileName">Dashboard asset file name to locate.</param>
    /// <returns>The first existing asset path, or <see langword="null"/> when the asset is unavailable.</returns>
    private static string? ResolveAssetPath(string assetFileName)
    {
        foreach (var assetPath in BuildCandidatePaths(assetFileName))
        {
            if (File.Exists(assetPath))
            {
                return assetPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds candidate output paths for a dashboard asset file.
    /// </summary>
    /// <param name="assetFileName">Dashboard asset file name to locate.</param>
    /// <returns>Ordered candidate paths under the application base directory.</returns>
    private static string[] BuildCandidatePaths(string assetFileName)
    {
        return
        [
            Path.Combine(AppContext.BaseDirectory, assetFileName),
            Path.Combine(AppContext.BaseDirectory, "Dashboard", assetFileName),
        ];
    }
}
