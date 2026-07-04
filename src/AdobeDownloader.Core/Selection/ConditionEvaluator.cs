using System.Text.RegularExpressions;

namespace AdobeDownloader.Core.Selection;

/// <summary>
/// 求值 Adobe 包的 Condition 表达式，例如 [installLanguage]==zh_CN 或
/// [OSVersion]&gt;=10.0 &amp;&amp; ([OSArchitecture]==x64 || [OSArchitecture]==arm64)。
/// 忠实移植原版 HDPIMConditionTokenizer / HDPIMConditionEvaluator。
/// </summary>
public sealed partial class ConditionEvaluator
{
    private readonly List<char> _chars;
    private int _index;
    private readonly IReadOnlyDictionary<string, string> _vars;
    private Token _current;

    private ConditionEvaluator(string condition, IReadOnlyDictionary<string, string> variables)
    {
        var normalized = condition.Replace("&amp;&amp;", "&&").Replace("&amp;||", "||");
        _chars = normalized.ToList();
        _vars = variables;
        _current = Next();
    }

    /// <summary>空条件视为满足。</summary>
    public static bool Evaluate(string condition, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        return new ConditionEvaluator(condition.Trim(), variables).ParseOr();
    }

    // ---- 语法分析（or -> and -> unary -> primary）----

    private bool ParseOr()
    {
        var result = ParseAnd();
        while (_current.Kind == Kind.Or) { Advance(); result = ParseAnd() || result; }
        return result;
    }

    private bool ParseAnd()
    {
        var result = ParseUnary();
        while (_current.Kind == Kind.And) { Advance(); result = ParseUnary() && result; }
        return result;
    }

    private bool ParseUnary()
    {
        if (_current.Kind == Kind.Not) { Advance(); return !ParseUnary(); }
        return ParsePrimary();
    }

    private bool ParsePrimary()
    {
        if (_current.Kind == Kind.LeftParen)
        {
            Advance();
            var value = ParseOr();
            if (_current.Kind == Kind.RightParen) Advance();
            return value;
        }

        var left = ParseOperand();
        switch (_current.Kind)
        {
            case Kind.Equal or Kind.NotEqual or Kind.Gt or Kind.Ge or Kind.Lt or Kind.Le:
                var comparator = _current.Kind;
                Advance();
                var right = ParseOperand();
                return Compare(left, comparator, right);
            default:
                return Truthy(left);
        }
    }

    private string ParseOperand()
    {
        switch (_current.Kind)
        {
            case Kind.Variable:
                var name = _current.Text;
                Advance();
                return _vars.TryGetValue(name, out var v) ? v : "";
            case Kind.Literal:
                var lit = _current.Text;
                Advance();
                return lit;
            default:
                return "";
        }
    }

    private void Advance() => _current = Next();

    // ---- 比较 ----

    private static bool Compare(string leftRaw, Kind cmp, string rightRaw)
    {
        var left = Normalize(leftRaw);
        var right = Normalize(rightRaw);

        if (IsVersion(left) || IsVersion(right))
        {
            var c = CompareVersions(left, right);
            return cmp switch
            {
                Kind.Equal => c == 0,
                Kind.NotEqual => c != 0,
                Kind.Gt => c > 0,
                Kind.Ge => c >= 0,
                Kind.Lt => c < 0,
                Kind.Le => c <= 0,
                _ => false,
            };
        }

        if (double.TryParse(left, out var nl) && double.TryParse(right, out var nr))
        {
            return cmp switch
            {
                Kind.Equal => nl == nr,
                Kind.NotEqual => nl != nr,
                Kind.Gt => nl > nr,
                Kind.Ge => nl >= nr,
                Kind.Lt => nl < nr,
                Kind.Le => nl <= nr,
                _ => false,
            };
        }

        var leftValues = SplitList(left);
        var rightValues = SplitList(right);
        var leftAll = leftValues.Contains("ALL");
        var rightAll = rightValues.Contains("ALL");

        switch (cmp)
        {
            case Kind.Equal:
                if (leftAll || rightAll) return true;
                return leftValues.Overlaps(rightValues);
            case Kind.NotEqual:
                if (leftAll || rightAll) return false;
                return !leftValues.Overlaps(rightValues);
            default:
                var comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
                return cmp switch
                {
                    Kind.Gt => comparison > 0,
                    Kind.Ge => comparison >= 0,
                    Kind.Lt => comparison < 0,
                    Kind.Le => comparison <= 0,
                    _ => false,
                };
        }
    }

