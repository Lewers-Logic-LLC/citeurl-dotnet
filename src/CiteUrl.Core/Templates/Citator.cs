using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CiteUrl.Core.Models;
using CiteUrl.Core.Utilities;
using CiteUrl.Core.Exceptions;
using Serilog;

namespace CiteUrl.Core.Templates;

/// <summary>
/// Main citation extraction orchestrator.
/// Thread-safe immutable design with lazy singleton (Gap Decisions #2, #3).
/// </summary>
public class Citator : ICitator
{
    private static readonly ILogger? Logger = Log.Logger;
    private static readonly Lazy<Citator> _default = new(() => CreateDefault(), isThreadSafe: true);

    /// <summary>
    /// Default singleton instance with embedded YAML templates.
    /// Thread-safe lazy initialization.
    /// </summary>
    public static Citator Default => _default.Value;

    /// <summary>
    /// Immutable dictionary of templates keyed by name.
    /// Thread-safe for concurrent access (Gap Decision #2).
    /// </summary>
    public ImmutableDictionary<string, Template> Templates { get; init; }

    /// <summary>
    /// Regex timeout for pattern matching.
    /// </summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Constructor with custom templates.
    /// </summary>
    public Citator(ImmutableDictionary<string, Template> templates, TimeSpan? regexTimeout = null)
    {
        Templates = templates;
        RegexTimeout = regexTimeout ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Creates default instance with embedded YAML templates.
    /// </summary>
    private static Citator CreateDefault()
    {
        try
        {
            var yaml = ResourceLoader.LoadAllDefaultYaml();
            var templates = YamlLoader.LoadYaml(yaml, "default-templates");
            Logger?.Information("Loaded {TemplateCount} default templates", templates.Count);
            return new Citator(templates.ToImmutableDictionary());
        }
        catch (Exception ex)
        {
            Logger?.Error(ex, "Failed to load default templates");
            throw new CiteUrlYamlException("Failed to initialize default Citator", ex);
        }
    }

    /// <summary>
    /// Creates Citator from YAML string.
    /// </summary>
    public static Citator FromYaml(string yaml, string? fileName = null)
    {
        var templates = YamlLoader.LoadYaml(yaml, fileName);
        return new Citator(templates.ToImmutableDictionary());
    }

    /// <summary>
    /// Finds the first citation in the text.
    /// </summary>
    public Citation? Cite(string text, bool broad = true)
    {
        return this.ListCitations(text).FirstOrDefault();
    }

    /// <summary>
    /// Finds all citations in the text, including shortforms and idforms.
    /// Returns streaming enumerable (Gap Decision #6).
    /// </summary>
    public IEnumerable<Citation> ListCitations(string text, Regex? idBreaks = null)
    {
        var citations = new List<Citation>();

        // Find all longform citations from all templates
        foreach (var template in Templates.Values)
        {
            var regexes = template.BroadRegexes.Concat(template.Regexes);

            foreach (var regex in regexes)
            {
                try
                {
                    foreach (Match match in regex.Matches(text))
                    {
                        var citation = Citation.FromMatch(match, template, text, null, RegexTimeout);
                        citations.Add(citation);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    Logger?.Warning("Regex timeout for template {TemplateName}: {Pattern}",
                        template.Name, regex.ToString());
                }
            }
        }

        // Sort by position
        citations = citations.OrderBy(c => c.Span.Start).ToList();

        // Remove overlapping citations (prefer longer match)
        citations = RemoveOverlaps(citations);

        // Find shortforms and idforms for each citation
        foreach (var citation in citations)
        {
            // Yield longform
            yield return citation;

            // Find idform chain
            var current = citation;
            var idformChainLength = 0;
            var previousIdformPosition = -1; // Track previous position to ensure forward progress
            const int MaxIdformChainLength = 100; // Safety limit to prevent infinite loops

            while (idformChainLength < MaxIdformChainLength)
            {
                idformChainLength++;

                var nextCitationStart = citations
                    .Where(c => c.Span.Start > current.Span.End)
                    .Select(c => (int?)c.Span.Start)
                    .FirstOrDefault();

                var idform = current.GetIdformCitation(nextCitationStart);
                if (idform == null)
                {
                    break;
                }

                // Check for forward progress - prevent infinite loops from same position matches
                if (idform.Span.Start <= previousIdformPosition)
                {
                    Logger?.Warning("ListCitations:   Idform chain detected non-forward progress at position {Position}, breaking chain",
                        idform.Span.Start);
                    break;
                }
                previousIdformPosition = idform.Span.Start;

                yield return idform;
                current = idform;

                // Check for id break
                if (idBreaks != null && current.Span.End < text.Length)
                {
                    var remainingText = text.Substring(current.Span.End);
                    if (idBreaks.IsMatch(remainingText))
                    {
                        break;
                    }
                }
            }

            if (idformChainLength >= MaxIdformChainLength)
            {
                Logger?.Error("ListCitations:   Idform chain reached maximum limit of {MaxLength} iterations, likely infinite loop",
                    MaxIdformChainLength);
            }
        }
    }

    /// <summary>
    /// Removes overlapping citations, preferring longer matches.
    /// </summary>
    private List<Citation> RemoveOverlaps(List<Citation> citations)
    {
        var result = new List<Citation>();
        Citation? last = null;

        foreach (var citation in citations)
        {
            if (last == null || citation.Span.Start >= last.Span.End)
            {
                // No overlap
                result.Add(citation);
                last = citation;
            }
            else if (citation.Text.Length > last.Text.Length)
            {
                // Current is longer, replace last
                result[result.Count - 1] = citation;
                last = citation;
            }
            // else: last is longer or equal, keep it
        }

        return result;
    }

    /// <summary>
    /// Groups citations by their core tokens, creating Authority records.
    /// Returns streaming enumerable for memory efficiency.
    /// </summary>
    public IEnumerable<Authority> ListAuthorities(IEnumerable<Citation> citations,
        IEnumerable<string>? ignoredTokens = null, bool sortByCites = true)
    {
        var ignored = ignoredTokens?.ToHashSet() ?? new HashSet<string>();
        var authorities = new List<Authority>();

        foreach (var citation in citations)
        {
            var coreTokens = citation.Tokens
                .Where(kv => !ignored.Contains(kv.Key))
                .Where(kv => !citation.Template.Tokens[kv.Key].IsSeverable ||
                             citation.Parent == null)
                .ToImmutableDictionary();

            var existing = authorities.FirstOrDefault(a =>
                a.Template.Name == citation.Template.Name &&
                a.Tokens.SequenceEqual(coreTokens));

            if (existing != null)
            {
                existing.Citations.Add(citation);
            }
            else
            {
                authorities.Add(new Authority
                {
                    Template = citation.Template,
                    Tokens = coreTokens,
                    Citations = new List<Citation> { citation },
                    IgnoredTokens = ignored.ToArray()
                });
            }
        }

        if (sortByCites)
        {
            authorities = authorities.OrderByDescending(a => a.Citations.Count).ToList();
        }

        return authorities;
    }

    /// <summary>
    /// Inserts hyperlinks for all citations in the text.
    /// Supports HTML and Markdown formats with configurable attributes.
    /// </summary>
    /// <param name="text">Text containing citations.</param>
    /// <param name="attrs">Optional HTML attributes (default: class="citation").</param>
    /// <param name="addTitle">If true, adds title attribute with citation name.</param>
    /// <param name="urlOptional">If true, includes citations without URLs.</param>
    /// <param name="redundantLinks">If true, links repeated URLs.</param>
    /// <param name="idBreaks">Optional regex to break idform chains.</param>
    /// <param name="ignoreMarkup">If true, preserves inline markup (future enhancement).</param>
    /// <param name="markupFormat">Format: "html" or "markdown".</param>
    /// <returns>Text with hyperlinks inserted.</returns>
    public string InsertLinks(
        string text,
        Dictionary<string, string>? attrs = null,
        bool addTitle = true,
        bool urlOptional = false,
        bool redundantLinks = true,
        Regex? idBreaks = null,
        bool ignoreMarkup = true,
        string markupFormat = "html")
    {
        var citations = ListCitations(text, idBreaks).ToList();
        if (citations.Count == 0)
            return text;

        var result = new System.Text.StringBuilder(text);
        var offset = 0;
        string? lastUrl = null;

        foreach (var citation in citations)
        {
            // Skip citations without URLs unless urlOptional is true
            if (citation.Url == null && !urlOptional)
                continue;

            // Skip redundant links if not allowed
            if (!redundantLinks && citation.Url == lastUrl)
                continue;

            // Build link based on format
            var link = markupFormat.ToLower() == "markdown"
                ? BuildMarkdownLink(citation)
                : BuildHtmlLink(citation, attrs, addTitle);

            // Replace citation text with link, tracking offset
            var start = citation.Span.Start + offset;
            result.Remove(start, citation.Text.Length);
            result.Insert(start, link);
            offset += link.Length - citation.Text.Length;
            lastUrl = citation.Url;
        }

        return result.ToString();
    }

    /// <summary>
    /// Builds an HTML link for a citation.
    /// </summary>
    private string BuildHtmlLink(Citation citation, Dictionary<string, string>? attrs, bool addTitle)
    {
        // Build attributes string
        var attrList = new List<string>();

        if (attrs != null && attrs.Count > 0)
        {
            foreach (var kv in attrs)
            {
                attrList.Add($"{kv.Key}=\"{kv.Value}\"");
            }
        }
        else
        {
            attrList.Add("class=\"citation\"");
        }

        // Add title attribute if requested and name is available
        if (addTitle && citation.Name != null)
        {
            attrList.Add($"title=\"{EscapeHtml(citation.Name)}\"");
        }

        var attrStr = string.Join(" ", attrList);
        return $"<a href=\"{citation.Url}\" {attrStr}>{EscapeHtml(citation.Text)}</a>";
    }

    /// <summary>
    /// Builds a Markdown link for a citation.
    /// </summary>
    private string BuildMarkdownLink(Citation citation)
    {
        return $"[{citation.Text}]({citation.Url})";
    }

    /// <summary>
    /// Escapes HTML special characters.
    /// </summary>
    private string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    /// <summary>
    /// Static convenience method for finding first citation.
    /// Uses Default instance if citator not provided.
    /// </summary>
    public static Citation? Cite(string text, bool broad = true, ICitator? citator = null)
    {
        return (citator ?? Default).Cite(text, broad);
    }

    /// <summary>
    /// Static convenience method for listing all citations.
    /// Uses Default instance if citator not provided.
    /// </summary>
    public static IEnumerable<Citation> ListCitations(
        string text,
        ICitator? citator = null,
        Regex? idBreaks = null)
    {
        return (citator ?? Default).ListCitations(text, idBreaks);
    }

    /// <summary>
    /// Static convenience method for listing all authorities from text.
    /// Uses Default instance if citator not provided.
    /// </summary>
    public static IEnumerable<Authority> ListAuthorities(string text, ICitator? citator = null,
        IEnumerable<string>? ignoredTokens = null)
    {
        var c = citator ?? Default;
        var citations = c.ListCitations(text);
        return c.ListAuthorities(citations, ignoredTokens);
    }
}
