using System;
using UnityEngine;

namespace Blueprinter.Editor.Ops
{
    [CreateAssetMenu(menuName = "Blueprinter/OpAddHints", fileName = "OpAddHints")]
    public class OpAddHints : OpCore
    {
        [TextArea]
        public string[] hints = Array.Empty<string>();

        [Serializable]
        private class PayloadDto
        {
            public string[] hints;
        }

        public override string opId => "OpAddHints";
        public override bool IsEmpty => hints == null || hints.Length == 0;

        public override object BuildPayload()
        {
            foreach (var hint in hints)
            {
                if (!RequireValue(hint, nameof(hints)))
                    return null;
            }

            return new PayloadDto
            {
                hints = hints
            };
        }
    }
}
