namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Helper class for extracting metric labels from telemetry events
/// Provides safe label extraction to prevent cardinality explosion
/// </summary>
public static class MetricLabelHelper
{
    /// <summary>
    /// Extracts line_id from SourceId (e.g., "ulsan-line1" -> "line1", "asan-line2" -> "line2")
    /// Returns "unknown" if line_id cannot be extracted to prevent cardinality explosion
    /// </summary>
    /// <param name="sourceId">Source identifier (format: {factory}-line{number} or {factory}-{line})</param>
    /// <returns>Line identifier (e.g., "line1", "line2") or "unknown" if not found</returns>
    public static string ExtractLineId(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return "unknown";
        }

        // Normalize to lowercase for consistency
        var normalized = sourceId.ToLowerInvariant().Trim();

        // Pattern 1: "ulsan-line1", "asan-line2", "jeonju-line3" -> extract "line1", "line2", "line3"
        // This is the primary pattern used by the simulator
        var lineMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"-line(\d+)$");
        if (lineMatch.Success)
        {
            var lineNumber = lineMatch.Groups[1].Value;
            // Validate line number is 1-3 (or allow any number for flexibility)
            if (int.TryParse(lineNumber, out var lineNum) && lineNum >= 1 && lineNum <= 10)
            {
                return $"line{lineNumber}";
            }
        }

        // Pattern 2: "ulsan-line-1", "asan-line-2" -> extract "line-1", "line-2"
        var lineDashMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"-line-(\d+)$");
        if (lineDashMatch.Success)
        {
            var lineNumber = lineDashMatch.Groups[1].Value;
            if (int.TryParse(lineNumber, out var lineNum) && lineNum >= 1 && lineNum <= 10)
            {
                return $"line{lineNumber}";
            }
        }

        // Pattern 3: Try to extract any "line" pattern (fallback)
        var anyLineMatch = System.Text.RegularExpressions.Regex.Match(normalized, @".*line[_-]?(\d+).*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (anyLineMatch.Success)
        {
            var lineNumber = anyLineMatch.Groups[1].Value;
            if (int.TryParse(lineNumber, out var lineNum) && lineNum >= 1 && lineNum <= 10)
            {
                return $"line{lineNumber}";
            }
        }

        // If no pattern matches, return "unknown" to prevent cardinality explosion
        // This ensures we don't create unique metrics for every possible sourceId
        return "unknown";
    }

    /// <summary>
    /// Validates and sanitizes label values to prevent cardinality explosion
    /// Ensures label values are safe for Prometheus (alphanumeric, underscore, hyphen)
    /// </summary>
    /// <param name="labelValue">Raw label value</param>
    /// <param name="maxLength">Maximum length (default: 50)</param>
    /// <returns>Sanitized label value</returns>
    public static string SanitizeLabelValue(string labelValue, int maxLength = 50)
    {
        if (string.IsNullOrWhiteSpace(labelValue))
        {
            return "unknown";
        }

        // Remove invalid characters (keep only alphanumeric, underscore, hyphen)
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            labelValue,
            @"[^a-zA-Z0-9_-]",
            "_");

        // Truncate if too long
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength);
        }

        // Ensure it's not empty after sanitization
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "unknown";
        }

        return sanitized.ToLowerInvariant();
    }
}