    private static bool Truthy(string value)
    {
        var n = Normalize(value).ToLowerInvariant();
        if (n.Length == 0) return false;
        return n is not ("false" or "0" or "no");
    }

    private static string Normalize(string value)
        => value.Trim().Replace("\"", "").Replace("'", "");

    private static bool IsVersion(string value)
        => value.Contains('.') && VersionRegex().IsMatch(value);

    private static int CompareVersions(string v1, string v2)
    {
        var a = v1.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var b = v2.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var len = Math.Max(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x - y;
        }
        return 0;
    }

    private static HashSet<string> SplitList(string value)
        => value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    // ---- 词法分析 ----

    private enum Kind
    {
        LeftParen, RightParen, And, Or, Not,
        Equal, NotEqual, Gt, Ge, Lt, Le,
        Variable, Literal, End,
    }

    private readonly record struct Token(Kind Kind, string Text = "");

    private Token Next()
    {
        SkipWhitespace();
        if (_index >= _chars.Count) return new Token(Kind.End);

        var c = _chars[_index];
        switch (c)
        {
            case '(': _index++; return new Token(Kind.LeftParen);
            case ')': _index++; return new Token(Kind.RightParen);
            case '[': return ReadVariable();
            case '"' or '\'': return ReadQuoted(c);
        }
        if (c == '&' && Match("&&")) return new Token(Kind.And);
        if (c == '|' && Match("||")) return new Token(Kind.Or);
        if (c == '!' && Match("!=")) return new Token(Kind.NotEqual);
        if (c == '!') { _index++; return new Token(Kind.Not); }
        if (c == '=' && Match("==")) return new Token(Kind.Equal);
        if (c == '>' && Match(">=")) return new Token(Kind.Ge);
        if (c == '<' && Match("<=")) return new Token(Kind.Le);
        if (c == '>') { _index++; return new Token(Kind.Gt); }
        if (c == '<') { _index++; return new Token(Kind.Lt); }
        return ReadLiteral();
    }

    private void SkipWhitespace()
    {
        while (_index < _chars.Count && char.IsWhiteSpace(_chars[_index])) _index++;
    }

    private bool Match(string value)
    {
        var end = _index + value.Length;
        if (end > _chars.Count) return false;
        if (new string(_chars.GetRange(_index, value.Length).ToArray()) == value)
        {
            _index = end;
            return true;
        }
        return false;
    }

    private Token ReadVariable()
    {
        _index++; // skip '['
        var start = _index;
        while (_index < _chars.Count && _chars[_index] != ']') _index++;
        var name = new string(_chars.GetRange(start, Math.Min(_index, _chars.Count) - start).ToArray());
        if (_index < _chars.Count && _chars[_index] == ']') _index++;
        return new Token(Kind.Variable, name);
    }

    private Token ReadQuoted(char quote)
    {
        _index++;
        var start = _index;
        while (_index < _chars.Count && _chars[_index] != quote) _index++;
        var value = new string(_chars.GetRange(start, Math.Min(_index, _chars.Count) - start).ToArray());
        if (_index < _chars.Count && _chars[_index] == quote) _index++;
        return new Token(Kind.Literal, value);
    }

    private Token ReadLiteral()
    {
        var start = _index;
        while (_index < _chars.Count)
        {
            var c = _chars[_index];
            if (char.IsWhiteSpace(c) || c is '(' or ')' or '[' or ']') break;
            if (c is '&' or '|' or '!' or '=' or '<' or '>') break;
            _index++;
        }
        var literal = new string(_chars.GetRange(start, _index - start).ToArray()).Trim();
        return new Token(Kind.Literal, literal);
    }

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)+$")]
    private static partial Regex VersionRegex();
}
