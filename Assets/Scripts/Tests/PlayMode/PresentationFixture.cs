#if UNITY_EDITOR
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    internal static class PresentationFixture
    {
        public static GameController CreateGame()
        {
            WorkshopViews.Catalog = AssetDatabase.LoadAssetAtPath<WorkshopViewCatalog>("Assets/Prefabs/Editor/Workshop/WorkshopViews.asset");
            return Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameController>("Assets/Prefabs/GameRoot.prefab"));
        }
        public static BoardView CreateBoard()
            => Object.Instantiate(AssetDatabase.LoadAssetAtPath<BoardView>("Assets/Prefabs/Board/Board.prefab"));
    }
}
#endif
