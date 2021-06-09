using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using System.IO;
using System;

public class Board : SerializedMonoBehaviour
{
    [SerializeField] private PromotionDialogue promotionDialogue;
    [SerializeField] private LastMoveTracker moveTracker;
    [SerializeField] private TurnPanel turnPanel;
    [SerializeField] private Timers timers;
    [SerializeField] private SmoothHalfOrbitalCamera cam;
    [SerializeField] private FreePlaceModeToggle freePlaceMode;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private TurnHistoryPanel turnHistoryPanel;
    public AudioClip moveClip;
    public AudioClip winFanfare;
    public List<Jail> jails = new List<Jail>();
    [SerializeField] private GameObject hexPrefab;
    public Dictionary<(Team, Piece), GameObject> piecePrefabs = new Dictionary<(Team, Piece), GameObject>();
    public Game game;
    public List<BoardState> turnHistory = new List<BoardState>();
    [ReadOnly] public Dictionary<(Team, Piece), IPiece> activePieces = new Dictionary<(Team, Piece), IPiece>();
    public delegate void NewTurn(BoardState newState);
    [HideInInspector] public NewTurn newTurn;
    public delegate void GameOver(Game game);
    [HideInInspector] public GameOver gameOver;
    [SerializeField] public HexGrid hexGrid;
    [OdinSerialize] public List<List<Hex>> hexes = new List<List<Hex>>();
    List<Hex> highlightedHexes = new List<Hex>();
    [ReadOnly] public readonly string defaultBoardStateFileLoc = "DefaultBoardState";
    [ReadOnly] public List<Promotion> promotions = new List<Promotion>();
    public Color lastMoveHighlightColor;
    public float timeOffset {get; private set;} = 0f;
    public int turnsSincePawnMovedOrPieceTaken = 0;
    [OdinSerialize] public List<List<Piece>> insufficientSets = new List<List<Piece>>();

    private BoardState? lastSetState = null;

    // Used to write the default boardstate out to file
    [Button]
    public void WriteTurnHistoryToFile()
    {
        Game game = new Game(turnHistory, promotions);
        string json = game.Serialize();
        File.WriteAllText("Assets/Resources/" + defaultBoardStateFileLoc + ".json", json);
        Debug.Log($"Wrote to file: {defaultBoardStateFileLoc}");
    }
    // private void Awake() => SetBoardState(turnHistory[turnHistory.Count - 1]);

    private void Awake() => LoadGame(GetDefaultGame(defaultBoardStateFileLoc));
    private void Start() => newTurn.Invoke(turnHistory[turnHistory.Count - 1]);

