using Sokoban.Core;
using UnityEngine;

namespace Sokoban.EditorTools
{
    // The separate MonoScript also lets Unity restore this temporary object on domain reload.
    public sealed class LevelEditorDraft : ScriptableObject
    {
        public LevelDefinition Data;
        public string VerifiedSolution;
    }
}
