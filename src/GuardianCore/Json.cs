using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GuardianCore
{
    /// <summary>
    /// A JSON value tree. Deliberately hand-rolled: GuardianCore takes no package dependencies
    /// (netstandard2.0 has no System.Text.Json), and SPEC section 11.2 needs byte-exact canonical
    /// output that a general-purpose serializer would not guarantee across runtimes.
    ///
    /// Numbers are NEVER produced for money. Money is carried as a string (SPEC 11.2, G21).
    /// </summary>
    public abstract class JsonValue
    {
        public static JsonValue Str(string s) => new JsonString(s);
        public static JsonValue Int(long i) => new JsonNumber(i);
        public static JsonValue Bool(bool b) => new JsonBool(b);
        public static JsonValue Null() => JsonNull.Instance;
        public static JsonObject Obj() => new JsonObject();
        public static JsonArray Arr() => new JsonArray();

        /// <summary>Canonical form: keys sorted ordinal, no insignificant whitespace (SPEC 11.2).</summary>
        public string ToCanonical()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        internal abstract void Write(StringBuilder sb);
    }

    public sealed class JsonString : JsonValue
    {
        public string Value { get; }
        public JsonString(string value) { Value = value; }
        internal override void Write(StringBuilder sb) => WriteEscaped(sb, Value);

        internal static void WriteEscaped(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    public sealed class JsonNumber : JsonValue
    {
        public long Value { get; }
        public JsonNumber(long value) { Value = value; }
        internal override void Write(StringBuilder sb) => sb.Append(Value.ToString(CultureInfo.InvariantCulture));
    }

    public sealed class JsonBool : JsonValue
    {
        public bool Value { get; }
        public JsonBool(bool value) { Value = value; }
        internal override void Write(StringBuilder sb) => sb.Append(Value ? "true" : "false");
    }

    public sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
        internal override void Write(StringBuilder sb) => sb.Append("null");
    }

    public sealed class JsonArray : JsonValue
    {
        private readonly List<JsonValue> _items = new List<JsonValue>();
        public IReadOnlyList<JsonValue> Items => _items;
        public JsonArray Add(JsonValue v) { _items.Add(v); return this; }
        public int Count => _items.Count;
        internal override void Write(StringBuilder sb)
        {
            sb.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                _items[i].Write(sb);
            }
            sb.Append(']');
        }
    }

    public sealed class JsonObject : JsonValue
    {
        private readonly Dictionary<string, JsonValue> _members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _members.Keys;
        public bool Has(string key) => _members.ContainsKey(key);
        public JsonValue this[string key] => _members.TryGetValue(key, out var v) ? v : null;

        public JsonObject Set(string key, JsonValue value) { _members[key] = value; return this; }
        public JsonObject Set(string key, string value) { _members[key] = JsonValue.Str(value); return this; }
        public JsonObject Set(string key, long value) { _members[key] = JsonValue.Int(value); return this; }
        public JsonObject Set(string key, bool value) { _members[key] = JsonValue.Bool(value); return this; }
        /// <summary>Money goes in as a 2-decimal string, never as a JSON number (SPEC 11.2).</summary>
        public JsonObject SetMoney(string key, decimal value) { _members[key] = JsonValue.Str(Money.Format(value)); return this; }
        public JsonObject Remove(string key) { _members.Remove(key); return this; }

        public string GetString(string key) => this[key] is JsonString s ? s.Value : null;
        public long? GetInt(string key) => this[key] is JsonNumber n ? n.Value : (long?)null;

        internal override void Write(StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            // Ordinal sort: the canonical key order of SPEC 11.2.
            foreach (var key in _members.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!first) sb.Append(',');
                first = false;
                JsonString.WriteEscaped(sb, key);
                sb.Append(':');
                _members[key].Write(sb);
            }
            sb.Append('}');
        }
    }

    /// <summary>Minimal strict JSON reader. Rejects trailing content, comments and duplicate keys:
    /// a file we cannot parse unambiguously is an unknown, and unknowns fail closed (SPEC section 10).</summary>
    public static class JsonParser
    {
        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            value = null; error = null;
            if (text == null) { error = "input is null"; return false; }
            int i = 0;
            try
            {
                var v = ParseValue(text, ref i);
                SkipWs(text, ref i);
                if (i != text.Length) { error = "trailing content at offset " + i.ToString(CultureInfo.InvariantCulture); return false; }
                value = v;
                return true;
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '﻿')) i++;
        }

        private static JsonValue ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of input");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonString(ParseString(s, ref i));
                case 't': Expect(s, ref i, "true"); return JsonValue.Bool(true);
                case 'f': Expect(s, ref i, "false"); return JsonValue.Bool(false);
                case 'n': Expect(s, ref i, "null"); return JsonValue.Null();
                default: return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new FormatException("expected " + literal + " at offset " + i.ToString(CultureInfo.InvariantCulture));
            i += literal.Length;
        }

        private static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool any = false, isInt = true;
            while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+'))
            {
                if (s[i] == '.' || s[i] == 'e' || s[i] == 'E') isInt = false;
                any = true; i++;
            }
            if (!any) throw new FormatException("invalid value at offset " + start.ToString(CultureInfo.InvariantCulture));
            var raw = s.Substring(start, i - start);
            if (!isInt) throw new FormatException("non-integer JSON number '" + raw + "' at offset " + start.ToString(CultureInfo.InvariantCulture) +
                                                 "; money and fractions must be strings (SPEC 11.2)");
            if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
                throw new FormatException("integer out of range at offset " + start.ToString(CultureInfo.InvariantCulture));
            return JsonValue.Int(l);
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string at offset " + i.ToString(CultureInfo.InvariantCulture));
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("unterminated string");
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("unterminated escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw new FormatException("unknown escape \\" + e);
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static JsonArray ParseArray(string s, ref int i)
        {
            i++; // '['
            var arr = JsonValue.Arr();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return arr; }
            while (true)
            {
                arr.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return arr; }
                throw new FormatException("expected ',' or ']' at offset " + i.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static JsonObject ParseObject(string s, ref int i)
        {
            i++; // '{'
            var obj = JsonValue.Obj();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipWs(s, ref i);
                var key = ParseString(s, ref i);
                if (obj.Has(key)) throw new FormatException("duplicate key '" + key + "'");
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' after key '" + key + "'");
                i++;
                obj.Set(key, ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return obj; }
                throw new FormatException("expected ',' or '}' at offset " + i.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