    public void SetBoardState(BoardState newState, List<Promotion> promos = null, int? turn = null)
    {
        BoardState defaultBoard = GetDefaultGame(defaultBoardStateFileLoc).turnHistory.FirstOrDefault();
        promotions = promos == null ? new List<Promotion>() : promos;
        foreach(KeyValuePair<(Team team, Piece piece), GameObject> prefab in piecePrefabs)
        {
            IPiece piece;
            Jail applicableJail = jails[(int)prefab.Key.team];
            IPiece jailedPiece = applicableJail.GetPieceIfInJail(prefab.Key.piece);

            defaultBoard.TryGetIndex(prefab.Key, out Index startLoc);
            Vector3 loc = GetHexIfInBounds(startLoc.row, startLoc.col).transform.position + Vector3.up;
            if(activePieces.ContainsKey(prefab.Key))
            {
                piece = activePieces[prefab.Key];
                // Reset promoted pawn if needed
                if(prefab.Key.piece >= Piece.Pawn1 && !(piece is Pawn))
                {
                    if(turn.HasValue)
                    {
                        IEnumerable<Promotion> applicablePromos = promotions.Where(promo => promo.turnNumber <= turn.Value);
                        if(applicablePromos.Any())
                        {
                            Promotion promotion = applicablePromos.First();
                            GameObject properPromotedPrefab = piecePrefabs[(promotion.team, promotion.to)];
                            if(properPromotedPrefab.GetComponent<IPiece>().GetType() != piece.GetType())
                            {
                                // Piece is promoted wrong, change type
                                IPiece old = activePieces[prefab.Key];
                                activePieces.Remove(prefab.Key);
                                Debug.Log("Pawn is promoted to the wrong piece.");
                                Destroy(old.obj);
                                piece = Instantiate(properPromotedPrefab, loc, Quaternion.identity).GetComponent<IPiece>();
                                piece.Init(prefab.Key.team, prefab.Key.piece, startLoc);
                                activePieces.Add(prefab.Key, piece);
                            }   
                        }
                        else
                        {
                            // No applicable promo, return to pawn
                            IPiece old = activePieces[prefab.Key];
                            activePieces.Remove(prefab.Key);
                            Debug.Log("No applicable promo found, resetting promoted piece to pawn.");
                            Destroy(old.obj);
                            piece = Instantiate(prefab.Value, loc, Quaternion.identity).GetComponent<IPiece>();
                            piece.Init(prefab.Key.team, prefab.Key.piece, startLoc);
                            activePieces.Add(prefab.Key, piece);
                        }
                    }
                    else
                    {
                        // No turn was provided to check promo status, revert to pawn
                        IPiece old = activePieces[prefab.Key];
                        activePieces.Remove(prefab.Key);
                        Debug.Log("Revert to pawn.");
                        Destroy(old.obj);
                        piece = Instantiate(prefab.Value, loc, Quaternion.identity).GetComponent<IPiece>();
                        piece.Init(prefab.Key.team, prefab.Key.piece, startLoc);
                        activePieces.Add(prefab.Key, piece);
                    }

                }
            }
            else if(jailedPiece != null)
            {
                piece = jailedPiece;
                applicableJail.RemoveFromPrison(piece);
                // a piece coming out of jail needs to be added back into the Active Pieces dictionary
                activePieces.Add(prefab.Key, piece);
            }
            else
            {
                piece = Instantiate(prefab.Value, loc, Quaternion.identity).GetComponent<IPiece>();
                piece.Init(prefab.Key.team, prefab.Key.piece, startLoc);
                activePieces.Add(prefab.Key, piece);
            }
            
            // It might need to be promoted.
            // Do that before moving to avoid opening the promotiond dialogue when the pawn is moved to the promotion position
            piece = GetPromotedPieceIfNeeded(piece, promos != null, turn);
            
            // If the piece is on the board, place it at the correct location
            if(newState.TryGetIndex(prefab.Key, out Index newLoc))
            {
                if(lastSetState.HasValue 
                    && lastSetState.Value.TryGetPiece(newLoc, out (Team team, Piece piece) teamedPiece) 
                    && teamedPiece != prefab.Key 
                    && activePieces.ContainsKey(teamedPiece)
                ){
                    IPiece occupyingPiece = activePieces[teamedPiece];
                    if(newState.TryGetIndex((occupyingPiece.team, occupyingPiece.piece), out Index belongsAtLoc) && belongsAtLoc == newLoc)
                        Enprison(occupyingPiece, false);
                }
                piece.MoveTo(GetHexIfInBounds(newLoc.row, newLoc.col));
                continue;
            }
            // Put the piece in the correct jail
            else
            {
                applicableJail.Enprison(piece);
                activePieces.Remove(prefab.Key);
            }
        }

        if(newState.currentMove != Team.None && turnHistory.Count > 1)
        {
            Move newMove = BoardState.GetLastMove(turnHistory);
            if(newMove.lastTeam != Team.None)
                HighlightMove(newMove);
            else
                ClearMoveHighlight();
        }
        else
            ClearMoveHighlight();

        lastSetState = newState; 
    }

