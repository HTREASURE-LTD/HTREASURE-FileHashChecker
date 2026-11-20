using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HTREASURE.Standards.Security
{
    /// <summary>
    /// Strict JSON reading on .NET Framework (equivalent of StandardsJson.Options in the .NET 10 library):
    /// unknown members are rejected, required members must be present with the right type, depth is limited.
    /// </summary>
    internal sealed class JsonObj
    {
        private readonly IDictionary<string, object> values;
        private readonly HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
        private readonly string path;

        private JsonObj(IDictionary<string, object> values, string path)
        {
            this.values = values;
            this.path = path;
        }

        public static JsonObj Parse(byte[] utf8, int maxBytes)
        {
            if (utf8.Length > maxBytes) throw new InvalidDataException("The JSON document is too large.");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(utf8); }
            catch (DecoderFallbackException) { throw new InvalidDataException("The JSON document is not valid UTF-8."); }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            object root;
            try
            {
                root = new JavaScriptSerializer { MaxJsonLength = maxBytes, RecursionLimit = 32 }.DeserializeObject(text);
            }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid JSON: " + ex.Message); }
            catch (InvalidOperationException ex) { throw new InvalidDataException("Invalid JSON: " + ex.Message); }
            var dict = root as IDictionary<string, object>;
            if (dict == null) throw new InvalidDataException("The JSON document must be an object.");
            return new JsonObj(dict, "$");
        }

        public bool Has(string key) { return values.ContainsKey(key) && values[key] != null; }

        private object Raw(string key, bool required)
        {
            used.Add(key);
            object v;
            if (!values.TryGetValue(key, out v) || v == null)
            {
                if (required) throw new InvalidDataException(path + "." + key + " is required.");
                return null;
            }
            return v;
        }

        public string Str(string key, bool required = true)
        {
            object v = Raw(key, required);
            if (v == null) return null;
            var s = v as string;
            if (s == null) throw new InvalidDataException(path + "." + key + " must be a string.");
            return s;
        }

        public bool Bool(string key)
        {
            object v = Raw(key, true);
            if (!(v is bool)) throw new InvalidDataException(path + "." + key + " must be true or false.");
            return (bool)v;
        }

        public long Long(string key)
        {
            object v = Raw(key, true);
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is decimal && decimal.Truncate((decimal)v) == (decimal)v) return (long)(decimal)v;
            throw new InvalidDataException(path + "." + key + " must be an integer.");
        }

        public double? Number(string key, bool required = true)
        {
            object v = Raw(key, required);
            if (v == null) return null;
            if (v is int || v is long || v is decimal || v is double) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            throw new InvalidDataException(path + "." + key + " must be a number.");
        }

        public DateTimeOffset? Date(string key, bool required = true)
        {
            string s = Str(key, required);
            if (s == null) return null;
            DateTimeOffset d;
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                throw new InvalidDataException(path + "." + key + " is not a valid date and time.");
            return d;
        }

        public JsonObj Obj(string key, bool required = true)
        {
            object v = Raw(key, required);
            if (v == null) return null;
            var d = v as IDictionary<string, object>;
            if (d == null) throw new InvalidDataException(path + "." + key + " must be an object.");
            return new JsonObj(d, path + "." + key);
        }

        public List<JsonObj> Objs(string key, bool required = true)
        {
            var list = Arr(key, required);
            if (list == null) return null;
            var result = new List<JsonObj>();
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i] as IDictionary<string, object>;
                if (d == null) throw new InvalidDataException(path + "." + key + "[" + i + "] must be an object.");
                result.Add(new JsonObj(d, path + "." + key + "[" + i + "]"));
            }
            return result;
        }

        public List<string> Strs(string key, bool required = true)
        {
            var list = Arr(key, required);
            if (list == null) return null;
            var result = new List<string>();
            foreach (var o in list)
            {
                var s = o as string;
                if (s == null) throw new InvalidDataException(path + "." + key + " must contain only strings.");
                result.Add(s);
            }
            return result;
        }

        private List<object> Arr(string key, bool required)
        {
            object v = Raw(key, required);
            if (v == null) return null;
            if (v is string || !(v is IEnumerable)) throw new InvalidDataException(path + "." + key + " must be an array.");
            return ((IEnumerable)v).Cast<object>().ToList();
        }

        /// <summary>Rejects members that were not read (the schemas forbid unknown members).</summary>
        public void RejectUnknown()
        {
            foreach (var k in values.Keys)
                if (!used.Contains(k)) throw new InvalidDataException(path + " has an unknown member '" + k + "'.");
        }
    }

    /// <summary>Minimal JSON writer for state files and reports (stable formatting, invariant culture).</summary>
    internal static class JsonOut
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { Str(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is DateTimeOffset) { Str(sb, ((DateTimeOffset)v).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)); return; }
            if (v is int || v is long) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            var dict = v as IEnumerable<KeyValuePair<string, object>>;
            if (dict != null)
            {
                var items = dict.ToList();
                if (items.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(' ', (indent + 1) * 2);
                    Str(sb, items[i].Key);
                    sb.Append(": ");
                    Write(sb, items[i].Value, indent + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(' ', indent * 2).Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                var items = list.Cast<object>().ToList();
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(' ', (indent + 1) * 2);
                    Write(sb, items[i], indent + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(' ', indent * 2).Append(']');
                return;
            }
            Str(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
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

    /// <summary>Ordered key/value list used with <see cref="JsonOut"/>.</summary>
    internal sealed class JsonMap : List<KeyValuePair<string, object>>
    {
        public void Add(string key, object value) { Add(new KeyValuePair<string, object>(key, value)); }
    }
}
