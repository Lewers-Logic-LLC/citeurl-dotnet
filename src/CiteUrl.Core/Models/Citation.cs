using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CiteUrl.Core.Templates;
using CiteUrl.Core.Tokens;

namespace CiteUrl.Core.Models;

/// <summary>
/// Represents a legal citation found in text.
/// Immutable record for thread safety.
/// </summary>
public record class Citation
{
    /// <summary>
    /// The matched citation text.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Position of the citation in the source text (start index, end index).
    /// </summary>
    public (int Start, int End) Span { get; init; }

    /// <summary>
    /// The full source text that was searched.
    /// </summary>
    public string SourceText { get; init; } = string.Empty;

    /// <summary>
    /// The template that matched this citation.
    /// </summary>
    public Template Template { get; init; } = null!;

    /// <summary>
    /// Parent citation (for shortforms and idforms).
    /// Null for longform citations.
    /// </summary>
    public Citation? Parent { get; init; }

    /// <summary>
    /// Normalized token values extracted from the match.
    /// </summary>
    public ImmutableDictionary<string, string> Tokens { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Raw token values as captured by regex (before normalization).
    /// </summary>
    public ImmutableDictionary<string, string> RawTokens { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Lazy-initialized shortform regexes compiled with parent token values.
    /// </summary>
    private Lazy<ImmutableList<Regex>>? _shortformRegexes;

    /// <summary>
    /// Lazy-initialized idform regexes compiled with parent token values.
    /// </summary>
    private Lazy<ImmutableList<Regex>>? _idformRegexes;

    /// <summary>
    /// Regex timeout for shortform/idform pattern matching.
    /// </summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The constructed URL for this citation (computed property).
    /// </summary>
    public string? Url => Template.UrlBuilder?.Build(
        Tokens.ToDictionary(kv => kv.Key, kv => kv.Value),
        RegexTimeout);

    /// <summary>
    /// The display name for this citation (computed property).
    /// </summary>
    public string? Name => Template.NameBuilder?.Build(
        Tokens.ToDictionary(kv => kv.Key, kv => kv.Value),
        RegexTimeout);

    /// <summary>
    /// Gets shortform regexes with parent token values substituted.
    /// </summary>
    public ImmutableList<Regex> ShortformRegexes
    {
        get
        {
            _shortformRegexes ??= new Lazy<ImmutableList<Regex>>(() => CompileShortformRegexes());
            return _shortformRegexes.Value;
        }
    }

    /// <summary>
    /// Gets idform regexes with parent token values substituted.
    /// </summary>
    public ImmutableList<Regex> IdformRegexes
    {
        get
        {
            _idformRegexes ??= new Lazy<ImmutableList<Regex>>(() => CompileIdformRegexes());
            return _idformRegexes.Value;
        }
    }

    /// <summary>
    /// Constructor from regex match.
    /// </summary>
    public static Citation FromMatch(
        Match match,
        Template template,
        string sourceText,
        Citation? parent = null,
        TimeSpan? regexTimeout = null)
    {
        // Extract raw tokens from match groups
        var rawTokens = ImmutableDictionary.CreateBuilder<string, string>();
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();

        foreach (var (tokenName, tokenType) in template.Tokens)
        {
            var group = match.Groups[tokenName];
            if (group.Success)
            {
                rawTokens[tokenName] = group.Value;

                // Normalize token
                var normalized = tokenType.Normalize(group.Value, regexTimeout);
                if (normalized != null)
                {
                    tokens[tokenName] = normalized;
                }
            }
        }

        // Inherit tokens from parent
        if (parent != null)
        {
            foreach (var (tokenName, value) in parent.Tokens)
            {
                // Stop inheriting after first token we captured
                if (rawTokens.ContainsKey(tokenName))
                    break;

                // Inherit both raw and normalized
                if (!rawTokens.ContainsKey(tokenName))
                {
                    rawTokens[tokenName] = parent.RawTokens.GetValueOrDefault(tokenName, value);
                    tokens[tokenName] = value;
                }
            }
        }

        return new Citation
        {
            Text = match.Value,
            Span = (match.Index, match.Index + match.Length),
            SourceText = sourceText,
            Template = template,
            Parent = parent,
            RawTokens = rawTokens.ToImmutable(),
            Tokens = tokens.ToImmutable(),
            RegexTimeout = regexTimeout ?? TimeSpan.FromSeconds(1)
        };
    }

    /// <summary>
    /// Compiles shortform regexes with {same token} replaced by parent values.
    /// </summary>
    private ImmutableList<Regex> CompileShortformRegexes()
    {
        var regexes = ImmutableList.CreateBuilder<Regex>();

        foreach (var pattern in Template.ProcessedShortformPatterns)
        {
            try
            {
                var processedPattern = SubstituteSameTokens(pattern);
                regexes.Add(new Regex(processedPattern, RegexOptions.Compiled, RegexTimeout));
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip patterns that timeout during compilation
                continue;
            }
            catch (ArgumentException)
            {
                // Skip invalid regex patterns
                continue;
            }
        }

        return regexes.ToImmutable();
    }

    /// <summary>
    /// Compiles idform regexes with {same token} replaced by parent values.
    /// Also adds basic "Id." pattern.
    /// </summary>
    private ImmutableList<Regex> CompileIdformRegexes()
    {
        var regexes = ImmutableList.CreateBuilder<Regex>();

        // Add basic id pattern
        regexes.Add(new Regex(@"(?<!\w)[Ii]d\.(?!\w)", RegexOptions.Compiled, RegexTimeout));

        foreach (var pattern in Template.ProcessedIdformPatterns)
        {
            try
            {
                var processedPattern = SubstituteSameTokens(pattern);
                regexes.Add(new Regex(processedPattern, RegexOptions.Compiled, RegexTimeout));
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }
            catch (ArgumentException)
            {
                // Skip invalid regex patterns
                continue;
            }
        }

        return regexes.ToImmutable();
    }

    /// <summary>
    /// Replaces {same token_name} with the raw token value from this citation.
    /// </summary>
    private string SubstituteSameTokens(string pattern)
    {
        var result = pattern;
        var regex = new Regex(@"\{same ([^}]+)\}");

        result = regex.Replace(result, match =>
        {
            var tokenName = match.Groups[1].Value;

            if (RawTokens.TryGetValue(tokenName, out var value))
            {
                // Escape the value for use in regex
                return Regex.Escape(value);
            }

            // If token not found, leave as-is (will likely fail to match)
            return match.Value;
        });

        return result;
    }

    /// <summary>
    /// Finds shortform citations that reference this citation.
    /// </summary>
    /// <param name="afterIndex">Start searching after this index in source text.</param>
    /// <param name="untilIndex">Stop searching at this index.</param>
    /// <returns>Enumerable of shortform citations.</returns>
    public IEnumerable<Citation> GetShortformCitations(int? afterIndex = null, int? untilIndex = null)
    {
        var startIndex = afterIndex ?? Span.End;
        var endIndex = untilIndex ?? SourceText.Length;

        if (startIndex >= endIndex || startIndex >= SourceText.Length)
            yield break;

        var searchText = SourceText.Substring(startIndex, endIndex - startIndex);

        foreach (var regex in ShortformRegexes)
        {
            foreach (Match match in regex.Matches(searchText))
            {
                var adjustedIndex = startIndex + match.Index;

                // Create citation with correct position in source text
                yield return new Citation
                {
                    Text = match.Value,
                    Span = (adjustedIndex, adjustedIndex + match.Length),
                    SourceText = SourceText,
                    Template = Template,
                    Parent = this,
                    RawTokens = ExtractTokensFromMatch(match, Template),
                    Tokens = ExtractAndNormalizeTokens(match, Template, RegexTimeout),
                    RegexTimeout = RegexTimeout
                };
            }
        }
    }

    /// <summary>
    /// Extracts raw tokens from a regex match.
    /// </summary>
    private static ImmutableDictionary<string, string> ExtractTokensFromMatch(Match match, Template template)
    {
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (tokenName, _) in template.Tokens)
        {
            var group = match.Groups[tokenName];
            if (group.Success)
            {
                tokens[tokenName] = group.Value;
            }
        }
        return tokens.ToImmutable();
    }

    /// <summary>
    /// Extracts and normalizes tokens from a regex match.
    /// </summary>
    private static ImmutableDictionary<string, string> ExtractAndNormalizeTokens(
        Match match, Template template, TimeSpan? regexTimeout)
    {
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (tokenName, tokenType) in template.Tokens)
        {
            var group = match.Groups[tokenName];
            if (group.Success)
            {
                var normalized = tokenType.Normalize(group.Value, regexTimeout);
                if (normalized != null)
                {
                    tokens[tokenName] = normalized;
                }
            }
        }
        return tokens.ToImmutable();
    }

    /// <summary>
    /// Finds the next idform citation ("Id.") that references this citation.
    /// </summary>
    public Citation? GetIdformCitation(int? untilIndex = null)
    {
        var startIndex = Span.End;
        var endIndex = untilIndex ?? SourceText.Length;

        if (startIndex >= endIndex || startIndex >= SourceText.Length)
            return null;

        var searchText = SourceText.Substring(startIndex, endIndex - startIndex);

        foreach (var regex in IdformRegexes)
        {
            var match = regex.Match(searchText);
            if (match.Success)
            {
                var adjustedIndex = startIndex + match.Index;
                // Create citation with correct position in source text
                return new Citation
                {
                    Text = match.Value,
                    Span = (adjustedIndex, adjustedIndex + match.Length),
                    SourceText = SourceText,
                    Template = Template,
                    Parent = this,
                    RawTokens = ImmutableDictionary<string, string>.Empty,
                    Tokens = ImmutableDictionary<string, string>.Empty,
                    RegexTimeout = RegexTimeout
                };
            }
        }

        return null;
    }
}
