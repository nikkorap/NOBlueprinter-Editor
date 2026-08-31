using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Blueprinter
{
    public static class AssetRipperPreviewShaders
    {
        private static readonly Regex ShaderNameRegex = new Regex(@"\bShader\s+""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex PropertiesRegex = new Regex(@"\bProperties\s*\{", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TexturePropertyRegex = new Regex(@"(?<attributes>(?:\[[^\]]+\]\s*)*)(?<name>[A-Za-z_]\w*)\s*\(\s*""[^""]*""\s*,\s*2D\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string ReadShaderName(string shaderText)
        {
            var match = ShaderNameRegex.Match(shaderText);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static bool IsDummyShader(string shaderText)
        {
            return !string.IsNullOrEmpty(shaderText) && shaderText.IndexOf("//DummyShaderTextExporter", StringComparison.Ordinal) >= 0;
        }

        public static bool TryBuildPreviewShader(string dummyShader, out string previewShader)
        {
            previewShader = null;
            var nameMatch = ShaderNameRegex.Match(dummyShader);
            var propertiesMatch = PropertiesRegex.Match(dummyShader);
            if (!nameMatch.Success || !propertiesMatch.Success)
                return false;

            var openBrace = dummyShader.IndexOf('{', propertiesMatch.Index);
            var closeBrace = FindMatchingBrace(dummyShader, openBrace);
            if (openBrace < 0 || closeBrace < 0)
                return false;

            var properties = dummyShader.Substring(propertiesMatch.Index, closeBrace - propertiesMatch.Index + 1);
            previewShader = WritePreviewShader(nameMatch.Groups[1].Value, properties, FindPreviewTexture(properties));
            return true;
        }

        private static string FindPreviewTexture(string properties)
        {
            string first = null;
            foreach (Match match in TexturePropertyRegex.Matches(properties))
            {
                var name = match.Groups["name"].Value;
                if (first == null)
                    first = name;

                if (match.Groups["attributes"].Value.IndexOf("MainTexture", StringComparison.OrdinalIgnoreCase) >= 0)
                    return name;
            }

            return first;
        }

        private static int FindMatchingBrace(string text, int openBrace)
        {
            if (openBrace < 0)
                return -1;

            var depth = 0;
            for (var i = openBrace; i < text.Length; i++)
            {
                if (text[i] == '{')
                    depth++;
                else if (text[i] == '}' && --depth == 0)
                    return i;
            }

            return -1;
        }

        private static string WritePreviewShader(string shaderName, string properties, string textureProperty)
        {
            var textureDeclarations = string.IsNullOrEmpty(textureProperty) ? string.Empty : $"            TEXTURE2D({textureProperty});\n            SAMPLER(sampler_{textureProperty});\n            float4 {textureProperty}_ST;\n";
            var uvAssignment = string.IsNullOrEmpty(textureProperty) ? "                output.uv = input.uv;" : $"                output.uv = input.uv * {textureProperty}_ST.xy + {textureProperty}_ST.zw;";
            var fragmentBody = string.IsNullOrEmpty(textureProperty) ? "                return half4(1, 1, 1, 1);" : $"                return SAMPLE_TEXTURE2D({textureProperty}, sampler_{textureProperty}, input.uv);";

            var builder = new StringBuilder();
            builder.Append("Shader \"").Append(shaderName).AppendLine("\"");
            builder.AppendLine("{");
            foreach (var line in properties.Replace("\r", string.Empty).Split('\n'))
                builder.Append("    ").AppendLine(line);

            builder.AppendLine(@"    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""RenderPipeline""=""UniversalPipeline"" }
        Pass
        {
            Name ""BlueprinterPreview""
            Tags { ""LightMode""=""SRPDefaultUnlit"" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
");
            builder.Append(textureDeclarations);
            builder.AppendLine(@"
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
");
            builder.AppendLine(uvAssignment);
            builder.AppendLine(@"                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {");
            builder.AppendLine(fragmentBody);
            builder.AppendLine(@"            }
            ENDHLSL
        }
    }
}");
            return builder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
