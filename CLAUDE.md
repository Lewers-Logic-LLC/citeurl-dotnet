# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CiteUrl.NET is a C# port of the Python [citeurl](https://github.com/raindrum/citeurl) library for parsing and hyperlinking legal citations. The library supports 130+ citation formats including US federal and state case law, statutes, and regulations using YAML-based extensible templates.

**Status**: 🚧 Alpha - Core functionality complete, API may change before 1.0 release

**Technology Stack**: .NET 9, C# 13, xUnit, YamlDotNet, Serilog

**Python Reference**: The original Python implementation is located at `C:\Users\tlewers\source\repos\citeurl` and can be used as a reference for understanding expected behavior, YAML template formats, and bug fixes. When encountering ambiguities or bugs, always consult the Python version first.

**API Naming Conventions**: Python uses `snake_case` while C# follows .NET conventions with `PascalCase`. Key method mappings:
- Python `list_cites()` → C# `ListCitations()`
- Python `list_authorities()` → C# `ListAuthorities()`
- Python `insert_links()` → C# `InsertLinks()`
- Python `cite()` → C# `Cite()`

## Build and Test Commands

### Building
```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build src/CiteUrl.Core/CiteUrl.Core.csproj
```

**Known Build Warnings**:
- `NU1504`: Duplicate PackageReference for `Microsoft.NET.Test.Sdk` in test project - benign, can be ignored
- `NETSDK1057`: Preview .NET version warning - expected for .NET 9

### NuGet Packaging

```bash
# Build Release configuration
dotnet build --configuration Release

# Create NuGet package (outputs to standard location: src/CiteUrl.Core/bin/Release/)
dotnet pack src/CiteUrl.Core/CiteUrl.Core.csproj --configuration Release --no-build

# Package location: src/CiteUrl.Core/bin/Release/CiteUrl.Core.{version}.nupkg
```

**IMPORTANT**: Always use the standard output location (`bin/Release/`) for NuGet packages. Do NOT use custom `--output` paths unless explicitly requested by the user.

### Release Process

This project uses **Release Please** for automated version management and releases.

