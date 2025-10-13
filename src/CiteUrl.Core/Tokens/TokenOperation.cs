using System.Text.RegularExpressions;
using System.Globalization;

namespace CiteUrl.Core.Tokens;

/// <summary>
/// Defines the type of transformation operation to apply to a token value.
/// </summary>
public enum TokenOperationAction
{
    /// <summary>Regex substitution (search and replace)</summary>
    Sub,

    /// <summary>Dictionary lookup (map key to value)</summary>
    Lookup,

    /// <summary>Case transformation (upper/lower/title)</summary>
    Case,

    /// <summary>Left-pad with character to specified length</summary>
    LeftPad,

    /// <summary>Convert between number representations (roman/cardinal/ordinal/digit)</summary>
    NumberStyle
}

/// <summary>
/// Represents a single transformation operation applied to a token value.
/// Immutable record for functional pipeline processing.
/// </summary>
public record TokenOperation
{
    /// <summary>
    /// The type of transformation to apply.
    /// </summary>
    public TokenOperationAction Action { get; init; }

    /// <summary>
    /// The data required for the transformation. Type depends on Action:
    /// - Sub: (string pattern, string replacement) tuple
    /// - Lookup: Dictionary&lt;string, string&gt;
    /// - Case: string ("upper", "lower", "title")
    /// - LeftPad: (char padChar, int totalWidth) tuple
    /// - NumberStyle: (string from, string to) tuple
    /// </summary>
    public object Data { get; init; } = null!;

    /// <summary>
    /// If true, transformation failure throws exception.
    /// If false, transformation failure returns input unchanged.
    /// </summary>
    public bool IsMandatory { get; init; } = true;

    /// <summary>
    /// Optional: The token name this operation applies to (for StringBuilder edits).
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Optional: The output token name for derived values (for StringBuilder edits).
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Applies this transformation operation to the input string.
    /// </summary>
    /// <param name="input">The input string to transform.</param>
    /// <param name="regexTimeout">Optional regex timeout for Sub operations.</param>
    /// <returns>The transformed string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when IsMandatory=true and transformation fails.</exception>
    public string Apply(string input, TimeSpan? regexTimeout = null)
    {
        try
        {
            return Action switch
            {
                TokenOperationAction.Sub => ApplySub(input, regexTimeout ?? TimeSpan.FromSeconds(1)),
                TokenOperationAction.Lookup => ApplyLookup(input),
                TokenOperationAction.Case => ApplyCase(input),
                TokenOperationAction.LeftPad => ApplyLeftPad(input),
                TokenOperationAction.NumberStyle => ApplyNumberStyle(input),
                _ => throw new InvalidOperationException($"Unknown action: {Action}")
            };
        }
        catch (Exception) when (!IsMandatory)
        {
            // Optional operation failed - return input unchanged
            return input;
        }
    }

    private string ApplySub(string input, TimeSpan timeout)
    {
        var (pattern, replacement) = ((string, string))Data;
        var regex = new Regex(pattern, RegexOptions.None, timeout);
        return regex.Replace(input, replacement);
    }

