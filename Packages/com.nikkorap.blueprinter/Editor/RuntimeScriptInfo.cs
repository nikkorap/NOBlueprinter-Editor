using System.Collections.Generic;
using UnityEditor;

namespace Blueprinter
{
    public class RuntimeScriptInfo
    {
        public MonoScript Script;
        public string Guid;
        public long FileId;
        public string AssemblyName;
        public string FullTypeName;

        public static IEnumerable<RuntimeScriptInfo> GetAll()
        {
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                if (script == null)
                    continue;

                var type = script.GetClass();
                if (type == null || string.IsNullOrEmpty(type.FullName) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out var guid, out long fileId))
                    continue;

                yield return new RuntimeScriptInfo
                {
                    Script = script,
                    Guid = guid,
                    FileId = fileId,
                    AssemblyName = type.Assembly.GetName().Name,
                    FullTypeName = type.FullName
                };
            }
        }
    }
}
