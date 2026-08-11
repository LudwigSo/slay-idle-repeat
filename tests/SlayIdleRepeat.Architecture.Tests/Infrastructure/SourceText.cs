using System.Text;
using System.Text.RegularExpressions;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// A C# file with every comment and string literal blanked out, so a regex rule
/// matches code and only code. Line and column offsets are preserved — removed
/// characters become spaces — so a hit still reports a usable <c>file:line</c>.
/// </summary>
/// <remarks>
/// Source greps in this suite are the belt to the IL scan's braces (<c>14</c> §8.1 says
/// "CI greps for these"). The IL scan is authoritative; the grep additionally
/// catches a banned API written in code that does not compile into the scanned
/// assembly, such as a <c>#if</c>-excluded branch.
/// </remarks>
internal sealed class SourceText
{
    private SourceText(string path, string stripped)
    {
        Path = path;
        Stripped = stripped;
    }

    /// <summary>Absolute path of the file.</summary>
    internal string Path { get; }

    /// <summary>The file's text with comments and string literals replaced by spaces.</summary>
    internal string Stripped { get; }

    /// <summary>Reads a file and blanks out its comments and string literals.</summary>
    internal static SourceText Read(string path) => new(path, Strip(File.ReadAllText(path)));

    /// <summary>Every match of <paramref name="pattern"/> in the stripped text, as <c>relative/path.cs:line</c>.</summary>
    internal IEnumerable<string> Hits(Regex pattern)
    {
        foreach (Match match in pattern.Matches(Stripped))
        {
            yield return $"{RepoLayout.Relative(Path)}:{LineOf(match.Index)} -> {match.Value.Trim()}";
        }
    }

    private int LineOf(int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < Stripped.Length; i++)
        {
            if (Stripped[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// Replaces comments and string/char literals with spaces. Interpolation holes are
    /// blanked with the rest of the literal, which is conservative: the IL scan is what
    /// catches a banned API used inside an interpolated string.
    /// </summary>
    private static string Strip(string text)
    {
        var output = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '/' && Peek(text, i + 1) == '/')
            {
                i = BlankUntilNewline(text, i, output);
            }
            else if (c == '/' && Peek(text, i + 1) == '*')
            {
                i = BlankBlockComment(text, i, output);
            }
            else if (c == '@' && Peek(text, i + 1) == '"')
            {
                i = BlankVerbatimString(text, i, output);
            }
            else if (c == '$' && Peek(text, i + 1) == '@' && Peek(text, i + 2) == '"')
            {
                Blank(output, 1);
                i = BlankVerbatimString(text, i + 1, output);
            }
            else if (c == '$' && Peek(text, i + 1) == '"')
            {
                Blank(output, 1);
                i = BlankQuotedLiteral(text, i + 1, output, '"');
            }
            else if (c == '"')
            {
                i = BlankQuotedLiteral(text, i, output, '"');
            }
            else if (c == '\'')
            {
                i = BlankQuotedLiteral(text, i, output, '\'');
            }
            else
            {
                output.Append(c);
                i++;
            }
        }

        return output.ToString();
    }

    private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

    private static void Blank(StringBuilder output, int count) => output.Append(' ', count);

    private static void Copy(StringBuilder output, char c) => output.Append(c == '\n' ? '\n' : ' ');

    private static int BlankUntilNewline(string text, int start, StringBuilder output)
    {
        var i = start;
        while (i < text.Length && text[i] != '\n')
        {
            Blank(output, 1);
            i++;
        }

        return i;
    }

    private static int BlankBlockComment(string text, int start, StringBuilder output)
    {
        var i = start;
        Blank(output, 2);
        i += 2;

        while (i < text.Length && !(text[i] == '*' && Peek(text, i + 1) == '/'))
        {
            Copy(output, text[i]);
            i++;
        }

        if (i < text.Length)
        {
            Blank(output, 2);
            i += 2;
        }

        return i;
    }

    private static int BlankQuotedLiteral(string text, int start, StringBuilder output, char quote)
    {
        var i = start;
        Blank(output, 1);
        i++;

        while (i < text.Length && text[i] != quote)
        {
            if (text[i] == '\n')
            {
                // An unterminated literal: bail out rather than swallow the rest of the file.
                return i;
            }

            if (text[i] == '\\' && i + 1 < text.Length)
            {
                Blank(output, 2);
                i += 2;
                continue;
            }

            Blank(output, 1);
            i++;
        }

        if (i < text.Length)
        {
            Blank(output, 1);
            i++;
        }

        return i;
    }

    private static int BlankVerbatimString(string text, int start, StringBuilder output)
    {
        var i = start;
        Blank(output, 2);
        i += 2;

        while (i < text.Length)
        {
            if (text[i] == '"' && Peek(text, i + 1) == '"')
            {
                Blank(output, 2);
                i += 2;
                continue;
            }

            if (text[i] == '"')
            {
                Blank(output, 1);
                return i + 1;
            }

            Copy(output, text[i]);
            i++;
        }

        return i;
    }
}