    private string ApplyLookup(string input)
    {
        var dict = (Dictionary<string, string>)Data;

        // Lookup uses regex pattern matching (case-insensitive fullmatch)
        // Keys in the dictionary are regex patterns, not literal strings
        // Matches Python's pattern.fullmatch(input) behavior
        foreach (var (pattern, replacement) in dict)
        {
            try
            {
                // Add anchors for fullmatch behavior (match entire string)
                var anchoredPattern = $"^(?:{pattern})$";
                var regex = new Regex(anchoredPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                if (regex.IsMatch(input))
                {
                    return replacement;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Timeout - continue to next pattern
                continue;
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern - skip it
                continue;
            }
        }

        if (IsMandatory)
        {
            throw new InvalidOperationException($"Lookup key not found: '{input}'");
        }

        return input;
    }

    private string ApplyCase(string input)
    {
        var caseType = (string)Data;

        return caseType.ToLowerInvariant() switch
        {
            "upper" => input.ToUpperInvariant(),
            "lower" => input.ToLowerInvariant(),
            "title" => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLowerInvariant()),
            _ => throw new InvalidOperationException($"Unknown case type: {caseType}")
        };
    }

    private string ApplyLeftPad(string input)
    {
        var (padChar, totalWidth) = ((char, int))Data;
        return input.PadLeft(totalWidth, padChar);
    }

    private string ApplyNumberStyle(string input)
    {
        var (fromStyle, toStyle) = ((string, string))Data;

        // First, convert input to integer (with auto-detection if fromStyle is "detect")
        int number;
        if (fromStyle.ToLowerInvariant() == "detect")
        {
            // Auto-detect the input format (matches Python behavior)
            number = DetectAndParseNumber(input);
        }
        else
        {
            number = fromStyle.ToLowerInvariant() switch
            {
                "digit" => int.Parse(input),
                "roman" => RomanToInt(input),
                "cardinal" => CardinalToInt(input),
                "ordinal" => OrdinalToInt(input),
                _ => throw new InvalidOperationException($"Unknown from style: {fromStyle}")
            };
        }

        // Then convert integer to target style
        return toStyle.ToLowerInvariant() switch
        {
            "digit" => number.ToString(),
            "roman" => IntToRoman(number),
            "cardinal" => IntToCardinal(number),
            "ordinal" => IntToOrdinal(number),
            _ => throw new InvalidOperationException($"Unknown to style: {toStyle}")
        };
    }

    private static int DetectAndParseNumber(string input)
    {
        // Try digit first (most common)
        if (int.TryParse(input, out int digit))
            return digit;

        // Try digit with suffix (e.g., "2nd", "3rd")
        if (input.Length > 2 && int.TryParse(input[..^2], out int digitWithSuffix))
            return digitWithSuffix;

        // Try roman numeral
        try
        {
            return RomanToInt(input);
        }
        catch
        {
            // Not a roman numeral, continue
        }

        // Try cardinal
        try
        {
            return CardinalToInt(input);
        }
        catch
        {
            // Not a cardinal, continue
        }

        // Try ordinal
        try
        {
            return OrdinalToInt(input);
        }
        catch
        {
            // Not an ordinal either
        }

        throw new InvalidOperationException($"Could not detect number format for: {input}");
    }

    #region Number Style Conversion Helpers

    private static readonly Dictionary<char, int> RomanValues = new()
    {
        ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50,
        ['C'] = 100, ['D'] = 500, ['M'] = 1000
    };

    private static int RomanToInt(string roman)
    {
        roman = roman.ToUpperInvariant();
        int result = 0;
        int prevValue = 0;

        for (int i = roman.Length - 1; i >= 0; i--)
        {
            int value = RomanValues[roman[i]];
            if (value < prevValue)
                result -= value;
            else
                result += value;
            prevValue = value;
        }

        return result;
    }

    private static string IntToRoman(int number)
    {
        if (number < 1 || number > 100)
            throw new ArgumentOutOfRangeException(nameof(number), "Roman numerals only supported for 1-100");

        var romanNumerals = new (int value, string numeral)[]
        {
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };

        var result = new System.Text.StringBuilder();
        foreach (var (value, numeral) in romanNumerals)
        {
            while (number >= value)
            {
                result.Append(numeral);
                number -= value;
            }
        }
        return result.ToString();
    }

    private static readonly string[] Cardinals = new[]
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
        "twenty", "twenty-one", "twenty-two", "twenty-three", "twenty-four", "twenty-five", "twenty-six", "twenty-seven", "twenty-eight", "twenty-nine",
        "thirty", "thirty-one", "thirty-two", "thirty-three", "thirty-four", "thirty-five", "thirty-six", "thirty-seven", "thirty-eight", "thirty-nine",
        "forty", "forty-one", "forty-two", "forty-three", "forty-four", "forty-five", "forty-six", "forty-seven", "forty-eight", "forty-nine",
        "fifty", "fifty-one", "fifty-two", "fifty-three", "fifty-four", "fifty-five", "fifty-six", "fifty-seven", "fifty-eight", "fifty-nine",
        "sixty", "sixty-one", "sixty-two", "sixty-three", "sixty-four", "sixty-five", "sixty-six", "sixty-seven", "sixty-eight", "sixty-nine",
        "seventy", "seventy-one", "seventy-two", "seventy-three", "seventy-four", "seventy-five", "seventy-six", "seventy-seven", "seventy-eight", "seventy-nine",
        "eighty", "eighty-one", "eighty-two", "eighty-three", "eighty-four", "eighty-five", "eighty-six", "eighty-seven", "eighty-eight", "eighty-nine",
        "ninety", "ninety-one", "ninety-two", "ninety-three", "ninety-four", "ninety-five", "ninety-six", "ninety-seven", "ninety-eight", "ninety-nine",
        "one hundred"
    };

