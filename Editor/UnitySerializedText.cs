using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Blueprinter
{
    public static class UnitySerializedText
    {
        public static readonly Regex ObjectReferenceRegex = new Regex(@"\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]{32}),\s*type:\s*(\d+)\}", RegexOptions.Compiled);

        private static readonly Regex MetaGuidRegex = new Regex(@"^guid:[ \t]*([0-9a-fA-F]{32})[ \t]*(?=\r?$)", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex AssetReferenceGuidRegex = new Regex(@"(^[ \t]*m_AssetGUID:[ \t]*)([0-9a-fA-F]{32})([ \t]*(?=\r?$))", RegexOptions.Compiled | RegexOptions.Multiline);

        public static bool IsSerializedText(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return IsSerializedText(stream);
        }

        public static bool IsSerializedText(Stream stream)
        {
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
                return reader.ReadLine()?.StartsWith("%YAML", StringComparison.Ordinal) == true;
        }

        public static string ReadMetaGuid(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8))
                return ReadMetaGuid(reader);
        }

        public static string ReplaceMetaGuid(string text, string guid)
        {
            return MetaGuidRegex.Replace(text, "guid: " + guid, 1);
        }

        public static string RewriteObjectReference(Match match, string guid, string fileId)
        {
            return $"{{fileID: {fileId}, guid: {guid}, type: {match.Groups[3].Value}}}";
        }

        public static string RewriteAssetReferenceGuids(string text, Dictionary<string, string> guidMap, out int replacements)
        {
            var count = 0;
            var result = AssetReferenceGuidRegex.Replace(text, match =>
            {
                var guid = match.Groups[2].Value;
                if (!guidMap.TryGetValue(guid, out var replacement) || string.Equals(guid, replacement, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }

                count++;
                return match.Groups[1].Value + replacement + match.Groups[3].Value;
            });

            replacements = count;
            return result;
        }

        public static string ReadMetaGuid(TextReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("guid:", StringComparison.Ordinal))
                    continue;

                var match = MetaGuidRegex.Match(line);
                return match.Success ? match.Groups[1].Value : null;
            }

            return null;
        }
    }
}
