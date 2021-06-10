using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RefsaAI
{
    public class VisualBoard : MonoBehaviour
    {
        [SerializeField] List<PiecePrefab> piecePrefabs = new List<PiecePrefab>();

        Dictionary<(Team, Piece), IPiece> spawnedPieces;
        List<List<Hex>> hexes;

        void Awake()
        {
            spawnedPieces = new Dictionary<(Team, Piece), IPiece>();
            hexes = new List<List<Hex>>();

            CollectHexes();

            for (int i = 0; i < piecePrefabs.Count; i++)
            {
                var go = Instantiate(piecePrefabs[i].Prefab);
                go.SetActive(false);
                spawnedPieces.Add((piecePrefabs[i].Team, piecePrefabs[i].Piece), go.GetComponent<IPiece>());
            }
        }

        public void ShowBoardState(BoardState currentBoard)
        {
            foreach (var piece in spawnedPieces.Values)
            {
                ((MonoBehaviour)piece).gameObject.SetActive(false);
            }

            foreach (var kvp in currentBoard.allPiecePositions)
            {
                if (spawnedPieces.TryGetValue(kvp.Key, out var piece))
                {
                    ((MonoBehaviour)piece).gameObject.SetActive(true);
                    piece.MoveTo(GetHexIfInBounds(kvp.Value));
                }
            }
        }

        void CollectHexes()
        {
            int wide = 0;
            int narrow = -1;
            int index = 0;
            hexes.Add(new List<Hex>());
            for (int i = 0; i < transform.childCount; i++)
            {
                Hex hex = transform.GetChild(i).GetComponent<Hex>();

                if (narrow < 4)
                {
                    narrow++;
                    if (narrow == 4)
                    {
                        hexes.Add(new List<Hex>());
                        wide = 0;
                    }
                }
                else if (wide < 5)
                {
                    wide++;
                    if (wide == 5)
                    {
                        hexes.Add(new List<Hex>());
                        narrow = 0;
                    }
                }

                hexes.Last().Add(hex);
            }
        }

        public Hex GetHexIfInBounds(int row, int col) =>
            HexGrid.IsInBounds(row, col) ? hexes[row][col] : null;
        public Hex GetHexIfInBounds(Index index) =>
            GetHexIfInBounds(index.row, index.col);
    }
}