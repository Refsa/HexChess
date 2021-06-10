using System.Linq;

namespace RefsaAI
{
    public class AIGame
    {
        BoardState boardState;

        AI white;
        AI black;
        Team currentTeam;

        public BoardState CurrentBoard => boardState;

        Team OtherTeam => currentTeam == Team.White ? Team.Black : Team.White;
        AI GetAI(Team team) => team == Team.White ? white : black;

        System.Diagnostics.Stopwatch perfSW = new System.Diagnostics.Stopwatch();

        public AIGame(BoardState initialState)
        {
            boardState = initialState.Clone();

            var blackPieces = CurrentBoard.allPiecePositions.Where(kvp => kvp.Key.Item1 == Team.Black).Select(kvp => (kvp.Value, kvp.Key.Item2));
            var whitePieces = CurrentBoard.allPiecePositions.Where(kvp => kvp.Key.Item1 == Team.White).Select(kvp => (kvp.Value, kvp.Key.Item2));

            white = new AI(Team.White, whitePieces);
            black = new AI(Team.Black, blackPieces);

            currentTeam = Team.White;
        }

        public bool Tick()
        {
            perfSW.Restart();

            var team = GetAI(currentTeam);
            var move = team.Tick(boardState);

            if (move != null)
            {
                var _move = move.Value;
                if (_move.MoveType != MoveType.None)
                {
                    boardState.TryGetPiece(_move.From, out var fromPiece);

                    var result = boardState.ApplyMove(fromPiece, _move.From, (_move.To, _move.MoveType), team.Promotions);

                    if (_move.MoveType == MoveType.Attack || _move.MoveType == MoveType.EnPassant)
                    {
                        GetAI(OtherTeam).KillPiece(_move.To);
                    }

                    boardState.Dispose();
                    boardState = result.newState;

                }
            }

            currentTeam = OtherTeam;

            perfSW.Stop();
            // UnityEngine.Debug.Log($"Tick took {perfSW.ElapsedTicks / 10_000f} ms");

            return !boardState.IsChecking(team.Team, null);
        }
    }
}