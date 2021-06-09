using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RefsaAI
{
    [System.Serializable]
    class PiecePrefab
    {
        public Team Team;
        public Piece Piece;
        public GameObject Prefab;
    }

    public class AIBoard : MonoBehaviour
    {
        static readonly string defaultBoardStateFileLoc = "DefaultBoardState";

        public event System.Action<BoardState> OnNewTurn;
        public event System.Action<Game> OnGameOver;

        [SerializeField] List<PiecePrefab> piecePrefabs = new List<PiecePrefab>();

        List<BoardState> turnHistory = new List<BoardState>();
        List<Jail> jails = new List<Jail>();
        List<Promotion> promotions = new List<Promotion>();
        Dictionary<(Team, Piece), IPiece> activePieces = new Dictionary<(Team, Piece), IPiece>();
        List<List<Piece>> insufficientSets = new List<List<Piece>>();
        private BoardState? lastSetState = null;
        List<List<Hex>> hexes = new List<List<Hex>>();

        public Game game;
        public int turnsSincePawnMovedOrPieceTaken = 0;
        float timeOffset = 0f;

        private void Awake()
        {
            LoadGame(GetDefaultGame(defaultBoardStateFileLoc));
        }
        private void Start() { }

        public Game GetDefaultGame(string loc)
        {
            return Game.Deserialize(((TextAsset)Resources.Load(loc, typeof(TextAsset))).text);
        }

        public void LoadGame(Game game)
        {
            turnHistory = game.turnHistory;
            this.game = game;

            foreach (Jail jail in jails)
                jail?.Clear();

            BoardState state = turnHistory[turnHistory.Count - 1];

            SetBoardState(state, game.promotions);

            Move move = BoardState.GetLastMove(turnHistory);

            // When loading a game, we need to count how many turns have passed towards the 50 move rule
            turnsSincePawnMovedOrPieceTaken = 0;
            for (int i = 0; i < turnHistory.Count - 1; i++)
            {
                Move moveStep = BoardState.GetLastMove(turnHistory.Skip(i).Take(2).ToList());
                if (moveStep.capturedPiece.HasValue || moveStep.lastPiece >= Piece.Pawn1)
                    turnsSincePawnMovedOrPieceTaken = 0;
                else
                    turnsSincePawnMovedOrPieceTaken++;
            }

            // TODO: Handle Game Over
        }

        public void SetBoardState(BoardState newState, List<Promotion> promos = null, int? turn = null)
        {
            BoardState defaultBoard = GetDefaultGame(defaultBoardStateFileLoc).turnHistory.FirstOrDefault();
            promotions = promos == null ? new List<Promotion>() : promos;
            foreach (var prefab in piecePrefabs)
            {
                IPiece piece;
                Jail applicableJail = jails[(int)prefab.Team];
                IPiece jailedPiece = applicableJail.GetPieceIfInJail(prefab.Piece);
                (Team team, Piece piece) pieceTeam = (prefab.Team, prefab.Piece);

                defaultBoard.TryGetIndex(pieceTeam, out Index startLoc);
                Vector3 loc = GetHexIfInBounds(startLoc.row, startLoc.col).transform.position + Vector3.up;
                if (activePieces.ContainsKey(pieceTeam))
                {
                    piece = activePieces[pieceTeam];
                    // Reset promoted pawn if needed
                    if (pieceTeam.piece >= Piece.Pawn1 && !(piece is Pawn))
                    {
                        if (turn.HasValue)
                        {
                            IEnumerable<Promotion> applicablePromos = promotions.Where(promo => promo.turnNumber <= turn.Value);
                            if (applicablePromos.Any())
                            {
                                Promotion promotion = applicablePromos.First();
                                GameObject properPromotedPrefab = piecePrefabs.Find(e => e.Team == promotion.team && e.Piece == promotion.to).Prefab;
                                if (properPromotedPrefab.GetComponent<IPiece>().GetType() != piece.GetType())
                                {
                                    // Piece is promoted wrong, change type
                                    IPiece old = activePieces[pieceTeam];
                                    activePieces.Remove(pieceTeam);
                                    Debug.Log("Pawn is promoted to the wrong piece.");
                                    Destroy(old.obj);
                                    piece = Instantiate(properPromotedPrefab, loc, Quaternion.identity).GetComponent<IPiece>();
                                    piece.Init(pieceTeam.team, pieceTeam.piece, startLoc);
                                    activePieces.Add(pieceTeam, piece);
                                }
                            }
                            else
                            {
                                // No applicable promo, return to pawn
                                IPiece old = activePieces[pieceTeam];
                                activePieces.Remove(pieceTeam);
                                Debug.Log("No applicable promo found, resetting promoted piece to pawn.");
                                Destroy(old.obj);
                                piece = Instantiate(prefab.Prefab, loc, Quaternion.identity).GetComponent<IPiece>();
                                piece.Init(pieceTeam.team, pieceTeam.piece, startLoc);
                                activePieces.Add(pieceTeam, piece);
                            }
                        }
                        else
                        {
                            // No turn was provided to check promo status, revert to pawn
                            IPiece old = activePieces[pieceTeam];
                            activePieces.Remove(pieceTeam);
                            Debug.Log("Revert to pawn.");
                            Destroy(old.obj);
                            piece = Instantiate(prefab.Prefab, loc, Quaternion.identity).GetComponent<IPiece>();
                            piece.Init(pieceTeam.team, pieceTeam.piece, startLoc);
                            activePieces.Add(pieceTeam, piece);
                        }

                    }
                }
                else if (jailedPiece != null)
                {
                    piece = jailedPiece;
                    applicableJail.RemoveFromPrison(piece);
                    // a piece coming out of jail needs to be added back into the Active Pieces dictionary
                    activePieces.Add(pieceTeam, piece);
                }
                else
                {
                    piece = Instantiate(prefab.Prefab, loc, Quaternion.identity).GetComponent<IPiece>();
                    piece.Init(pieceTeam.team, pieceTeam.piece, startLoc);
                    activePieces.Add(pieceTeam, piece);
                }

                // It might need to be promoted.
                // Do that before moving to avoid opening the promotiond dialogue when the pawn is moved to the promotion position
                piece = GetPromotedPieceIfNeeded(piece, promos != null, turn);

                // If the piece is on the board, place it at the correct location
                if (newState.TryGetIndex(pieceTeam, out Index newLoc))
                {
                    if (lastSetState.HasValue
                        && lastSetState.Value.TryGetPiece(newLoc, out (Team team, Piece piece) teamedPiece)
                        && teamedPiece != pieceTeam
                        && activePieces.ContainsKey(teamedPiece)
                    )
                    {
                        IPiece occupyingPiece = activePieces[teamedPiece];
                        if (newState.TryGetIndex((occupyingPiece.team, occupyingPiece.piece), out Index belongsAtLoc) && belongsAtLoc == newLoc)
                            Enprison(occupyingPiece, false);
                    }
                    piece.MoveTo(GetHexIfInBounds(newLoc.row, newLoc.col));
                    continue;
                }
                // Put the piece in the correct jail
                else
                {
                    applicableJail.Enprison(piece);
                    activePieces.Remove(pieceTeam);
                }
            }

            lastSetState = newState;
        }

        private IPiece GetPromotedPieceIfNeeded(IPiece piece, bool surpressNewPromotion = false, int? turn = null)
        {
            if (piece is Pawn pawn)
            {
                Piece p = pawn.piece;
                foreach (Promotion promo in promotions)
                {
                    if (promo.team == pawn.team && promo.from == p)
                    {
                        if (turn.HasValue)
                        {
                            if (turn.Value >= promo.turnNumber)
                                p = promo.to;
                            else
                                continue;
                        }
                        else
                            p = promo.to;
                    }
                }
                if (p != pawn.piece)
                    piece = Promote(pawn, p, surpressNewPromotion);
            }

            return piece;
        }

        public void AdvanceTurn(BoardState newState, bool updateTime = true, bool surpressAudio = false)
        {
            // IEnumerable<IPiece> checkingPieces = GetCheckingPieces(newState, newState.currentMove);
            Multiplayer multiplayer = GameObject.FindObjectOfType<Multiplayer>();
            float timestamp = Time.timeSinceLevelLoad + timeOffset;
            Team otherTeam = newState.currentMove == Team.White ? Team.Black : Team.White;

            if (updateTime)
                newState.executedAtTime = timestamp;

            if (multiplayer == null || multiplayer.gameParams.localTeam == newState.currentMove)
                newState = ResetCheck(newState);
            else
                newState = ResetCheck(newState);

            newState = CheckForCheckAndMate(newState, otherTeam, newState.currentMove);

            // Handle potential checkmate
            if (newState.checkmate != Team.None)
            {
                newState.currentMove = otherTeam;
                turnHistory.Add(newState);
                OnNewTurn.Invoke(newState);

                if (multiplayer)
                {
                    if (multiplayer.gameParams.localTeam == newState.checkmate)
                        multiplayer.SendGameEnd(timestamp, MessageType.Checkmate);
                    else
                        return;
                }

                EndGame(
                    timestamp,
                    endType: GameEndType.Checkmate,
                    winner: newState.checkmate == Team.White ? Winner.Black : Winner.White
                );
                return;
            }

            // When another player has 0 valid moves, a stalemate has occured
            bool isStalemate = true;
            IEnumerable<KeyValuePair<(Team, Piece), IPiece>> otherTeamPieces = activePieces.Where(piece => piece.Key.Item1 == otherTeam);
            foreach (KeyValuePair<(Team, Piece), IPiece> otherTeamPiece in otherTeamPieces)
            {
                IEnumerable<(Index, MoveType)> validMoves = GetAllValidMovesForPiece(otherTeamPiece.Value, newState);
                if (validMoves.Any())
                {
                    isStalemate = false;
                    break;
                }
            }

            // Handle potential stalemate
            if (isStalemate)
            {
                if (multiplayer)
                {
                    if (multiplayer.gameParams.localTeam == otherTeam)
                        multiplayer.SendGameEnd(timestamp, MessageType.Stalemate);
                    else
                        return;
                }

                newState.currentMove = otherTeam;
                turnHistory.Add(newState);

                Move move = BoardState.GetLastMove(turnHistory);
                OnNewTurn.Invoke(newState);

                EndGame(timestamp, GameEndType.Stalemate, Winner.None);
                return;
            }

            // Check for insufficient material, stalemate if both teams have insufficient material
            IEnumerable<Piece> whitePieces = GetRemainingPieces(Team.White, newState);
            IEnumerable<Piece> blackPieces = GetRemainingPieces(Team.Black, newState);
            bool whiteSufficient = true;
            bool blackSufficient = true;

            foreach (List<Piece> insufficientSet in insufficientSets)
            {
                whiteSufficient = whiteSufficient ? whitePieces.Except(insufficientSet).Any() : false;
                blackSufficient = blackSufficient ? blackPieces.Except(insufficientSet).Any() : false;
            }

            if (!whiteSufficient && !blackSufficient)
            {
                newState.currentMove = otherTeam;
                turnHistory.Add(newState);

                Move move = BoardState.GetLastMove(turnHistory);
                OnNewTurn.Invoke(newState);

                EndGame(timestamp, GameEndType.Stalemate, Winner.None);
                return;
            }

            newState.currentMove = otherTeam;

            // Check for 5 fold repetition
            // When the same board state occurs 5 times in a game, the game ends in a draw
            IEnumerable<BoardState> repetition = turnHistory.Where(state => state == newState);
            if (repetition.Count() >= 5)
            {
                turnHistory.Add(newState);

                if (multiplayer != null)
                {
                    multiplayer.ClaimDraw();
                    return;
                }

                OnNewTurn.Invoke(newState);
                EndGame(timestamp, GameEndType.Draw, Winner.Draw);
                return;
            }

            turnHistory.Add(newState);

            Move newMove = BoardState.GetLastMove(turnHistory);

            OnNewTurn.Invoke(newState);

            // The game ends in a draw due to 50 move rule (50 turns of both teams playing with no captured piece, or moved pawn)
            if (newMove.capturedPiece.HasValue || newMove.lastPiece >= Piece.Pawn1)
                turnsSincePawnMovedOrPieceTaken = 0;
            else
                turnsSincePawnMovedOrPieceTaken++;

            if (turnsSincePawnMovedOrPieceTaken == 100f)
            {
                EndGame(timestamp, GameEndType.Draw, Winner.Draw);
                return;
            }
        }

        public BoardState MovePiece(IPiece piece, Index targetLocation, BoardState boardState, bool isQuery = false, bool includeBlocking = false)
        {
            // Copy the existing board state
            BoardState currentState = boardState;
            // BidirectionalDictionary<(Team, Piece), Index> allPiecePositions = new BidirectionalDictionary<(Team, Piece), Index>(boardState.allPiecePositions);
            BidirectionalDictionary<(Team, Piece), Index> allPiecePositions = boardState.allPiecePositions.Clone();

            // If the hex being moved into contains an enemy piece, capture it
            Piece? takenPieceAtLocation = null;
            Piece? defendedPieceAtLocation = null;

            if (currentState.TryGetPiece(targetLocation, out (Team occupyingTeam, Piece occupyingType) teamedPiece))
            {
                if (teamedPiece.occupyingTeam != piece.team || includeBlocking)
                {
                    takenPieceAtLocation = teamedPiece.occupyingType;
                    IPiece occupyingPiece = activePieces[teamedPiece];

                    // Capture the enemy piece
                    if (!isQuery)
                    {
                        jails[(int)teamedPiece.occupyingTeam].Enprison(occupyingPiece);
                        activePieces.Remove(teamedPiece);
                    }
                    allPiecePositions.Remove(teamedPiece);
                }
                else
                    defendedPieceAtLocation = teamedPiece.occupyingType;
            }

            // Move piece
            if (!isQuery)
            {
                piece.MoveTo(GetHexIfInBounds(targetLocation));
            }

            // Update boardstate
            if (allPiecePositions.ContainsKey((piece.team, piece.piece)))
                allPiecePositions.Remove((piece.team, piece.piece));
            if (allPiecePositions.ContainsKey(targetLocation))
                allPiecePositions.Remove(targetLocation);
            allPiecePositions.Add((piece.team, piece.piece), targetLocation);
            currentState.allPiecePositions = allPiecePositions;

            return currentState;
        }

        public BoardState Swap(IPiece p1, IPiece p2, BoardState boardState, bool isQuery = false)
        {
            if (p1 == p2)
                return boardState;

            Index p1StartLoc = p1.location;
            Index p2StartLoc = p2.location;
            BoardState currentState = boardState;

            if (!isQuery)
            {
                p1.MoveTo(GetHexIfInBounds(p2.location));
                p2.MoveTo(GetHexIfInBounds(p1StartLoc));
            }

            BidirectionalDictionary<(Team, Piece), Index> allPiecePositions = currentState.allPiecePositions.Clone();
            allPiecePositions.Remove((p1.team, p1.piece));
            allPiecePositions.Remove((p2.team, p2.piece));
            allPiecePositions.Add((p1.team, p1.piece), p2StartLoc);
            allPiecePositions.Add((p2.team, p2.piece), p1StartLoc);

            currentState.allPiecePositions = allPiecePositions;

            return currentState;
        }

        private BoardState ResetCheck(BoardState newState)
        {
            if (turnHistory.Count > 0)
            {
                BoardState oldState = turnHistory[turnHistory.Count - 1];
                if (oldState.check != Team.None)
                {
                    newState.check = Team.None;
                    newState.checkmate = Team.None;
                }
            }
            else
            {
                newState.check = Team.None;
                newState.checkmate = Team.None;
            }
            return newState;
        }

        public bool IsChecking(BoardState boardState, Team checkForTeam)
        {
            return boardState.IsChecking(checkForTeam, promotions);
        }

        private BoardState CheckForCheckAndMate(BoardState newState, Team otherTeam, Team t)
        {
            if (IsChecking(newState, t))
            {
                List<(Index, MoveType)> validMoves = new List<(Index, MoveType)>();
                // Check for mate
                foreach (KeyValuePair<(Team, Piece), IPiece> kvp in activePieces)
                {
                    (Team team, Piece piece) = kvp.Key;
                    if (team == newState.currentMove)
                        continue;
                    IEnumerable<(Index, MoveType)> vm = GetAllValidMovesForPiece(kvp.Value, newState);
                    validMoves.AddRange(vm);
                }
                if (validMoves.Count == 0)
                    newState.checkmate = otherTeam;
                else
                    newState.check = otherTeam;
            }

            return newState;
        }

        public void Enprison(IPiece toPrison, bool updateState = true)
        {
            jails[(int)toPrison.team].Enprison(toPrison);
            activePieces.Remove((toPrison.team, toPrison.piece));

            if (updateState)
            {
                BoardState currentState = turnHistory.Last();
                BidirectionalDictionary<(Team, Piece), Index> allPiecePositions = currentState.allPiecePositions.Clone();
                allPiecePositions.Remove((toPrison.team, toPrison.piece));
                currentState.allPiecePositions = allPiecePositions;
                AdvanceTurn(currentState);
            }
        }

        public IPiece Promote(Pawn pawn, Piece type, bool surpressNewPromotion = false)
        {
            // Replace the pawn with the chosen piece type
            // Worth noting: Even though the new IPiece is of a different type than Pawn,
            // we still use the PieceType.Pawn# (read from the pawn) to store it's position in the game state to maintain it's unique key
            Hex hex = GetHexIfInBounds(pawn.location);

            IPiece newPiece = Instantiate(piecePrefabs.Find(e => e.Team == pawn.team && e.Piece == type).Prefab, hex.transform.position + Vector3.up, Quaternion.identity).GetComponent<IPiece>();
            newPiece.Init(pawn.team, pawn.piece, pawn.location);
            if (!surpressNewPromotion)
            {
                Promotion newPromo = new Promotion(pawn.team, pawn.piece, type, Mathf.FloorToInt((float)turnHistory.Count / 2f) + 1);
                promotions.Add(newPromo);
            }
            activePieces[(pawn.team, pawn.piece)] = newPiece;
            Destroy(pawn.gameObject);
            return newPiece;
        }

        public BoardState EnPassant(Pawn pawn, Team enemyTeam, Piece enemyPiece, Index targetHex, BoardState boardState, bool isQuery = false)
        {
            if (!isQuery)
            {
                IPiece enemyIPiece = activePieces[(enemyTeam, enemyPiece)];
                activePieces.Remove((enemyTeam, enemyPiece));
                // Capture enemy
                jails[(int)enemyTeam].Enprison(enemyIPiece);
                // Move pawn
                pawn.MoveTo(GetHexIfInBounds(targetHex));
            }

            // Update board state
            BoardState currentState = boardState;
            BidirectionalDictionary<(Team, Piece), Index> allPiecePositions = currentState.allPiecePositions.Clone();
            allPiecePositions.Remove((enemyTeam, enemyPiece));
            allPiecePositions.Remove((pawn.team, pawn.piece));
            allPiecePositions.Add((pawn.team, pawn.piece), targetHex);

            currentState.allPiecePositions = allPiecePositions;
            return currentState;
        }

        public void EndGame(float timestamp, GameEndType endType = GameEndType.Pending, Winner winner = Winner.Pending)
        {
            BoardState currentState = turnHistory.Last();
            if (currentState.currentMove == Team.None)
                return;

            currentState.currentMove = Team.None;
            currentState.executedAtTime = timestamp;
            turnHistory.Add(currentState);
            OnNewTurn.Invoke(currentState);

            game = new Game(
                turnHistory,
                promotions,
                winner,
                endType
            );

            OnGameOver.Invoke(game);
        }

        public IEnumerable<(Index target, MoveType moveType)> GetAllValidMovesForPiece(IPiece piece, BoardState boardState, bool includeBlocking = false)
        {
            IEnumerable<(Index, MoveType)> possibleMoves = piece.GetAllPossibleMoves(boardState, includeBlocking);
            return ValidateMoves(possibleMoves, piece, boardState, includeBlocking);
        }

        public IEnumerable<Index> GetAllValidAttacksForPieceConcerningHex(IPiece piece, BoardState boardState, Index hexIndex, bool includeBlocking = false)
        {
            IEnumerable<(Index target, MoveType moveType)> possibleMoves = piece.GetAllPossibleMoves(boardState, includeBlocking)
                .Where(kvp => kvp.target != null && kvp.target == hexIndex)
                .Where(kvp => kvp.moveType == MoveType.Attack || kvp.moveType == MoveType.EnPassant);

            return ValidateMoves(possibleMoves, piece, boardState, includeBlocking).Select(kvp => kvp.target);
        }

        IEnumerable<Piece> GetRemainingPieces(Team team, BoardState state) =>
        state.allPiecePositions.Where(kvp => kvp.Key.Item1 == team).Select(kvp =>
        {
            IEnumerable<Promotion> applicablePromos = promotions.Where(promo => promo.from == kvp.Key.Item2 && promo.team == team);
            if (applicablePromos.Any())
                return applicablePromos.First().to;
            return kvp.Key.Item2;
        });

        public IEnumerable<(Index target, MoveType moveType)> ValidateMoves(IEnumerable<(Index target, MoveType moveType)> possibleMoves, IPiece piece, BoardState boardState, bool includeBlocking = false)
        {
            foreach (var possibleMove in possibleMoves)
            {
                (Index possibleHex, MoveType possibleMoveType) = possibleMove;

                BoardState newState;
                if (possibleMoveType == MoveType.Move || possibleMoveType == MoveType.Attack)
                    newState = MovePiece(piece, possibleHex, boardState, true, includeBlocking);
                else if (possibleMoveType == MoveType.Defend)
                    newState = Swap(piece, activePieces[boardState.allPiecePositions[possibleHex]], boardState, true);
                else if (possibleMoveType == MoveType.EnPassant)
                {
                    Index? enemyLoc = HexGrid.GetNeighborAt(possibleHex, piece.team == Team.White ? HexNeighborDirection.Down : HexNeighborDirection.Up);
                    Index? enemyStartLoc = HexGrid.GetNeighborAt(possibleHex, piece.team == Team.White ? HexNeighborDirection.Up : HexNeighborDirection.Down);
                    if (!enemyLoc.HasValue || !enemyStartLoc.HasValue)
                    {
                        // Debug.LogError($"Invalid hex for EnPassant on {possibleHex}");
                        continue;
                    }
                    if (!boardState.allPiecePositions.TryGetValue(enemyLoc.Value, out (Team team, Piece piece) enemy))
                    {
                        Debug.LogError($"Could not find enemy to capture for EnPassant on {possibleHex}");
                        continue;
                    }
                    BoardState previousBoardState = turnHistory[turnHistory.Count - 2];
                    if (!previousBoardState.IsOccupiedBy(enemyStartLoc.Value, enemy))
                        continue;
                    newState = EnPassant((Pawn)piece, enemy.team, enemy.piece, possibleHex, boardState, true);
                }
                else
                {
                    Debug.LogWarning($"Unhandled move type {possibleMoveType}");
                    continue;
                }

                Team otherTeam = piece.team == Team.White ? Team.Black : Team.White;
                // If any piece is checking, the move is invalid, remove it from the list of possible moves
                if (!IsChecking(newState, otherTeam))
                    yield return (possibleMove.target, possibleMove.moveType);
            }
        }

        public IEnumerable<IPiece> GetValidAttacksConcerningHex(Hex hex) => activePieces
            .Where(kvp => GetAllValidAttacksForPieceConcerningHex(kvp.Value, turnHistory.Last(), hex.index, true)
                .Any(targetIndex => targetIndex == hex.index)
            ).Select(kvp => kvp.Value);

        public Hex GetNeighborAt(Index source, HexNeighborDirection direction)
        {
            Index? neighbor = HexGrid.GetNeighborAt(source, direction);
            if (neighbor.HasValue)
                return GetHexIfInBounds(neighbor.Value);
            return null;
        }
        public Hex GetHexIfInBounds(int row, int col) =>
            HexGrid.IsInBounds(row, col) ? hexes[row][col] : null;
        public Hex GetHexIfInBounds(Index index) =>
            GetHexIfInBounds(index.row, index.col);

        public bool TryGetHexIfInBounds(int row, int col, out Hex hex)
        {
            hex = GetHexIfInBounds(row, col);
            return hex != null;
        }
        public bool TryGetHexIfInBounds(Index index, out Hex hex)
        {
            hex = GetHexIfInBounds(index);
            return hex != null;
        }

        public IEnumerable<Hex> GetHexesInCol(int col)
        {
            List<Hex> hexesInCol = new List<Hex>();
            for (int i = 0; i < hexes.Count; i++)
            {
                for (int j = 0; j < hexes[i].Count; j++)
                {
                    if (j == col)
                    {
                        Hex hex = GetHexIfInBounds(i, j);
                        if (hex != null)
                            hexesInCol.Add(hex);
                    }
                }
            }
            return hexesInCol;
        }
    }
}