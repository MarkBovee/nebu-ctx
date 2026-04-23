namespace NebuCtx.Dashboard;

/// <summary>
/// Loads the shipped dashboard HTML that is copied beside the published host.
/// </summary>
public static class DashboardHtmlProvider
{
    private const string DashboardHtmlFileName = "dashboard.html";

    /// <summary>
    /// Loads the dashboard HTML payload from the application base directory.
    /// </summary>
    /// <returns>The dashboard HTML, or a small fallback page if the asset is unavailable.</returns>
    public static string LoadHtml()
    {
        var htmlPath = Path.Combine(AppContext.BaseDirectory, DashboardHtmlFileName);
        if (File.Exists(htmlPath))
        {
            return File.ReadAllText(htmlPath);
        }

        return """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>nebu-ctx dashboard</title>
</head>
<body>
  <h1>nebu-ctx dashboard asset missing</h1>
  <p>The server is running, but dashboard.html was not copied to the output directory.</p>
</body>
</html>
""";
    }
}