    public void LoadGame(Game game)
    {
        turnHistory = game.turnHistory;
        this.game = game;        

        foreach(Jail jail in jails)
            jail?.Clear();

        BoardState state = turnHistory[turnHistory.Count - 1];

        if(turnHistory.Count > 1)
            timeOffset = state.executedAtTime - Time.timeSinceLevelLoad;
        
        if(game.timerDuration <= 0)
        {
            timers.gameObject.SetActive(game.hasClock);
            timers.isClock = game.hasClock;
        }
        else
        {
            timers.gameObject.SetActive(true);
            timers.SetTimers(game.timerDuration);
        }
    
        SetBoardState(state, game.promotions);
        turnHistoryPanel.SetGame(game);
        
        Move move = BoardState.GetLastMove(turnHistory);
        if(move.lastTeam != Team.None)
            moveTracker.UpdateText(move);

        // When loading a game, we need to count how many turns have passed towards the 50 move rule
        turnsSincePawnMovedOrPieceTaken = 0;
        for(int i = 0; i < turnHistory.Count - 1; i++)
        {
            Move moveStep = BoardState.GetLastMove(turnHistory.Skip(i).Take(2).ToList());
            if(moveStep.capturedPiece.HasValue || moveStep.lastPiece >= Piece.Pawn1)
                turnsSincePawnMovedOrPieceTaken = 0;
            else
                turnsSincePawnMovedOrPieceTaken++;
        }

        // game.endType may not exist in older game saves, this bit of code supports both new and old save styles
        if(game.endType != GameEndType.Pending)
            gameOver?.Invoke(game);
        else
        {
            if(game.winner == Winner.Pending)
            {
                turnPanel.Reset();
                newTurn?.Invoke(state);
                cam.SetToTeam(state.currentMove);
            }
            else
                gameOver?.Invoke(game);
        }
    }

    public Game GetDefaultGame(string loc) =>
        Game.Deserialize(((TextAsset)Resources.Load(loc, typeof(TextAsset))).text);

    public Team GetCurrentTurn()
    {
        if(promotionDialogue.gameObject.activeSelf)
            return Team.None;

        return turnHistory[turnHistory.Count - 1].currentMove;
    }
 
    public BoardState GetCurrentBoardState() => turnHistoryPanel.TryGetCurrentBoardState(out BoardState state) 
        ? state 
        : turnHistory[turnHistory.Count - 1];

