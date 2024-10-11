using System.Text.RegularExpressions;
using UnityEditor;

namespace Salvavida.Unity.Editor
{
    public class NullableEnabler : AssetPostprocessor
    {
        private const string PROPERTY_GROUP = "<PropertyGroup>";
        private static readonly Regex regex = new("\\<TargetFrameworkVersion\\>.+?\\<\\/TargetFrameworkVersion\\>");

        public static string OnGeneratedCSProject(string path, string content)
        {
            if (!path.EndsWith("Salvavida.csproj"))
                return content;
            var crlf = false;
            var index = content.IndexOf(PROPERTY_GROUP);
            if (index < 0)
                return content;

            index += PROPERTY_GROUP.Length;
            while (true)
            {
                if (content[index] == '\r')
                {
                    crlf = true;
                    index++;
                    continue;
                }
                else if (content[index] == '\n')
                {
                    index++;
                    continue;
                }
                break;
            }
            content = string.Join("", content[..index],
                "    <Nullable>enable</Nullable>", crlf ? "\r\n" : "\n", content[index..]);

            content = regex.Replace(content, "<TargetFramework>net471</TargetFramework>");

            return content;
        }
    }
}
