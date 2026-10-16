using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PsVim;

/// <summary>
/// Mali JSON čitač/pisač bez zavisnosti (radi i na .NET Framework-u, gde nema System.Text.Json).
/// Podržava komentare (// i /* */) i zareze na kraju, kao jsonc.
/// </summary>
public static class MiniJson
{
    // ---------------- parsiranje u Dictionary / List / string / double / bool / null ----------------
    public static object Parse(string s)
    {
        int i = 0;
        var v = ParseValue(s, ref i);
        SkipWs(s, ref i);
        if (i < s.Length) throw new FormatException($"Višak teksta na poziciji {i}");
        return v;
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = e < 0 ? s.Length : e + 2; continue;
            }
            break;
        }
    }

    private static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) throw new FormatException("Neočekivan kraj JSON-a");
        char c = s[i];
        if (c == '{')
        {
            i++;
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"Očekivano ':' na poziciji {i}");
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException($"Očekivano ',' ili '}}' na poziciji {i}");
            }
        }
        if (c == '[')
        {
            i++;
            var list = new List<object>();
            while (true)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return list; }
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException($"Očekivano ',' ili ']' na poziciji {i}");
            }
        }
        if (c == '"') return ParseString(s, ref i);
        if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        int st = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (i == st) throw new FormatException($"Neočekivan znak '{c}' na poziciji {i}");
        return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
    }

    private static string ParseString(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '"') throw new FormatException($"Očekivan string na poziciji {i}");
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                    i += 4; break;
                default: sb.Append(e); break;
            }
        }
        throw new FormatException("Nezatvoren string");
    }

    // ---------------- mapiranje na objekte ----------------
    public static T Deserialize<T>(string json) where T : new() => (T)ToObject(Parse(json), typeof(T));

    private static object ToObject(object v, Type t)
    {
        if (v == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
        if (t == typeof(string)) return v is string s ? s : Convert.ToString(v, CultureInfo.InvariantCulture);
        if (t == typeof(bool)) return v is bool b ? b : Convert.ToBoolean(v, CultureInfo.InvariantCulture);
        if (t == typeof(int)) return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        if (t == typeof(double)) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var et = t.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(t);
            if (v is List<object> src) foreach (var x in src) list.Add(ToObject(x, et));
            return list;
        }
        if (v is Dictionary<string, object> d)
        {
            var o = Activator.CreateInstance(t);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite || p.GetCustomAttribute<JsonSkipAttribute>() != null) continue;
                if (d.TryGetValue(p.Name, out var pv)) p.SetValue(o, ToObject(pv, p.PropertyType));
            }
            return o;
        }
        throw new FormatException($"Ne mogu da pretvorim {v.GetType().Name} u {t.Name}");
    }

    public static string Serialize(object o)
    {
        var sb = new StringBuilder();
        Write(sb, o, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object o, int ind)
    {
        switch (o)
        {
            case null: sb.Append("null"); return;
            case string s: WriteString(sb, s); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
            case double dd: sb.Append(dd.ToString(CultureInfo.InvariantCulture)); return;
            case IList list:
                {
                    if (list.Count == 0) { sb.Append("[]"); return; }
                    sb.Append("[\n");
                    for (int i = 0; i < list.Count; i++)
                    {
                        sb.Append(' ', (ind + 1) * 2);
                        Write(sb, list[i], ind + 1);
                        sb.Append(i < list.Count - 1 ? ",\n" : "\n");
                    }
                    sb.Append(' ', ind * 2).Append(']');
                    return;
                }
        }
        var props = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetCustomAttribute<JsonSkipAttribute>() == null).ToList();
        sb.Append("{\n");
        for (int i = 0; i < props.Count; i++)
        {
            sb.Append(' ', (ind + 1) * 2);
            var name = props[i].Name;
            WriteString(sb, char.ToLowerInvariant(name[0]) + name.Substring(1));
            sb.Append(": ");
            Write(sb, props[i].GetValue(o), ind + 1);
            sb.Append(i < props.Count - 1 ? ",\n" : "\n");
        }
        sb.Append(' ', ind * 2).Append('}');
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}

/// <summary>Svojstvo se ne upisuje u JSON i ne čita iz njega.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class JsonSkipAttribute : Attribute { }
