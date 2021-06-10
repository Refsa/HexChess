using System.Collections.Generic;
using System.Linq;

namespace RefsaAI
{
    public readonly struct AIMove
    {
        public readonly Index From;
        public readonly Index To;
        public readonly Piece Piece;
        public readonly MoveType MoveType;

        public AIMove(Piece piece, Index from, Index to, MoveType moveType)
        {
            Piece = piece;
            From = from;
            To = to;
            MoveType = moveType;
        }
    }

    class AIPiece
    {
        Index position;
        Piece piece;

        public Index Position => position;
        public Piece Piece => piece;

        public AIPiece(Index position, Piece piece)
        {
            this.position = position;
            this.piece = piece;
        }

        public void SetPosition(Index position)
        {
            this.position = position;
        }
    }

    public class AI
    {
        Team team;

        List<AIMove> history;

        List<AIPiece> activePieces;
        List<AIPiece> graveyard;
        List<AIPiece> promoted;
        List<Promotion> promotions;

        public List<Promotion> Promotions => promotions;
        public Team Team => team;

        public AI(Team team, IEnumerable<(Index, Piece)> pieces)
        {
            this.team = team;

            graveyard = new List<AIPiece>();
            promoted = new List<AIPiece>();
            promotions = new List<Promotion>();
            history = new List<AIMove>();

            activePieces = pieces.Select(e => new AIPiece(e.Item1, e.Item2)).ToList();
        }

        public AIMove? Tick(BoardState boardState)
        {
            var selectedMove =
            activePieces
                .SelectMany(e => GetAllPossibleMovesWithPiece(e, boardState))
                .Where(e => e.Item2.Item2 != MoveType.None)
                /* .Where(e =>
                    e.Item2.Item2 switch
                    {
                        MoveType.EnPassant => 150,
                        MoveType.Attack => 100,
                        MoveType.Defend => 50,
                        MoveType.Move => 25,
                    } >= 25) */
                .Shuffle()
                .First();

            (AIPiece piece, (Index to, MoveType moveType)) = selectedMove;

            var from = piece.Position;
            var move = new AIMove(piece.Piece, from, to, moveType);
            history.Add(move);

            piece.SetPosition(to);
            return move;
            // return RandomMove(boardState);
        }

        public void HandlePromotion()
        {

        }

        public void KillPiece(Index position)
        {
            int index = activePieces.FindIndex(e => e.Position == position);
            if (index < 0 || index >= activePieces.Count)
            {
                throw new System.IndexOutOfRangeException($"Couldnt find piece at index {position}");
            }

            graveyard.Add(activePieces[index]);
            activePieces.RemoveAt(index);
            UnityEngine.Debug.Log("Killed Piece");
        }

        IEnumerable<(Index, MoveType)> GetAllPossibleMoves(AIPiece piece, in BoardState boardState)
        {
            return MoveGenerator.GetAllPossibleMoves(piece.Position, piece.Piece, team, boardState);
        }

        IEnumerable<(AIPiece, (Index, MoveType))> GetAllPossibleMovesWithPiece(AIPiece piece, in BoardState boardState)
        {
            return MoveGenerator
                .GetAllPossibleMoves(piece.Position, piece.Piece, team, boardState)
                .Select(e => (piece, e));
        }


        AIMove? RandomMove(in BoardState boardState)
        {
            foreach (var piece in activePieces.Shuffle())
            {
                var availableMoves = GetAllPossibleMoves(piece, boardState).Where(e => e.Item2 != MoveType.None);
                if (availableMoves.Any())
                {
                    var move = availableMoves.Shuffle().First();
                    var moveType = move.Item2;
                    var to = move.Item1;
                    var from = piece.Position;

                    piece.SetPosition(to);

                    return new AIMove(piece.Piece, from, to, moveType);
                }
            }

            return null;
        }
    }
}