using System.Collections;
using UnityEngine;

namespace RefsaAI
{
    public class AIManager : MonoBehaviour
    {
        AIBoard board;

        void Awake()
        {
            board = GetComponent<AIBoard>();
            StartCoroutine(TestMove());
        }

        IEnumerator TestMove()
        {
            yield return new WaitForSeconds(0.25f);
            Debug.Assert(board.MovePiece(new Index(2, 'a'), new Index(3, 'a')) != null);
            yield return new WaitForSeconds(0.25f);
            Debug.Assert(board.MovePiece(new Index(3, 'a'), new Index(4, 'a')) == null);
        }
    }
}