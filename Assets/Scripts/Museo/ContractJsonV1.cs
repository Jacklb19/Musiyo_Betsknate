using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusiyoBetsknate.Museo
{
    internal static class ContractJsonV1
    {
        // Supports the vocabulary emitted by the canonical Pydantic models.
        public static T Parse<T>(string json, string schemaJson)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new JsonException("Empty v1 contract.");
            JToken value;
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                value = JToken.Load(reader);
            var schema = JObject.Parse(schemaJson);
            if (!Matches(value, schema, schema))
                throw new JsonException("Invalid v1 contract structure.");
            return JsonConvert.DeserializeObject<T>(json);
        }

        private static bool Matches(JToken value, JToken schema, JObject root)
        {
            if (schema["$ref"] != null)
                return Matches(value, root["$defs"][(string)schema["$ref"].ToString().Split('/').Last()], root);
            if (schema["anyOf"] is JArray alternatives)
                return alternatives.Any(option => Matches(value, option, root));
            if (schema["const"] != null && !JToken.DeepEquals(value, schema["const"])) return false;
            if (schema["enum"] is JArray choices && !choices.Any(choice => JToken.DeepEquals(value, choice))) return false;
            switch ((string)schema["type"])
            {
                case "null": return value.Type == JTokenType.Null;
                case "object":
                    if (!(value is JObject record)) return false;
                    var properties = (JObject)schema["properties"];
                    if (schema["required"] is JArray required && required.Any(field => record[(string)field] == null)) return false;
                    if (schema["additionalProperties"]?.Value<bool>() == false
                        && record.Properties().Any(field => properties[field.Name] == null)) return false;
                    return record.Properties().All(field => properties[field.Name] == null || Matches(field.Value, properties[field.Name], root));
                case "array":
                    if (!(value is JArray items) || (schema["minItems"] != null && items.Count < (int)schema["minItems"])) return false;
                    if (schema["uniqueItems"]?.Value<bool>() == true
                        && items.Where((item, index) => items.Take(index).Any(prior => JToken.DeepEquals(prior, item))).Any()) return false;
                    return items.All(item => Matches(item, schema["items"], root));
                case "string":
                    if (value.Type != JTokenType.String) return false;
                    var text = value.Value<string>();
                    if (schema["minLength"] != null && text.Length < (int)schema["minLength"]) return false;
                    if (schema["pattern"] != null && !Regex.IsMatch(text, (string)schema["pattern"])) return false;
                    if ((string)schema["format"] == "date-time" && !DateTimeOffset.TryParse(text, out _)) return false;
                    return true;
                case "integer":
                    if (value.Type != JTokenType.Integer) return false;
                    return WithinBounds(value, schema);
                case "number":
                    if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float) return false;
                    return WithinBounds(value, schema);
                case "boolean": return value.Type == JTokenType.Boolean;
                default: throw new JsonException("Unsupported contract schema vocabulary.");
            }
        }

        private static bool WithinBounds(JToken value, JToken schema)
        {
            var number = value.Value<double>();
            return !double.IsNaN(number) && !double.IsInfinity(number)
                && (schema["minimum"] == null || number >= schema["minimum"].Value<double>())
                && (schema["maximum"] == null || number <= schema["maximum"].Value<double>());
        }
    }
}