    private static readonly string[] Ordinals = new[]
    {
        "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth",
        "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth",
        "twentieth", "twenty-first", "twenty-second", "twenty-third", "twenty-fourth", "twenty-fifth", "twenty-sixth", "twenty-seventh", "twenty-eighth", "twenty-ninth",
        "thirtieth", "thirty-first", "thirty-second", "thirty-third", "thirty-fourth", "thirty-fifth", "thirty-sixth", "thirty-seventh", "thirty-eighth", "thirty-ninth",
        "fortieth", "forty-first", "forty-second", "forty-third", "forty-fourth", "forty-fifth", "forty-sixth", "forty-seventh", "forty-eighth", "forty-ninth",
        "fiftieth", "fifty-first", "fifty-second", "fifty-third", "fifty-fourth", "fifty-fifth", "fifty-sixth", "fifty-seventh", "fifty-eighth", "fifty-ninth",
        "sixtieth", "sixty-first", "sixty-second", "sixty-third", "sixty-fourth", "sixty-fifth", "sixty-sixth", "sixty-seventh", "sixty-eighth", "sixty-ninth",
        "seventieth", "seventy-first", "seventy-second", "seventy-third", "seventy-fourth", "seventy-fifth", "seventy-sixth", "seventy-seventh", "seventy-eighth", "seventy-ninth",
        "eightieth", "eighty-first", "eighty-second", "eighty-third", "eighty-fourth", "eighty-fifth", "eighty-sixth", "eighty-seventh", "eighty-eighth", "eighty-ninth",
        "ninetieth", "ninety-first", "ninety-second", "ninety-third", "ninety-fourth", "ninety-fifth", "ninety-sixth", "ninety-seventh", "ninety-eighth", "ninety-ninth",
        "one hundredth"
    };

    private static int CardinalToInt(string cardinal)
    {
        var index = Array.FindIndex(Cardinals, c =>
            string.Equals(c, cardinal, StringComparison.OrdinalIgnoreCase));

        if (index == -1)
            throw new InvalidOperationException($"Unknown cardinal: {cardinal}");

        return index;
    }

    private static string IntToCardinal(int number)
    {
        if (number < 0 || number > 100)
            throw new ArgumentOutOfRangeException(nameof(number), "Cardinals only supported for 0-100");

        return Cardinals[number];
    }

    private static int OrdinalToInt(string ordinal)
    {
        var index = Array.FindIndex(Ordinals, o =>
            string.Equals(o, ordinal, StringComparison.OrdinalIgnoreCase));

        if (index == -1)
            throw new InvalidOperationException($"Unknown ordinal: {ordinal}");

        return index;
    }

    private static string IntToOrdinal(int number)
    {
        if (number < 0 || number > 100)
            throw new ArgumentOutOfRangeException(nameof(number), "Ordinals only supported for 0-100");

        return Ordinals[number];
    }

    #endregion
}
