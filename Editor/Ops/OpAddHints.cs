using System;
using UnityEngine;

namespace Blueprinter.Editor.Ops
{
    [CreateAssetMenu(menuName = "Blueprinter/OpAddHints", fileName = "OpAddHints")]
    public class OpAddHints : OpCore
    {
        public HintEntry[] hints = Array.Empty<HintEntry>();

        [Serializable]
        public class HintEntry
        {
            public string type = "Did you know?";

            [TextArea]
            public string text;
        }

        [Serializable]
        private class PayloadDto
        {
            public HintEntry[] hints;
        }

        public override string opId => "OpAddHints";
        public override bool IsEmpty => hints == null || hints.Length == 0;

        public override object BuildPayload()
        {
            foreach (var hint in hints)
            {
                if (hint == null || !RequireValue(hint.text, nameof(hints)))
                    return null;
            }

            return new PayloadDto
            {
                hints = hints
            };
        }
    }
}
