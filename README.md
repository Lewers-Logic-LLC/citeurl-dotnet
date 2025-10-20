# CiteUrl.NET

A .NET 9 library for parsing and hyperlinking legal citations. C# port of the Python [citeurl](https://github.com/raindrum/citeurl) library by Simon Raindrum Sherred.

[![NuGet Version](https://img.shields.io/nuget/v/CiteUrl.Core.svg?label=CiteUrl.Core)](https://www.nuget.org/packages/CiteUrl.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CiteUrl.Core.svg)](https://www.nuget.org/packages/CiteUrl.Core/)
[![CI Build](https://github.com/Bartomy-Labs/citeurl-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/Bartomy-Labs/citeurl-dotnet/actions/workflows/ci.yml)
[![GitHub Release](https://img.shields.io/github/v/release/Bartomy-Labs/citeurl-dotnet)](https://github.com/Bartomy-Labs/citeurl-dotnet/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Features

- **130+ Citation Formats**: US federal and state case law, statutes, regulations, constitutions, and rules
- **Thread-Safe**: Fully immutable design with lazy singleton for concurrent use
- **Memory Efficient**: Streaming enumeration (`IEnumerable<T>`) for processing large documents
- **HTML/Markdown Links**: Insert hyperlinks directly into your text
- **Authority Grouping**: Group multiple citations to the same legal source
- **Shortform Resolution**: Automatically links "Id." and shortform citations to their parent
- **YAML-Based Templates**: Extensible citation patterns via YAML configuration
- **DI-Friendly**: Optional dependency injection extensions
- **ReDoS Protection**: Built-in regex timeout protection

## Installation

```bash
dotnet add package CiteUrl.Core
```

For dependency injection support:
```bash
dotnet add package CiteUrl.Extensions.DependencyInjection
```

## Quick Start

### Find a Single Citation

```csharp
using CiteUrl.Core.Templates;

var citation = Citator.Cite("See 42 U.S.C. § 1983 for details.");

Console.WriteLine(citation?.Text);  // "42 U.S.C. § 1983"
Console.WriteLine(citation?.Url);   // "https://www.law.cornell.edu/uscode/text/42/1983"
Console.WriteLine(citation?.Name);  // "42 U.S.C. § 1983"
```

### Find All Citations

```csharp
var text = @"
    Federal law provides civil rights remedies under 42 U.S.C. § 1983,
    and attorney's fees may be awarded pursuant to § 1988.
    See also Fed. R. Civ. P. 12(b)(6).
";

foreach (var citation in Citator.ListCitations(text))
{
    Console.WriteLine($"{citation.Name} -> {citation.Url}");
}

// Output:
// 42 U.S.C. § 1983 -> https://www.law.cornell.edu/uscode/text/42/1983
// § 1988 -> https://www.law.cornell.edu/uscode/text/42/1988
// Fed. R. Civ. P. 12(b)(6) -> https://www.law.cornell.edu/rules/frcp/rule_12
```

### Insert Hyperlinks

```csharp
var text = "See 42 U.S.C. § 1983 and 29 C.F.R. § 1630.2 for details.";
var html = Citator.Default.InsertLinks(text);

Console.WriteLine(html);
// Output:
// See <a href="https://www.law.cornell.edu/uscode/text/42/1983" class="citation" title="42 U.S.C. § 1983">42 U.S.C. § 1983</a>
// and <a href="..." class="citation" title="29 C.F.R. § 1630.2">29 C.F.R. § 1630.2</a> for details.
```

### Markdown Links

```csharp
var markdown = Citator.Default.InsertLinks(text, markupFormat: "markdown");

Console.WriteLine(markdown);
// Output:
// See [42 U.S.C. § 1983](https://www.law.cornell.edu/uscode/text/42/1983)
// and [29 C.F.R. § 1630.2](...) for details.
```

### Group Citations by Authority

```csharp
var text = @"
    See 42 U.S.C. § 1983 and § 1985. Also 42 U.S.C. § 1983 again.
";

var authorities = Citator.ListAuthorities(text);

foreach (var authority in authorities)
{
    Console.WriteLine($"{authority.Name}: {authority.Citations.Count} citations");
}

// Output:
// 42 U.S.C. § 1983: 2 citations
// 42 U.S.C. § 1985: 1 citation
```

## Supported Citation Types

- **U.S. Case Law**: Supreme Court, Circuit Courts, District Courts, Bankruptcy
- **State Case Law**: All 50 states + territories
- **Federal Statutes**: U.S. Code (USC)
- **Federal Regulations**: Code of Federal Regulations (CFR)
- **State Codes**: California, New York, Texas, and 47 other states
- **Constitutions**: U.S. Constitution, state constitutions
- **Federal Rules**: FRCP, FRE, FRAP, FRCrP, etc.
- **Secondary Sources**: Law reviews, restatements

See [USAGE.md](USAGE.md) for detailed examples of each citation type.

## Advanced Usage

### Custom YAML Templates

```csharp
var yaml = @"
My Custom Citation:
  tokens:
    volume: \d+
    page: \d+
  pattern: 'Vol. {volume}, p. {page}'
  URL builder:
    parts: ['https://example.com/vol/', '{volume}', '/page/', '{page}']
";

var citator = Citator.FromYaml(yaml);
var citation = citator.Cite("See Vol. 123, p. 456");

Console.WriteLine(citation?.Url); // https://example.com/vol/123/page/456
```

### Dependency Injection

```csharp
// In Startup.cs or Program.cs
services.AddCiteUrl();

// In your service
public class MyService
{
    private readonly ICitator _citator;

    public MyService(ICitator citator)
    {
        _citator = citator;
    }

    public void ProcessLegalText(string text)
    {
        var citations = _citator.ListCitations(text);
        // ...
    }
}
```

## Documentation

- [USAGE.md](USAGE.md) - Detailed usage guide with examples
- **API Reference** - Full XML documentation available via IntelliSense in your IDE
- [Python Original](https://github.com/raindrum/citeurl) - Original Python library

## Contributing

Contributions welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Attribution

This is a C# port of the Python [citeurl](https://github.com/raindrum/citeurl) library created by [Simon Raindrum Sherred](https://github.com/raindrum). The original Python library is licensed under the MIT License.

## License

MIT License - see [LICENSE](LICENSE) for details.

Original Python library by Simon Raindrum Sherred, MIT License.

## Status

✅ **Released** - v1.0.1 available on NuGet. Core functionality complete with 121+ passing tests.