**Workflow**:
1. **Make changes** with conventional commits (see [Conventional Commits](#conventional-commits))
2. **Create PR** to `main` branch
3. **Merge PR** after CI passes and code review
4. **Release Please** automatically creates/updates a "Release PR" with:
   - Version bump in `.csproj` files (based on conventional commits)
   - Updated `CHANGELOG.md`
5. **Review and merge** the Release PR when ready to publish
6. **Automated deployment**:
   - GitHub Release is created
   - NuGet packages are built and published to NuGet.org
   - Release artifacts are uploaded to GitHub

**Version Bumping**:
- `feat:` commits → **minor** version (1.0.0 → 1.1.0)
- `fix:` commits → **patch** version (1.0.0 → 1.0.1)
- `BREAKING CHANGE:` footer → **major** version (1.0.0 → 2.0.0)
- Other types (docs, test, etc.) → no version bump

**Manual Releases**: Use the "Manual Release and Publish" workflow (`release.yml`) for emergency releases only. Normal releases should go through Release Please.

### Testing

```bash
# Run all tests (recommended - all bugs fixed!)
dotnet test

# Run specific test groups
dotnet test --filter "FullyQualifiedName~TokenOperationTests"
dotnet test --filter "FullyQualifiedName~TemplateTests"
dotnet test --filter "FullyQualifiedName~RealWorldTests"

# Run single test
dotnet test --filter "FullyQualifiedName~SpecificTestName"

# List all available tests
dotnet test --list-tests
```

**Test Suite Status**:
- ✅ All 127+ tests pass successfully
- ✅ Full test suite completes in ~20 seconds
- ✅ No timeouts or hangs (infinite loop bugs fixed as of commit 4ac5535)

**Test Suite Summary** (127+ tests total):
- ✅ TokenOperationTests: 17 tests - token normalization pipeline (sub, lookup, case, lpad, number_style)
- ✅ TemplateTests: 5 tests - regex compilation, token replacement, inheritance
- ✅ TokenTypeTests: 3 tests - edit pipeline execution
- ✅ StringBuilderTests: 8 tests - URL/name generation with token substitution
- ✅ InsertLinksTests: 14 tests - HTML/Markdown link insertion
- ✅ YamlLoaderTests: 5 tests - YAML deserialization with custom converters
- ✅ CitatorTests: 10 tests - citation finding and enumeration
- ✅ CitationTests: 8 tests - citation model and token handling
- ✅ AuthorityTests: 6 tests - citation grouping by core tokens
- ✅ RealWorldTests: 35 tests - integration tests with real legal citations
- ✅ ResourceLoaderTests: 9 tests - embedded YAML resource loading

## Architecture

### Core Components

**Citator** (`src/CiteUrl.Core/Templates/Citator.cs`)
- Main orchestrator for citation extraction
- Thread-safe singleton pattern using `Lazy<T>`
- Default instance loads embedded YAML templates via `ResourceLoader`
- Implements `ICitator` interface for dependency injection support
- Key methods:
  - `Cite(text)` - Find first citation
  - `ListCitations(text)` - Stream all citations (returns `IEnumerable<Citation>`)
  - `ListAuthorities(citations)` - Group citations by core tokens
  - `InsertLinks(text, markupFormat)` - Insert HTML/Markdown hyperlinks

**Template** (`src/CiteUrl.Core/Templates/Template.cs`)
- Immutable citation pattern definition with compiled regexes
- Supports template inheritance via `Template.Inherit()`
- Processes YAML patterns by replacing `{token_name}` with regex groups
- Normalizes token names (spaces → underscores) for .NET regex compatibility
- Contains:
  - `Regexes` - Narrow/precise patterns (case-sensitive)
  - `BroadRegexes` - Lenient patterns (case-insensitive)
  - `ProcessedShortformPatterns` - Compiled per-citation instance
  - `ProcessedIdformPatterns` - Compiled per-citation instance

**Citation** (`src/CiteUrl.Core/Models/Citation.cs`)
- Immutable record representing a found citation
- Lazily compiles shortform/idform regexes with parent token substitution
- Computed properties: `Url`, `Name` (built from `StringBuilder`)
- Methods:
  - `FromMatch()` - Create from regex match with token normalization
  - `GetShortformCitations()` - Find subsequent shortforms
  - `GetIdformCitation()` - Find next "Id." reference

**Authority** (`src/CiteUrl.Core/Models/Authority.cs`)
- Groups multiple citations referring to the same legal authority
- Uses core tokens (non-severable) for identity matching
- Tracks all citation instances for a given authority

### YAML Processing

**YamlLoader** (`src/CiteUrl.Core/Utilities/YamlLoader.cs`)
- Deserializes YAML templates using YamlDotNet
- Custom `TokenTypeYamlConverter` handles two token syntax forms:
  - Simple: `volume: \d+` (string → `TokenTypeYaml{Regex="\d+"}`)
  - Complex: `volume: {regex: \d+, edits: [...]}` (full object)
- Handles YAML boolean values (`yes/no/true/false/on/off`)
- Supports template inheritance via `inherit:` property
- Normalizes metadata keys (spaces → underscores)

**TokenType** (`src/CiteUrl.Core/Tokens/TokenType.cs`)
- Defines token regex pattern and normalization rules
- `IsSeverable` flag determines if token affects authority identity
- `Edits` list applies transformations (sub, lookup, case, lpad, number_style)

**StringBuilder** (`src/CiteUrl.Core/Tokens/StringBuilder.cs`)
- Builds URLs and display names from citation tokens
- Concatenates parts with token placeholder substitution
- Applies edits to transform token values
- `UrlEncode` flag for URL generation

### Embedded Resources

Default YAML templates are embedded as resources:
- `src/CiteUrl.Core/Templates/Resources/*.yaml`
- Files: `caselaw.yaml`, `general-federal-law.yaml`, `specific-federal-laws.yaml`, `state-law.yaml`, `secondary-sources.yaml`
- Loaded by `ResourceLoader.LoadAllDefaultYaml()`
- Combined into single YAML string for parsing

## Design Patterns

### Thread Safety
- **Immutable Design**: All models use `ImmutableDictionary`, `ImmutableList`, records
- **Lazy Singleton**: `Citator.Default` uses `Lazy<T>` with `isThreadSafe: true`
- **No Shared Mutable State**: Regex compilation happens at construction

### Streaming Enumeration
- `ListCitations()` returns `IEnumerable<Citation>` for memory efficiency
- Uses `yield return` to stream results instead of buffering
- Enables processing large documents without loading all citations into memory

### Dependency Injection
- `ICitator` interface abstracts citation operations
- Extension project: `CiteUrl.Extensions.DependencyInjection`
- Static convenience methods accept optional `ICitator` parameter

## YAML Template Format

```yaml
Template Name:
  tokens:
    volume: \d+              # Simple syntax (auto-wrapped in TokenTypeYaml)
    page:                     # Complex syntax
      regex: \d+
      severable: yes          # YAML boolean (yes/no/true/false/on/off)
      edits:
        - operation: sub
          pattern: ','
          replacement: ''
  patterns:
    - '{volume} Test {page}'  # {tokens} replaced with named regex groups
  broad patterns:             # Case-insensitive patterns
    - '{volume} test {page}'
  URL builder:
    parts: ['https://example.com/v', '{volume}', '/p', '{page}']
  name builder: '{volume} Test § {page}'
```

**Important**: Token names with spaces are normalized to underscores in regex groups (e.g., `{reporter id}` becomes `(?<reporter_id>...)`)

## Project Structure

```
src/
  CiteUrl.Core/                         # Main library
    Templates/
      Citator.cs, ICitator.cs           # Citation orchestration
      Template.cs                        # Pattern definitions
      Resources/*.yaml                   # Embedded templates (130+)
    Models/
      Citation.cs                        # Immutable citation record
      Authority.cs                       # Grouped citations
    Tokens/
      TokenType.cs, TokenOperation.cs   # Token normalization
      StringBuilder.cs                   # URL/name generation
    Utilities/
      YamlLoader.cs, YamlModels.cs      # YAML deserialization
      ResourceLoader.cs                  # Embedded resource access
    Exceptions/
      CiteUrlException.cs               # Custom exceptions

  CiteUrl.Extensions.DependencyInjection/ # Optional DI support

tests/
  CiteUrl.Core.Tests/                   # xUnit tests with Shouldly assertions
```

## Common Workflows

### Adding a New Citation Template
1. Edit appropriate YAML file in `src/CiteUrl.Core/Templates/Resources/`
2. Rebuild to embed updated resource: `dotnet build`
3. Test with `Citator.Default.Cite("your test text")`

### Debugging YAML Parsing Issues
- Check `YamlLoader.LoadYaml()` for deserialization errors
- Use `Citator.FromYaml(yamlString)` to test custom templates
- Verify token names don't contain spaces in regex groups (auto-normalized)
- Ensure boolean values use YAML format (`yes/no` not just `true/false`)

### Working with Citation Matching
1. **Longform matching**: `Template.Regexes` and `Template.BroadRegexes`
2. **Shortform matching**: `Citation.GetShortformCitations()` compiles patterns per-citation
3. **Idform matching**: `Citation.GetIdformCitation()` looks for "Id." patterns
4. **Overlap removal**: `Citator.RemoveOverlaps()` prefers longer matches

### Token Normalization Flow
1. Regex captures raw token value → `Citation.RawTokens`
2. `TokenType.Normalize()` applies edits → `Citation.Tokens`
3. `StringBuilder.Build()` uses normalized tokens → `Citation.Url`, `Citation.Name`

## Conventional Commits

**REQUIRED**: All commits must follow the [Conventional Commits](https://www.conventionalcommits.org/) specification for automated release management.

### Format
```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

### Types
- **feat**: New feature (minor bump: 1.0.0 → 1.1.0)
- **fix**: Bug fix (patch bump: 1.0.0 → 1.0.1)
- **docs**: Documentation only
- **test**: Adding/updating tests
- **refactor**: Code changes without bug fixes or features
- **perf**: Performance improvements
- **build**: Build system changes
- **ci**: CI/CD changes
- **chore**: Maintenance tasks

### Examples
```bash
# Feature (minor bump)
git commit -m "feat: add Bluebook 21st edition support"

# Bug fix (patch bump)
git commit -m "fix: resolve infinite loop in idform chain"

# Breaking change (major bump: 1.0.0 → 2.0.0)
git commit -m "feat!: redesign Citation API

BREAKING CHANGE: Citation is now a record type with init-only properties"

# Documentation (no bump)
git commit -m "docs: update README with new examples"
```

**Important**: PRs are squash-merged, so the PR title becomes the commit message. Ensure PR titles follow conventional commit format!

## Key Constraints

- **ReDoS Protection**: All regexes use 1-second timeout (configurable)
- **Immutability**: Never mutate `ImmutableDictionary`/`ImmutableList` - use `.Add()`, `.SetItems()` to create new instances
- **Nullable Reference Types**: Enabled (`<Nullable>enable</Nullable>`)
- **Documentation**: XML doc comments required (`GenerateDocumentationFile: true`)
- **Conventional Commits**: Required for all commits (see above)

## Supported Citation Types

- **U.S. Case Law**: Supreme Court, Circuit Courts, District Courts, Bankruptcy
- **State Case Law**: All 50 states + territories
- **Federal Statutes**: U.S. Code (USC)
- **Federal Regulations**: Code of Federal Regulations (CFR)
- **State Codes**: California, New York, Texas, and 47 other states
- **Constitutions**: U.S. Constitution, state constitutions
- **Federal Rules**: FRCP, FRE, FRAP, FRCrP, etc.
- **Secondary Sources**: Law reviews, restatements

## Recent Fixes (October 2025)

### Infinite Loop Bug Fix (Commit 4ac5535)
**Problem**: Tests were hanging/crashing with "Test host process crashed" due to infinite loop in idform citation chain resolution. The same idform citation was being found repeatedly (1500+ iterations) before crashing.

**Root Causes**:
1. Incorrect Span position calculation in `GetIdformCitation()` - was using match.Index (0) from substring instead of actual position in source text
2. No forward progress validation in idform chain iteration loop
3. No maximum iteration safety limit

**Solution**:
- Fixed `GetIdformCitation()` and `GetShortformCitations()` to correctly calculate Span positions using adjustedIndex
- Added forward progress check that breaks if `idform.Span.Start` doesn't advance
- Added maximum iteration limit (100) as safety valve
- Enhanced diagnostic logging with position tracking

**Result**: All 35 RealWorldTests now pass in 17 seconds (previously crashed after 60+ seconds)

**Files Modified**:
- `src/CiteUrl.Core/Models/Citation.cs` - Fixed Span calculation, added helper methods
- `src/CiteUrl.Core/Templates/Citator.cs` - Added progress checks and safety limits

### Template Inheritance Bug Fix (Commit efc59fe)
**Problem**: Child templates inheriting from parents without providing their own patterns received empty pattern arrays instead of inheriting parent's patterns, causing Federal Rules tests to fail.

**Root Cause**: `Template.Inherit()` used `Array.Empty<string>()` when child provided null patterns.

**Solution**:
- Added RawPatterns storage fields to Template class
- Store raw patterns before processing/compilation
- `Inherit()` now uses parent's raw patterns when child provides null
- `YamlLoader` passes null (not empty list) when YAML doesn't provide patterns

**Result**: All template inheritance tests now pass, including Federal Rules of Evidence, State Constitutions, and Federal Rules of Appellate Procedure

**Files Modified**:
- `src/CiteUrl.Core/Templates/Template.cs` - Added raw pattern fields and inheritance logic
- `src/CiteUrl.Core/Utilities/YamlLoader.cs` - Changed to pass null for empty patterns