    public void AdvanceTurn(BoardState newState, bool updateTime = true, bool surpressAudio = false)
    {
        if(!surpressAudio)
            audioSource.PlayOneShot(moveClip);

        // IEnumerable<IPiece> checkingPieces = GetCheckingPieces(newState, newState.currentMove);
        Multiplayer multiplayer = GameObject.FindObjectOfType<Multiplayer>();
        float timestamp = Time.timeSinceLevelLoad + timeOffset;
        Team otherTeam = newState.currentMove == Team.White ? Team.Black : Team.White;

        if(updateTime)
            newState.executedAtTime = timestamp;

        if(multiplayer == null || multiplayer.gameParams.localTeam == newState.currentMove)
            newState = ResetCheck(newState);
        else
            newState = ResetCheck(newState);
        
        newState = CheckForCheckAndMate(newState, otherTeam, newState.currentMove);

        // Handle potential checkmate
        if(newState.checkmate != Team.None)
        {
            newState.currentMove = otherTeam;
            turnHistory.Add(newState);
            newTurn.Invoke(newState);

            if(multiplayer)
            {
                if(multiplayer.gameParams.localTeam == newState.checkmate)
                    multiplayer.SendGameEnd(timestamp, MessageType.Checkmate);
                else
                    return;
            }

            Move move = BoardState.GetLastMove(turnHistory);
            HighlightMove(move);

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
        foreach(KeyValuePair<(Team, Piece), IPiece> otherTeamPiece in otherTeamPieces)
        {
            IEnumerable<(Index, MoveType)> validMoves = GetAllValidMovesForPiece(otherTeamPiece.Value, newState);
            if(validMoves.Any())
            {
                isStalemate = false;
                break;
            }
        }

        // Handle potential stalemate
        if(isStalemate)
        {
            if(multiplayer)
            {
                if(multiplayer.gameParams.localTeam == otherTeam)
                    multiplayer.SendGameEnd(timestamp, MessageType.Stalemate);
                else
                    return;
            }

            newState.currentMove = otherTeam;
            turnHistory.Add(newState);

            Move move = BoardState.GetLastMove(turnHistory);
            if(move.lastTeam != Team.None)
                HighlightMove(move);
            else
                ClearMoveHighlight();

            newTurn.Invoke(newState);

            EndGame(timestamp, GameEndType.Stalemate, Winner.None);
            return;
        }

        // Check for insufficient material, stalemate if both teams have insufficient material
        IEnumerable<Piece> whitePieces = GetRemainingPieces(Team.White, newState);
        IEnumerable<Piece> blackPieces = GetRemainingPieces(Team.Black, newState);
        bool whiteSufficient = true;
        bool blackSufficient = true;

        foreach(List<Piece> insufficientSet in insufficientSets)
        {
            whiteSufficient = whiteSufficient ? whitePieces.Except(insufficientSet).Any() : false;
            blackSufficient = blackSufficient ? blackPieces.Except(insufficientSet).Any() : false;
        }

        if(!whiteSufficient && !blackSufficient)
        {
            if(multiplayer)
            {
                if(multiplayer.gameParams.localTeam == otherTeam)
                    multiplayer.SendGameEnd(timestamp, MessageType.Stalemate);
                else
                    return;
            }
            else if(!freePlaceMode.toggle.isOn)
            {
                newState.currentMove = otherTeam;
                turnHistory.Add(newState);

                Move move = BoardState.GetLastMove(turnHistory);
                if (move.lastTeam != Team.None)
                    HighlightMove(move);
                else
                    ClearMoveHighlight();

                newTurn.Invoke(newState);

                EndGame(timestamp, GameEndType.Stalemate, Winner.None);
                return;
            }
        }

        newState.currentMove = otherTeam;

        // Check for 5 fold repetition
        // When the same board state occurs 5 times in a game, the game ends in a draw
        IEnumerable<BoardState> repetition = turnHistory.Where(state => state == newState);
        if(repetition.Count() >= 5)
        {
            turnHistory.Add(newState);

            if(multiplayer != null)
            {
                multiplayer.ClaimDraw();
                return;
            }

            newTurn.Invoke(newState);
            EndGame(timestamp, GameEndType.Draw, Winner.Draw);
            return;
        }

        turnHistory.Add(newState);

        Move newMove = BoardState.GetLastMove(turnHistory);
        if(newMove.lastTeam != Team.None)
            HighlightMove(newMove);
        else
            ClearMoveHighlight();

        newTurn.Invoke(newState);

        // The game ends in a draw due to 50 move rule (50 turns of both teams playing with no captured piece, or moved pawn)
        if(newMove.capturedPiece.HasValue || newMove.lastPiece >= Piece.Pawn1)
            turnsSincePawnMovedOrPieceTaken = 0;
        else
            turnsSincePawnMovedOrPieceTaken++;

        if(turnsSincePawnMovedOrPieceTaken == 100f)
        {
            if(multiplayer != null)
            {
                multiplayer.ClaimDraw();
                return;
            }

            EndGame(timestamp, GameEndType.Draw, Winner.Draw);
            return;
        }

        // In sandbox mode, flip the camera when the turn passes if the toggle is on
        if(multiplayer == null)
        {
            FlipCameraToggle flipCameraToggle = GameObject.FindObjectOfType<FlipCameraToggle>();
            if(flipCameraToggle != null && flipCameraToggle.toggle.isOn)
                cam.SetToTeam(newState.currentMove);
        }
    }

    private BoardState ResetCheck(BoardState newState)
    {
        if(turnHistory.Count > 0)
        {
            BoardState oldState = turnHistory[turnHistory.Count - 1];
            if(oldState.check != Team.None)
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

    private BoardState CheckForCheckAndMate(BoardState newState, Team otherTeam, Team t)
    {
        if(IsChecking(newState, t))
        {
            List<(Index, MoveType)> validMoves = new List<(Index, MoveType)>();
            // Check for mate
            foreach(KeyValuePair<(Team, Piece), IPiece> kvp in activePieces)
            {
                (Team team, Piece piece) = kvp.Key;
                if(team == newState.currentMove)
                    continue;
                IEnumerable<(Index, MoveType)> vm = GetAllValidMovesForPiece(kvp.Value, newState);
                validMoves.AddRange(vm);
            }
            if(validMoves.Count == 0)
                newState.checkmate = otherTeam;
            else
                newState.check = otherTeam;
        }

        return newState;
    }

    IEnumerable<Piece> GetRemainingPieces(Team team, BoardState state) =>
        state.allPiecePositions.Where(kvp => kvp.Key.Item1 == team).Select(kvp => {
            IEnumerable<Promotion> applicablePromos = promotions.Where(promo => promo.from == kvp.Key.Item2 && promo.team == team);
            if(applicablePromos.Any())
                return applicablePromos.First().to;
            return kvp.Key.Item2;
        });

    public IEnumerable<(Index target, MoveType moveType)> ValidateMoves(IEnumerable<(Index target, MoveType moveType)> possibleMoves, IPiece piece, BoardState boardState, bool includeBlocking = false)
    {
        foreach(var possibleMove in possibleMoves)
        {
            (Index possibleHex, MoveType possibleMoveType) = possibleMove;

            BoardState newState;
            if(possibleMoveType == MoveType.Move || possibleMoveType == MoveType.Attack)
                newState = MovePiece(piece, possibleHex, boardState, true, includeBlocking);
            else if(possibleMoveType == MoveType.Defend)
                newState = Swap(piece, activePieces[boardState.allPiecePositions[possibleHex]], boardState, true);
            else if(possibleMoveType == MoveType.EnPassant)
            {
                Index? enemyLoc = HexGrid.GetNeighborAt(possibleHex, piece.team == Team.White ? HexNeighborDirection.Down : HexNeighborDirection.Up);
                Index? enemyStartLoc = HexGrid.GetNeighborAt(possibleHex, piece.team == Team.White ? HexNeighborDirection.Up : HexNeighborDirection.Down);
                if(!enemyLoc.HasValue || !enemyStartLoc.HasValue)
                {
                    // Debug.LogError($"Invalid hex for EnPassant on {possibleHex}");
                    continue;
                }
                if(!boardState.allPiecePositions.TryGetValue(enemyLoc.Value, out (Team team, Piece piece) enemy))
                {
                    Debug.LogError($"Could not find enemy to capture for EnPassant on {possibleHex}");
                    continue;
                }
                BoardState previousBoardState = turnHistory[turnHistory.Count - 2];
                if(!previousBoardState.IsOccupiedBy(enemyStartLoc.Value, enemy))
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

    public IEnumerable<IPiece> GetValidAttacksConcerningHex(Hex hex) => activePieces
        .Where(kvp => GetAllValidAttacksForPieceConcerningHex(kvp.Value, GetCurrentBoardState(), hex.index, true)
            .Any(targetIndex => targetIndex == hex.index)
        ).Select(kvp => kvp.Value);

    public IEnumerable<IPiece> GetCheckingPieces(BoardState boardState, Team checkForTeam)
    {
        Team otherTeam = checkForTeam == Team.White ? Team.Black : Team.White;

        return activePieces
        .Where(kvp => kvp.Key.Item1 == checkForTeam
            && boardState.allPiecePositions.ContainsKey(kvp.Key)
            && kvp.Value.GetAllPossibleMoves(boardState)
                .Any(move =>
                    move.Item2 == MoveType.Attack
                    && boardState.allPiecePositions.ContainsKey(move.Item1)
                    && boardState.allPiecePositions[move.Item1] == (otherTeam, Piece.King)
                )
        ).Select(kvp => kvp.Value);
    }
    
    public bool IsChecking(BoardState boardState, Team checkForTeam)
    {
        return boardState.IsChecking(checkForTeam, promotions);
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
        
        if(currentState.TryGetPiece(targetLocation, out (Team occupyingTeam, Piece occupyingType) teamedPiece))
        {
            if(teamedPiece.occupyingTeam != piece.team || includeBlocking)
            {
                takenPieceAtLocation = teamedPiece.occupyingType;
                IPiece occupyingPiece = activePieces[teamedPiece];

                // Capture the enemy piece
                if(!isQuery)
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
        if(!isQuery)
        {
            moveTracker.UpdateText(new Move(
                Mathf.FloorToInt((float)turnHistory.Count / 2f) + 1,
                piece.team,
                piece.piece,
                piece.location,
                targetLocation,
                takenPieceAtLocation,
                defendedPieceAtLocation
            ));
            piece.MoveTo(GetHexIfInBounds(targetLocation));
        }

        // Update boardstate
        if(allPiecePositions.ContainsKey((piece.team, piece.piece)))
            allPiecePositions.Remove((piece.team, piece.piece));
        if(allPiecePositions.ContainsKey(targetLocation))
            allPiecePositions.Remove(targetLocation);
        allPiecePositions.Add((piece.team, piece.piece), targetLocation);
        currentState.allPiecePositions = allPiecePositions;
        
        return currentState;
    }

    public void MovePieceForPromotion(IPiece piece, Hex targetLocation, BoardState boardState)
    {
        // Copy the existing board state
        BoardState currentState = boardState;
        BidirectionalDictionary<(Team, Piece), Index> allPiecePositions = boardState.allPiecePositions.Clone();
        
        // If the hex being moved into contains an enemy piece, capture it
        Piece? takenPieceAtLocation = null;
        Piece? defendedPieceAtLocation = null;
        if(currentState.allPiecePositions.Contains(targetLocation.index))
        {
            (Team occupyingTeam, Piece occupyingType) = currentState.allPiecePositions[targetLocation.index];
            if(occupyingTeam != piece.team)
            {
                takenPieceAtLocation = occupyingType;
                IPiece occupyingPiece = activePieces[(occupyingTeam, occupyingType)];

                // Capture the enemy piece
                jails[(int)occupyingTeam].Enprison(occupyingPiece);
                activePieces.Remove((occupyingTeam, occupyingType));
                allPiecePositions.Remove((occupyingTeam, occupyingType));
            }
            else
                defendedPieceAtLocation = occupyingType;
        }

        // Move piece
        moveTracker.UpdateText(new Move(
            Mathf.FloorToInt((float)turnHistory.Count / 2f) + 1,
            piece.team,
            piece.piece,
            piece.location,
            targetLocation.index,
            takenPieceAtLocation,
            defendedPieceAtLocation
        ));
        piece.MoveTo(targetLocation, () => {
            // Update boardstate
            if(allPiecePositions.ContainsKey((piece.team, piece.piece)))
                allPiecePositions.Remove((piece.team, piece.piece));
            allPiecePositions.Add((piece.team, piece.piece), targetLocation.index);
            currentState.allPiecePositions = allPiecePositions;

            AdvanceTurn(currentState);
        });
    }

    public void QueryPromote(Pawn pawn, Action action)
    {
        // We don't want to display the query promote screen if we're not the team making the promote
        // That information will arrive to us across the network
        Multiplayer multiplayer = GameObject.FindObjectOfType<Multiplayer>();
        if(multiplayer != null && multiplayer.localTeam != GetCurrentTurn())
            return;
        promotionDialogue.Display(pieceType => {
            Promote(pawn, pieceType);
            int promoTurnCount = Mathf.FloorToInt((float)turnHistory.Count / 2f) + 1;
            action?.Invoke();

            Multiplayer multiplayer = GameObject.FindObjectOfType<Multiplayer>();
            multiplayer?.SendPromote(new Promotion(pawn.team, pawn.piece, pieceType, promoTurnCount));
        });
    }

    public IPiece Promote(Pawn pawn, Piece type, bool surpressNewPromotion = false)
    {
        // Replace the pawn with the chosen piece type
        // Worth noting: Even though the new IPiece is of a different type than Pawn,
        // we still use the PieceType.Pawn# (read from the pawn) to store it's position in the game state to maintain it's unique key
        Hex hex = GetHexIfInBounds(pawn.location);

        IPiece newPiece = Instantiate(piecePrefabs[(pawn.team, type)], hex.transform.position + Vector3.up, Quaternion.identity).GetComponent<IPiece>();
        newPiece.Init(pawn.team, pawn.piece, pawn.location);
        if(!surpressNewPromotion)
        {
            Promotion newPromo = new Promotion(pawn.team, pawn.piece, type, Mathf.FloorToInt((float)turnHistory.Count / 2f) + 1);
            promotions.Add(newPromo);
        }
        activePieces[(pawn.team, pawn.piece)] = newPiece;
        Destroy(pawn.gameObject);
        return newPiece;
    }

    private IPiece GetPromotedPieceIfNeeded(IPiece piece, bool surpressNewPromotion = false, int? turn = null)
    {
        if(piece is Pawn pawn)
        {
            Piece p = pawn.piece;
            foreach(Promotion promo in promotions)
            {
                if(promo.team == pawn.team && promo.from == p)
                {
                    if(turn.HasValue)
                    {
                        if(turn.Value >= promo.turnNumber)
                            p = promo.to;
                        else
                            continue;
                    }
                    else
                        p = promo.to;
                }
            }
            if(p != pawn.piece)
                piece = Promote(pawn, p, surpressNewPromotion);
        }

        return piece;
    }

    public BoardState Swap(IPiece p1, IPiece p2, BoardState boardState, bool isQuery = false)
    {
        if(p1 == p2)
            return boardState;

        Index p1StartLoc = p1.location;
        Index p2StartLoc = p2.location;
        BoardState currentState = boardState;
        
        if(!isQuery)
        {
            moveTracker.UpdateText(new Move(
                Mathf.FloorToInt((float)turnHistory.Count / 2f) + 1,
                p1.team,
                p1.piece,
                p1StartLoc,
                p2StartLoc,
                null,
                p2.piece
            ));
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

    public BoardState EnPassant(Pawn pawn, Team enemyTeam, Piece enemyPiece, Index targetHex, BoardState boardState, bool isQuery = false)
    {
        if(!isQuery)
        {
            IPiece enemyIPiece = activePieces[(enemyTeam, enemyPiece)];
            activePieces.Remove((enemyTeam, enemyPiece));
            // Capture enemy
            jails[(int)enemyTeam].Enprison(enemyIPiece);
            // Move pawn
            moveTracker.UpdateText(new Move(
                Mathf.FloorToInt((float)turnHistory.Count / 2f) + 1,
                pawn.team,
                pawn.piece,
                pawn.location,
                targetHex,
                enemyPiece,
                null
            ));
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

    public void Enprison(IPiece toPrison, bool updateState = true)
    {
        jails[(int)toPrison.team].Enprison(toPrison);
        activePieces.Remove((toPrison.team, toPrison.piece));

        if(updateState)
        {
            BoardState currentState = GetCurrentBoardState();
            BidirectionalDictionary<(Team, Piece), Index> allPiecePositions = currentState.allPiecePositions.Clone();
            allPiecePositions.Remove((toPrison.team, toPrison.piece));
            currentState.allPiecePositions = allPiecePositions;
            AdvanceTurn(currentState);
        }
    }

    public void EndGame(float timestamp, GameEndType endType = GameEndType.Pending, Winner winner = Winner.Pending)
    {
        BoardState currentState = GetCurrentBoardState();
        if(currentState.currentMove == Team.None)
            return;

        Multiplayer multiplayer = GameObject.FindObjectOfType<Multiplayer>();
        if(multiplayer)
        {
            Team winningTeam = Team.None;
            if(winner == Winner.White)
                winningTeam = Team.White;
            else if(winner == Winner.Black)
                winningTeam = Team.Black;

            if(multiplayer.gameParams.localTeam == winningTeam)
                audioSource.PlayOneShot(winFanfare);
        }
        else
            audioSource.PlayOneShot(winFanfare);

        currentState.currentMove = Team.None;
        currentState.executedAtTime = timestamp;
        turnHistory.Add(currentState);
        newTurn.Invoke(currentState);

        game = new Game(
            turnHistory,
            promotions,
            winner,
            endType,
            timers.timerDruation,
            timers.isClock
        );

        gameOver.Invoke(game);
    }

    public void HighlightMove(Move move)
    {
        ClearMoveHighlight();

        Hex fromHex = GetHexIfInBounds(move.from);
        Hex toHex = GetHexIfInBounds(move.to);

        fromHex.Highlight(lastMoveHighlightColor);
        toHex.Highlight(move.capturedPiece.HasValue
            ? Color.red
            : move.defendedPiece.HasValue
                ? Color.green
                : lastMoveHighlightColor
        );

        highlightedHexes.Add(fromHex);
        highlightedHexes.Add(toHex);
    }

    private void ClearMoveHighlight()
    {
        foreach(Hex hex in highlightedHexes)
            hex.Unhighlight();
        highlightedHexes.Clear();
    }

    public void Reset()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        SceneTransition sceneTransition = GameObject.FindObjectOfType<SceneTransition>();
        if(sceneTransition != null)
            sceneTransition.Transition(sceneName);
        else
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private void MaybeNewHex()
    {
        #if UNITY_EDITOR
        Hex[] selectedHexes = Selection.GetFiltered<Hex>(SelectionMode.Unfiltered);
        Debug.Log(selectedHexes.Length);
        #endif
    }

    [Button("Spawn Hexes")]
    private void SpawnHexes()
    {
        if(hexes.Count > 0)
            ClearHexes();
        
        for(int row = 0; row < HexGrid.rows; row++)
        {
            hexes.Add(new List<Hex>());
            for(int col = 0; col < HexGrid.cols; col++)
            {
                if(HexGrid.cols % 2 != 0 && col == HexGrid.cols - 1 && row % 2 == 0)
                    continue;

                GameObject newGo = Instantiate(
                    original: hexPrefab,
                    position: new Vector3(
                        x: hexGrid.radius * 3 * col + Get_X_Offset(row),
                        y: UnityEngine.Random.Range(hexGrid.minHeight, hexGrid.maxHeight),
                        z: row * hexGrid.Apothem
                    ),
                    rotation: Quaternion.identity,
                    parent: transform
                );

                Hex newHex = newGo.GetComponent<Hex>();

                newHex.transform.localScale = new Vector3(
                    x: newHex.transform.localScale.x * hexGrid.radius,
                    y: newHex.transform.localScale.y * hexGrid.height,
                    z: newHex.transform.localScale.z * hexGrid.radius
                );

                newHex.AssignIndex(new Index(row, col), this);

                hexes[row].Add(newHex);
                newHex.SetColor(GetColor(row));
            }
        }
    }

    public Color GetColor(int row) => row % 2 == 0  
        ? hexGrid.colors[(Mathf.FloorToInt(row/2) + 1) % 3]
        : hexGrid.colors[Mathf.FloorToInt(row/2) % 3];

    private float Get_X_Offset(int row) => row % 2 == 0 ? hexGrid.radius * 1.5f : 0f;

    [Button("Clear Hexes")]
    private void ClearHexes()
    {
        for(int row = 0; row < hexes.Count; row++)
        {
            for(int col = 0; col < hexes[row].Count; col++)
            {
#if UNITY_EDITOR
                DestroyImmediate(hexes[row][col].gameObject);
#elif !UNITY_EDITOR
                Destroy(hexes[row][col].gameObject);
#endif                
            }
        }
        hexes = new List<List<Hex>>();
    }

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
        for(int i = 0; i < hexes.Count; i++)
        {
            for(int j = 0; j < hexes[i].Count; j++)
            {
                if(j == col)
                {
                    Hex hex = GetHexIfInBounds(i, j);
                    if(hex != null)
                        hexesInCol.Add(hex);
                }
            }
        }
        return hexesInCol;
    }
}