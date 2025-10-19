using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CiteUrl.Core.Tokens;

namespace CiteUrl.Core.Templates;

/// <summary>
/// Represents a citation template with compiled regex patterns and token definitions.
/// Templates are immutable after construction for thread safety.
/// </summary>
public class Template
{
    /// <summary>
    /// The unique name of this template (e.g., "U.S. Code").
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Ordered dictionary of token names to token definitions.
    /// Order matters for pattern replacement.
    /// </summary>
    public ImmutableDictionary<string, TokenType> Tokens { get; init; } =
        ImmutableDictionary<string, TokenType>.Empty;

    /// <summary>
    /// Metadata key-value pairs (used for StringBuilder defaults, pattern replacements).
    /// </summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Raw pattern strings (before token replacement and compilation).
    /// Stored for template inheritance - child templates inherit parent's raw patterns.
    /// </summary>
    public ImmutableList<string> RawPatterns { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Raw broad pattern strings (before token replacement and compilation).
    /// Stored for template inheritance - child templates inherit parent's raw patterns.
    /// </summary>
    public ImmutableList<string> RawBroadPatterns { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Raw shortform pattern strings (before token replacement).
    /// Stored for template inheritance - child templates inherit parent's raw patterns.
    /// </summary>
    public ImmutableList<string> RawShortformPatterns { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Raw idform pattern strings (before token replacement).
    /// Stored for template inheritance - child templates inherit parent's raw patterns.
    /// </summary>
    public ImmutableList<string> RawIdformPatterns { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Compiled regex patterns for normal (narrow) matching.
    /// Case-sensitive, precise patterns.
    /// </summary>
    public ImmutableList<Regex> Regexes { get; init; } = ImmutableList<Regex>.Empty;

    /// <summary>
    /// Compiled regex patterns for broad matching.
    /// Case-insensitive, more lenient patterns for better recall.
    /// </summary>
    public ImmutableList<Regex> BroadRegexes { get; init; } = ImmutableList<Regex>.Empty;

    /// <summary>
    /// Processed shortform pattern strings with token replacements applied.
    /// Compiled per-citation instance, not globally.
    /// </summary>
    public ImmutableList<string> ProcessedShortformPatterns { get; init; } =
        ImmutableList<string>.Empty;

    /// <summary>
    /// Processed idform pattern strings with token replacements applied.
    /// Compiled per-citation instance, not globally.
    /// </summary>
    public ImmutableList<string> ProcessedIdformPatterns { get; init; } =
        ImmutableList<string>.Empty;

    /// <summary>
    /// StringBuilder for constructing URLs from citation tokens.
    /// </summary>
    public CiteUrl.Core.Tokens.StringBuilder? UrlBuilder { get; init; }

    /// <summary>
    /// StringBuilder for constructing display names from citation tokens.
    /// </summary>
    public CiteUrl.Core.Tokens.StringBuilder? NameBuilder { get; init; }

    /// <summary>
    /// Regex timeout for pattern matching (default: 1 second).
    /// </summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Constructor that processes patterns and compiles regexes.
    /// </summary>
    /// <param name="name">Template name</param>
    /// <param name="tokens">Token dictionary</param>
    /// <param name="metadata">Metadata dictionary</param>
    /// <param name="patterns">Normal pattern strings</param>
    /// <param name="broadPatterns">Broad pattern strings</param>
    /// <param name="shortformPatterns">Shortform pattern strings</param>
    /// <param name="idformPatterns">Idform pattern strings</param>
    /// <param name="urlBuilder">URL builder</param>
    /// <param name="nameBuilder">Name builder</param>
    /// <param name="regexTimeout">Regex match timeout</param>
    public Template(
        string name,
        ImmutableDictionary<string, TokenType> tokens,
        ImmutableDictionary<string, string> metadata,
        IEnumerable<string> patterns,
        IEnumerable<string> broadPatterns,
        IEnumerable<string> shortformPatterns,
        IEnumerable<string> idformPatterns,
        CiteUrl.Core.Tokens.StringBuilder? urlBuilder,
        CiteUrl.Core.Tokens.StringBuilder? nameBuilder,
        TimeSpan? regexTimeout = null)
    {
        Name = name;
        Tokens = tokens;
        Metadata = metadata;
        UrlBuilder = urlBuilder;
        NameBuilder = nameBuilder;
        RegexTimeout = regexTimeout ?? TimeSpan.FromSeconds(1);

        // Store raw patterns for inheritance
        RawPatterns = patterns.ToImmutableList();
        RawBroadPatterns = broadPatterns.ToImmutableList();
        RawShortformPatterns = shortformPatterns.ToImmutableList();
        RawIdformPatterns = idformPatterns.ToImmutableList();

        // Build replacement dictionary from metadata + tokens
        var replacements = BuildReplacementDictionary();

        // Process and compile normal patterns
        Regexes = patterns
            .Select(p => ProcessPattern(p, replacements))
            .Select(p => new Regex(p, RegexOptions.Compiled, RegexTimeout))
            .ToImmutableList();

        // Process and compile broad patterns (case-insensitive)
        BroadRegexes = broadPatterns
            .Select(p => ProcessPattern(p, replacements))
            .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout))
            .ToImmutableList();

        // Process shortform/idform patterns (NOT compiled here)
        ProcessedShortformPatterns = shortformPatterns
            .Select(p => ProcessPattern(p, replacements))
            .ToImmutableList();

        ProcessedIdformPatterns = idformPatterns
            .Select(p => ProcessPattern(p, replacements))
            .ToImmutableList();
    }

    /// <summary>
    /// Builds a dictionary of {placeholder} → replacement values
    /// from metadata and token regex patterns.
    /// </summary>
    private ImmutableDictionary<string, string> BuildReplacementDictionary()
    {
        var dict = new Dictionary<string, string>(Metadata);

        // Add token regex patterns
        foreach (var (tokenName, tokenType) in Tokens)
        {
            dict[tokenName] = tokenType.Regex;
        }

        return dict.ToImmutableDictionary();
    }

    /// <summary>
    /// Processes a pattern string by:
    /// 1. Replacing {token_name} with (?P&lt;token_name&gt;TOKEN_REGEX)(?!\w)
    /// 2. Replacing metadata placeholders
    /// 3. Adding word boundaries
    /// </summary>
    private string ProcessPattern(string pattern, ImmutableDictionary<string, string> replacements)
    {
        var result = pattern;

        // Replace {token} with (?<token>REGEX)(?!\w)
        var tokenPattern = new Regex(@"\{([^}]+)\}");
        result = tokenPattern.Replace(result, match =>
        {
            var tokenName = match.Groups[1].Value;
            // Normalize token name: replace spaces with underscores (matches Python behavior)
            var normalizedTokenName = tokenName.Replace(' ', '_');

            if (replacements.TryGetValue(normalizedTokenName, out var regex))
            {
                // Use .NET named group syntax with normalized name (no spaces allowed in regex group names)
                return $"(?<{normalizedTokenName}>{regex})(?!\\w)";
            }

            // If not found in replacements, leave as-is
            return match.Value;
        });

        // Add word boundaries at start and end
        result = $@"(?<!\w){result}(?!\w)";

        return result;
    }

    /// <summary>
    /// Creates a Template by inheriting from a parent template.
    /// Child properties override parent properties.
    /// </summary>
    public static Template Inherit(
        Template parent,
        string? name = null,
        ImmutableDictionary<string, TokenType>? tokens = null,
        ImmutableDictionary<string, string>? metadata = null,
        IEnumerable<string>? patterns = null,
        IEnumerable<string>? broadPatterns = null,
        IEnumerable<string>? shortformPatterns = null,
        IEnumerable<string>? idformPatterns = null,
        CiteUrl.Core.Tokens.StringBuilder? urlBuilder = null,
        CiteUrl.Core.Tokens.StringBuilder? nameBuilder = null)
    {
        // Merge tokens (child overrides parent)
        var mergedTokens = parent.Tokens;
        if (tokens != null)
        {
            mergedTokens = mergedTokens.SetItems(tokens);
        }

        // Merge metadata (child overrides parent)
        var mergedMetadata = parent.Metadata;
        if (metadata != null)
        {
            mergedMetadata = mergedMetadata.SetItems(metadata);
        }

        // Update StringBuilder defaults if metadata changed
        var mergedUrlBuilder = urlBuilder ?? parent.UrlBuilder;
        var mergedNameBuilder = nameBuilder ?? parent.NameBuilder;

        // Inherit parent's raw patterns when child doesn't provide them
        // This matches Python's behavior where child templates reuse parent patterns
        var mergedPatterns = patterns ?? parent.RawPatterns;
        var mergedBroadPatterns = broadPatterns ?? parent.RawBroadPatterns;
        var mergedShortformPatterns = shortformPatterns ?? parent.RawShortformPatterns;
        var mergedIdformPatterns = idformPatterns ?? parent.RawIdformPatterns;

        return new Template(
            name: name ?? parent.Name,
            tokens: mergedTokens,
            metadata: mergedMetadata,
            patterns: mergedPatterns,
            broadPatterns: mergedBroadPatterns,
            shortformPatterns: mergedShortformPatterns,
            idformPatterns: mergedIdformPatterns,
            urlBuilder: mergedUrlBuilder,
            nameBuilder: mergedNameBuilder,
            regexTimeout: parent.RegexTimeout
        );
    }
}
