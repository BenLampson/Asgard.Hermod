using System.Globalization;
using System.Text;

namespace Asgard.Hermod.PostgreSql;

/// <summary>临时筛选只接受字段比较、LIKE、IN、IS NULL 和布尔组合，不接受任意 SQL 片段。</summary>
internal sealed class PostgreSqlFilterParser(IEnumerable<string> columns, string text)
{
    private readonly Dictionary<string, string> _columns = columns.ToDictionary(name => name, StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);
    private readonly string _text = text.Length <= 2048 ? text : throw Text.Exception("PostgreSqlFilterInvalid");
    private int _position, _depth;
    private Token _current;

    internal (string Sql, IReadOnlyDictionary<string, object?> Parameters) Parse()
    {
        Next();
        if (_current.Kind == Kind.End) return ("", _parameters);
        Take("WHERE");
        string sql = Or();
        if (_current.Kind != Kind.End) throw Invalid();
        return (sql, _parameters);
    }
    private string Or()
    {
        string left = And();
        while (Take("OR")) left = "(" + left + " OR " + And() + ")";
        return left;
    }
    private string And()
    {
        string left = Primary();
        while (Take("AND")) left = "(" + left + " AND " + Primary() + ")";
        return left;
    }
    private string Primary()
    {
        if (++_depth > 16) throw Invalid();
        try
        {
            if (Take("NOT")) return "(NOT " + Primary() + ")";
            if (Take("(")) { string nested = Or(); Require(")"); return "(" + nested + ")"; }
            return Comparison();
        }
        finally { _depth--; }
    }
    private string Comparison()
    {
        if (_current.Kind != Kind.Identifier || !_columns.TryGetValue(_current.Text, out string? column)) throw Invalid();
        Next(); string name = PostgreSqlSql.Quote(column);
        if (Take("IS")) { bool not = Take("NOT"); Require("NULL"); return name + (not ? " IS NOT NULL" : " IS NULL"); }
        bool negate = Take("NOT");
        if (Take("LIKE")) return name + (negate ? " NOT LIKE " : " LIKE ") + Value();
        if (Take("IN"))
        {
            Require("("); var values = new List<string> { Value() };
            while (Take(",")) values.Add(Value());
            Require(")"); return name + (negate ? " NOT IN (" : " IN (") + string.Join(",", values) + ")";
        }
        if (negate || _current.Kind != Kind.Symbol || _current.Text is not ("=" or "!=" or "<>" or ">" or ">=" or "<" or "<=")) throw Invalid();
        string operation = _current.Text; Next(); return name + " " + operation + " " + Value();
    }
    private string Value()
    {
        object? value = _current.Kind switch
        {
            Kind.String => _current.Text,
            Kind.Number when decimal.TryParse(_current.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) => number,
            Kind.Identifier when _current.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) => true,
            Kind.Identifier when _current.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase) => false,
            Kind.Identifier when _current.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase) => null,
            _ => throw Invalid()
        };
        if (_parameters.Count >= 64) throw Invalid();
        string parameter = "@filter" + _parameters.Count.ToString(CultureInfo.InvariantCulture);
        _parameters.Add(parameter, value); Next(); return parameter;
    }
    private bool Take(string value)
    {
        if (_current.Quoted || !_current.Text.Equals(value, StringComparison.OrdinalIgnoreCase)) return false;
        Next(); return true;
    }
    private void Require(string value) { if (!Take(value)) throw Invalid(); }
    private void Next()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        if (_position == _text.Length) { _current = new(Kind.End, ""); return; }
        char first = _text[_position++];
        if (first is '\'' or '"')
        {
            var value = new StringBuilder();
            while (_position < _text.Length)
            {
                char part = _text[_position++];
                if (part != first) { value.Append(part); continue; }
                if (_position < _text.Length && _text[_position] == first) { _position++; value.Append(first); continue; }
                _current = new(first == '"' ? Kind.Identifier : Kind.String, value.ToString(), true); return;
            }
            throw Invalid();
        }
        if (char.IsLetter(first) || first == '_')
        {
            int start = _position - 1;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] is '_' or '$')) _position++;
            _current = new(Kind.Identifier, _text[start.._position]); return;
        }
        if (char.IsAsciiDigit(first) || first is '+' or '-' or '.')
        {
            int start = _position - 1;
            while (_position < _text.Length && (char.IsAsciiDigit(_text[_position]) || _text[_position] is '.' or 'e' or 'E' or '+' or '-')) _position++;
            _current = new(Kind.Number, _text[start.._position]); return;
        }
        if (first is '(' or ')' or ',' or '=' or '!' or '<' or '>')
        {
            string value = first.ToString();
            if (_position < _text.Length && first is '!' or '<' or '>' && _text[_position] == '=') value += _text[_position++];
            else if (first == '<' && _position < _text.Length && _text[_position] == '>') value += _text[_position++];
            _current = new(Kind.Symbol, value); return;
        }
        throw Invalid();
    }
    private static Exception Invalid() => Text.Exception("PostgreSqlFilterInvalid");
    private enum Kind { End, Identifier, String, Number, Symbol }
    private readonly record struct Token(Kind Kind, string Text, bool Quoted = false);
}
