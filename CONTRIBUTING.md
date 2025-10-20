# Contributing to CiteUrl.NET

Thank you for your interest in contributing to CiteUrl.NET! This document provides guidelines and information for contributors.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Commit Message Convention](#commit-message-convention)
- [Pull Request Process](#pull-request-process)
- [Testing Guidelines](#testing-guidelines)
- [Code Style](#code-style)
- [Release Process](#release-process)

## Code of Conduct

This project follows a standard code of conduct. Please be respectful and professional in all interactions.

## Getting Started

### Prerequisites

- **.NET 9 SDK** (RC2 or later)
- **Git** for version control
- **Visual Studio 2022**, **VS Code**, or **Rider** (recommended IDEs)

### Setting Up Your Development Environment

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR_USERNAME/citeurl-dotnet.git
   cd citeurl-dotnet
   ```
3. Add the upstream repository as a remote:
   ```bash
   git remote add upstream https://github.com/tlewers/citeurl-dotnet.git
   ```
4. Create a feature branch:
   ```bash
   git checkout -b feat/your-feature-name
   ```

### Building the Project

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run all tests
dotnet test
```

## Development Workflow

1. **Sync with upstream** before starting work:
   ```bash
   git fetch upstream
   git rebase upstream/main
   ```

2. **Create a feature branch** using conventional commit prefixes:
   - `feat/` for new features (e.g., `feat/add-bluebook-support`)
   - `fix/` for bug fixes (e.g., `fix/citation-parsing-error`)
   - `docs/` for documentation (e.g., `docs/update-readme`)
   - `test/` for test additions (e.g., `test/add-realworld-tests`)
   - `refactor/` for refactoring (e.g., `refactor/simplify-token-normalization`)

3. **Make your changes** following the [Code Style](#code-style) guidelines

4. **Write tests** for your changes (see [Testing Guidelines](#testing-guidelines))

5. **Commit your changes** using [Conventional Commits](#commit-message-convention)

6. **Push to your fork** and create a pull request

## Commit Message Convention

**IMPORTANT**: This project uses [Conventional Commits](https://www.conventionalcommits.org/) for automated release management. Your commit messages **must** follow this format:

### Format

```
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

### Types

- **feat**: A new feature (triggers **minor** version bump: 1.0.0 → 1.1.0)
- **fix**: A bug fix (triggers **patch** version bump: 1.0.0 → 1.0.1)
- **docs**: Documentation changes only
- **test**: Adding or updating tests
- **refactor**: Code changes that neither fix bugs nor add features
- **perf**: Performance improvements
- **build**: Changes to build system or dependencies
- **ci**: Changes to CI/CD configuration
- **chore**: Other changes that don't modify src or test files

### Breaking Changes

For breaking changes that require a **major** version bump (1.0.0 → 2.0.0), add `BREAKING CHANGE:` in the footer or append `!` after the type:

```
feat!: remove deprecated Cite() overload

BREAKING CHANGE: The Cite(text, citator) overload has been removed.
Use Cite(text, broad) instead.
```

### Examples

```bash
# Feature addition (minor bump: 1.0.0 → 1.1.0)
git commit -m "feat: add support for Bluebook 21st edition citations"

# Bug fix (patch bump: 1.0.0 → 1.0.1)
git commit -m "fix: resolve infinite loop in idform citation chain"

# Documentation update (no version bump)
git commit -m "docs: update README with new examples"

# Breaking change (major bump: 1.0.0 → 2.0.0)
git commit -m "feat!: redesign Citation API for immutability

BREAKING CHANGE: Citation class is now a record type with init-only properties"
```

### Multi-line Commits

For complex changes, provide details in the body:

```bash
git commit -m "fix: correct Span position calculation in GetIdformCitation

The Span was calculated using match.Index from the substring instead of
the actual position in the source text, causing infinite loops.

This fix adjusts the calculation to use startIndex + match.Index for
proper positioning.

Fixes #42"
```

## Pull Request Process

1. **Ensure all tests pass** locally before creating a PR:
   ```bash
   dotnet test
   ```

2. **Update documentation** if you've changed:
   - Public APIs → Update XML comments
   - Usage patterns → Update USAGE.md
   - Build/test process → Update CLAUDE.md

3. **Create a Pull Request** against the `main` branch with:
   - Clear title using conventional commit format
   - Description of what changed and why
   - Link to any related issues

4. **Required Checks**:
   - ✅ All CI tests must pass (127+ tests)
   - ✅ Code must build successfully
   - ✅ Branch must be up-to-date with main

5. **Code Review**:
   - Address review feedback promptly
   - Use conventional commits for review fixes
   - Squash fixup commits if requested

6. **Merge**:
   - PRs are squash-merged to maintain clean history
   - Your PR title becomes the commit message (use conventional format!)

## Testing Guidelines

### Test Framework

We use **xUnit** with **Shouldly** assertions for all tests.

### Test Structure

```csharp
[Fact]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    var input = "42 U.S.C. § 1983";

    // Act
    var citation = Citator.Cite(input);

    // Assert
    citation.ShouldNotBeNull();
    citation.Text.ShouldBe("42 U.S.C. § 1983");
}
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~CitatorTests"

# Run single test method
dotnet test --filter "FullyQualifiedName~Cite_FindsFirstCitation"

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Test Coverage

- All new features **must** include tests
- Bug fixes **should** include regression tests
- Aim for >85% code coverage

## Code Style

### General Guidelines

- **C# 13** language features are allowed (.NET 9)
- **Immutable design** preferred (`ImmutableDictionary`, `ImmutableList`, `record` types)
- **Nullable reference types** enabled - handle nulls explicitly
- **Thread-safe** by default (no shared mutable state)
- **XML documentation** required for public APIs

### Naming Conventions

- **Classes/Methods**: `PascalCase`
- **Private fields**: `_camelCase` with underscore
- **Local variables**: `camelCase`
- **Constants**: `PascalCase`

### Pattern Preferences

```csharp
// ✅ Preferred: Immutable records
public record Citation
{
    public string Text { get; init; }
    public (int Start, int End) Span { get; init; }
}

// ✅ Preferred: Streaming enumeration
public IEnumerable<Citation> ListCitations(string text)
{
    foreach (var citation in FindAll(text))
    {
        yield return citation;
    }
}

// ✅ Preferred: Pattern matching
if (citation is { Url: not null })
{
    InsertLink(citation.Url);
}
```

### Assertion Style

Use **Shouldly** for fluent assertions:

```csharp
// ✅ Preferred
result.ShouldNotBeNull();
result.Text.ShouldBe("expected");
citations.ShouldContain(c => c.Name == "42 U.S.C. § 1983");

// ❌ Avoid
Assert.NotNull(result);
Assert.Equal("expected", result.Text);
```

## Release Process

This project uses **Release Please** for automated releases:

1. **Make changes** with conventional commits
2. **Create PR** to `main` branch
3. **Merge PR** after approval and passing CI
4. **Release Please** automatically:
   - Creates a "Release PR" with version bump and changelog
   - Updates version in `.csproj` files
   - Updates CHANGELOG.md
5. **Review and merge** the Release PR when ready to publish
6. **Automated deployment**:
   - GitHub Release is created
   - NuGet packages are built and published
   - Release artifacts are uploaded

### Version Bumping

- `feat:` commits → **minor** version bump (1.0.0 → 1.1.0)
- `fix:` commits → **patch** version bump (1.0.0 → 1.0.1)
- `BREAKING CHANGE:` footer → **major** version bump (1.0.0 → 2.0.0)
- Other types (docs, test, etc.) → no version bump

## Questions?

- Check [CLAUDE.md](CLAUDE.md) for developer documentation
- Review [USAGE.md](USAGE.md) for usage examples
- Open an issue for bugs or feature requests

## License

By contributing to CiteUrl.NET, you agree that your contributions will be licensed under the MIT License.
