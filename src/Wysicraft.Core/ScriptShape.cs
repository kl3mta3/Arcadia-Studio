using System.Text.RegularExpressions;
namespace Wysicraft.Core;

/// <summary>Quick looks at a script's text, without running it: enough to give advice, not a JavaScript parser.</summary>
public static class ScriptShape
{
    static readonly Regex Ctx = new(@"(?<![\w$.])(ctx|ui)\s*\.", RegexOptions.Compiled);
    static readonly Regex Save = new(@"(?<![\w$])ctx\s*\.\s*save\b", RegexOptions.Compiled);

    /// <summary>The first line that uses ctx or ui outside every block (so outside any function), or 0. With kept script
    /// state that code runs once, on the first event, instead of on every event.</summary>
    public static int TopLevelCtxLine(string source)
    {
        var code = Blank(source); int depth = 0, line = 1, at = 0;
        foreach (Match m in Ctx.Matches(code))
        {
            for (; at < m.Index; at++) { char c = code[at]; if (c == '{') depth++; else if (c == '}') depth = Math.Max(0, depth - 1); else if (c == '\n') line++; }
            if (depth == 0) return line;
        }
        return 0;
    }
    static readonly Regex Open = new(@"(?<![\w$])(ctx\s*\.\s*)?ui\s*\.\s*open\s*\(", RegexOptions.Compiled);
    /// <summary>Whether a script opens a screen with ui.open (client scripts: web and desktop only).</summary>
    public static bool UsesOpen(string source) => Open.IsMatch(Blank(source));
    /// <summary>Whether a script saves or loads with ctx.save (web and desktop only).</summary>
    public static bool UsesSave(string source) => Save.IsMatch(Blank(source));

    /// <summary>The source with comments and the insides of strings blanked out (line breaks kept), so braces and
    /// names inside them don't count.</summary>
    public static string Blank(string s)
    {
        var o = s.ToCharArray();
        for (int i = 0; i < o.Length; i++)
        {
            char c = o[i];
            if (c == '/' && i + 1 < o.Length && o[i + 1] == '/') { while (i < o.Length && o[i] != '\n') o[i++] = ' '; i--; }
            else if (c == '/' && i + 1 < o.Length && o[i + 1] == '*') { o[i] = o[i + 1] = ' '; i += 2; while (i < o.Length && !(o[i] == '*' && i + 1 < o.Length && o[i + 1] == '/')) { if (o[i] != '\n') o[i] = ' '; i++; } if (i < o.Length) { o[i] = ' '; if (i + 1 < o.Length) o[i + 1] = ' '; i++; } }
            else if (c is '"' or '\'' or '`')
            {
                i++;
                while (i < o.Length && o[i] != c) { if (o[i] == '\\' && i + 1 < o.Length) { o[i] = ' '; i++; } if (o[i] != '\n') o[i] = ' '; i++; }
            }
        }
        return new string(o);
    }
}
