using System.Collections;
using System.Linq;
using UnityEngine;

namespace RefsaAI
{
    public enum BoardEval
    {
        Invalid = 0,
        Regular,
        InCheck
    }

    public struct BoardEvaluation
    {
        public BoardEval Evaluation;
        public Index? Position;
    }

    public class AIManager : MonoBehaviour
    {
        BoardState defaultBoard;

        VisualBoard visualBoard;

        AIGame game;

        void Start()
        {
            visualBoard = GetComponent<VisualBoard>();

            defaultBoard = Game.Deserialize(((TextAsset)Resources.Load("DefaultBoardState", typeof(TextAsset))).text).turnHistory.Last();

            game = new AIGame(defaultBoard);
            visualBoard.ShowBoardState(game.CurrentBoard);

            StartCoroutine(SlowTick());
        }

        IEnumerator SlowTick()
        {
            while (true)
            {
                if (!game.Tick())
                {
                    Debug.Log("Checkmate!");
                    game = new AIGame(defaultBoard);
                }
                visualBoard.ShowBoardState(game.CurrentBoard);
                // yield return new WaitForSeconds(0.25f);
                yield return null;
                yield return null;
            }
        }
    }
}