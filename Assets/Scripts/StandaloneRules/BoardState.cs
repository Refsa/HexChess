using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Serialization;
using Extensions;

public struct BoardState : System.IDisposable
{
    public Team currentMove;
    [FormerlySerializedAs("biDirPiecePositions")]
    public BidirectionalDictionary<(Team, Piece), Index> allPiecePositions;

    public Team check;
    public Team checkmate;
    public float executedAtTime;

    public Team CurrentMove => currentMove;

    public BoardState(BidirectionalDictionary<(Team, Piece), Index> allPiecePositions, Team currentMove, Team check, Team checkmate, float executedAtTime)
    {
        this.allPiecePositions = allPiecePositions;
        this.currentMove = currentMove;
        this.check = check;
        this.checkmate = checkmate;
        this.executedAtTime = executedAtTime;
    }

    public static Move GetLastMove(List<BoardState> history)
    {
        if (history.Count > 1)
        {
            BoardState lastState = history[history.Count - 2];
            BoardState nowState = history[history.Count - 1];
            foreach (KeyValuePair<(Team team, Piece piece), Index> kvp in lastState.allPiecePositions)
            {
                if (!nowState.TryGetIndex(kvp.Key, out Index nowPos))
                    continue;

                if (kvp.Value == nowPos)
                    continue;

                (Team previousTeamAtLocation, Piece? previousPieceAtLocation) = lastState.allPiecePositions.Contains(nowPos)
                    ? lastState.allPiecePositions[nowPos]
                    : (Team.None, (Piece?)null);

                Piece? capturedPiece = previousTeamAtLocation == kvp.Key.team ? null : previousPieceAtLocation;
                if (kvp.Key.piece.IsPawn() && kvp.Value.GetLetter() != nowPos.GetLetter() && capturedPiece == null)
                {
                    // Pawns that move sideways are always attacks. If the new location was unoccupied, then did En Passant
                    Index? enemyLocation = nowPos.GetNeighborAt(kvp.Key.team == Team.White ? HexNeighborDirection.Down : HexNeighborDirection.Up);
                    if (enemyLocation != null && lastState.TryGetPiece(enemyLocation.Value, out var captured))
                        capturedPiece = captured.piece;
                }

                return new Move(
                    turn: Mathf.FloorToInt((float)history.Count / 2f),
                    lastTeam: kvp.Key.team,
                    lastPiece: kvp.Key.piece,
                    from: kvp.Value,
                    to: nowPos,
                    capturedPiece: capturedPiece,
                    defendedPiece: previousTeamAtLocation != kvp.Key.team ? null : previousPieceAtLocation,
                    duration: nowState.executedAtTime - lastState.executedAtTime
                );

            }
        }
        return new Move(0, Team.None, Piece.King, default(Index), default(Index));
    }

    public readonly bool TryGetPiece(Index index, out (Team team, Piece piece) teamedPiece) =>
        allPiecePositions.TryGetValue(index, out teamedPiece);
    public readonly bool TryGetIndex((Team team, Piece piece) teamedPiece, out Index index) =>
        allPiecePositions.TryGetValue(teamedPiece, out index);

    public static bool operator ==(BoardState a, BoardState b) =>
        a.currentMove == b.currentMove
        && a.allPiecePositions.Count == b.allPiecePositions.Count
        && !a.allPiecePositions.Except(b.allPiecePositions).Any();
    public static bool operator !=(BoardState a, BoardState b) => !(a == b);

    public override bool Equals(object obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();
    public override string ToString() => base.ToString();

    public readonly bool IsOccupied(Index index)
    {
        return allPiecePositions.ContainsKey(index);
    }
    public readonly bool IsOccupiedBy(Index index, (Team expectedTeam, Piece expectedPiece) expected)
    {
        if (!TryGetPiece(index, out (Team actualTeam, Piece actualPiece) actual))
            return false;

        return expected == actual;
    }

    /// <summary>
    /// Is a piece from <paramref name="checkForTeam"/> attacking the enemy king?
    /// </summary>
    /// <param name="checkForTeam"></param>
    /// <returns>true if the enemy king is threatened</returns>
    public bool IsChecking(Team checkForTeam, IEnumerable<Promotion> promotions)
    {
        Team otherTeam = checkForTeam == Team.White ? Team.Black : Team.White;
        Index otherKing = allPiecePositions.Where(kvp => kvp.Key == (otherTeam, Piece.King)).Select(kvp => kvp.Value).FirstOrDefault();

        foreach (KeyValuePair<(Team team, Piece piece), Index> kvp in allPiecePositions)
        {
            if (kvp.Key.team != checkForTeam) continue;

            Piece realPiece = kvp.Key.piece;
            if (promotions != null)
            {
                foreach (Promotion promo in promotions)
                {
                    if (promo.team == checkForTeam && promo.from == realPiece)
                    {
                        realPiece = promo.to;
                        break;
                    }
                }
            }

            IEnumerable<(Index, MoveType)> moves = MoveGenerator.GetAllPossibleMoves(kvp.Value, realPiece, checkForTeam, this);
            foreach ((Index hex, MoveType moveType) in moves)
            {
                if (moveType == MoveType.Attack && hex == otherKing)
                    return true;
            }
        }

        return false;
    }

    public bool HasAnyValidMoves(Team checkForTeam, List<Promotion> promotions)
    {
        foreach (KeyValuePair<(Team team, Piece piece), Index> kvp in allPiecePositions)
        {
            if (kvp.Key.team != checkForTeam)
                continue;

            Piece realPiece = kvp.Key.piece;
            if (promotions != null)
            {
                foreach (Promotion promo in promotions)
                {
                    if (promo.team == checkForTeam && promo.from == realPiece)
                    {
                        realPiece = promo.to;
                        break;
                    }
                }
            }

            Team enemyTeam = kvp.Key.team == Team.White ? Team.Black : Team.White;
            var pieceMoves = MoveGenerator.GetAllPossibleMoves(kvp.Value, realPiece, kvp.Key.team, this);
            foreach (var potentialMove in pieceMoves)
            {
                (BoardState newState, List<Promotion> newPromotions) = ApplyMove(kvp.Key, kvp.Value, potentialMove, promotions);
                if (!newState.IsChecking(enemyTeam, newPromotions))
                    return true;
            }
        }

        return false;
    }

    public (BoardState newState, List<Promotion> promotions) ApplyMove((Team team, Piece piece) piece, Index startLocation, (Index target, MoveType moveType) move, List<Promotion> promotions)
    {
        switch (move.moveType)
        {
            case MoveType.Move:
            case MoveType.Attack:
                return MoveOrAttack(piece, startLocation, move, promotions);
            case MoveType.Defend:
                return Defend(piece, startLocation, move, promotions);
            case MoveType.EnPassant:
                return EnPassant(piece, startLocation, move, promotions);
            default:
                throw new Exception($"Invalid move type: {move.moveType}");
        }
    }

    public readonly (BoardState newState, List<Promotion> promotions) MoveOrAttack((Team team, Piece piece) piece, Index startLocation, (Index target, MoveType moveType) move, List<Promotion> promotions)
    {
        var newPositions = allPiecePositions.Clone();
        newPositions.Remove(startLocation);
        newPositions.Remove(move.target);
        newPositions.Add(piece, move.target);

        // TODO: promotions

        var newState = new BoardState(newPositions, currentMove, check, checkmate, executedAtTime);
        return (newState, promotions);
    }
    public readonly (BoardState newState, List<Promotion> promotions) Defend((Team team, Piece piece) piece, Index startLocation, (Index target, MoveType moveType) move, List<Promotion> promotions)
    {
        var targetPiece = allPiecePositions[move.target];
        var newPositions = allPiecePositions.Clone();
        newPositions.Remove(startLocation);
        newPositions.Remove(move.target);
        newPositions.Add(piece, move.target);
        newPositions.Add(targetPiece, startLocation);

        var newState = new BoardState(newPositions, currentMove, check, checkmate, executedAtTime);
        return (newState, promotions);
    }
    public readonly (BoardState newState, List<Promotion> promotions) EnPassant((Team team, Piece piece) piece, Index startLocation, (Index target, MoveType moveType) move, List<Promotion> promotions)
    {
        var victimLocation = move.target.GetNeighborAt(piece.team == Team.White ? HexNeighborDirection.Down : HexNeighborDirection.Up).Value;

        var newPositions = allPiecePositions.Clone();
        newPositions.Remove(startLocation);
        newPositions.Remove(victimLocation);
        newPositions.Add(piece, move.target);

        var newState = new BoardState(newPositions, currentMove, check, checkmate, executedAtTime);
        return (newState, promotions);
    }

    // Json (including newtonsoft) can not properly serialize a dictionary that uses a key that is any type other than than a string.
    // To get around this, we convert our Bidirectional dictionary into a list, then serialze that list.
    public List<SerializedPiece> GetSerializeable()
    {
        List<SerializedPiece> list = new List<SerializedPiece>();
        foreach (KeyValuePair<(Team, Piece), Index> kvp in allPiecePositions)
        {
            (Team team, Piece piece) = kvp.Key;
            list.Add(new SerializedPiece { t = team, p = piece, i = kvp.Value });
        }
        return list;
    }

    public byte[] Serialize() =>
        Encoding.ASCII.GetBytes(
            JsonConvert.SerializeObject(new SerializedBoard
            {
                pieces = GetSerializeable(),
                currentMove = currentMove,
                check = check,
                checkmate = checkmate,
                executedAtTime = executedAtTime
            })
        );

    // When deserializing from json, because of the before mentioned dictionary issues, we must deserialize as a list, then construct our dictionary from it.
    public static BoardState GetBoardStateFromDeserializedBoard(List<SerializedPiece> list, Team currentMove, Team check, Team checkmate, float executedAtTime)
    {
        BidirectionalDictionary<(Team, Piece), Index> newDict = new BidirectionalDictionary<(Team, Piece), Index>();
        foreach (SerializedPiece tpl in list)
            newDict.Add((tpl.t, tpl.p), tpl.i);

        return new BoardState { allPiecePositions = newDict, currentMove = currentMove, check = check, checkmate = checkmate, executedAtTime = executedAtTime };
    }

    public BoardState Clone()
    {
        var newPositions = allPiecePositions.Clone();
        var clone = new BoardState(newPositions, currentMove, check, checkmate, executedAtTime);
        return clone;
    }

    public static BoardState Deserialize(byte[] data)
    {
        string json = Encoding.ASCII.GetString(data);
        SerializedBoard boardstate = JsonConvert.DeserializeObject<SerializedBoard>(json);

        BidirectionalDictionary<(Team, Piece), Index> newDict = BidirectionalDictionaryPool<(Team, Piece), Index>.Get();
        foreach (SerializedPiece tpl in boardstate.pieces)
            newDict.Add((tpl.t, tpl.p), tpl.i);

        return new BoardState
        {
            allPiecePositions = newDict,
            currentMove = boardstate.currentMove,
            check = boardstate.check,
            checkmate = boardstate.checkmate,
            executedAtTime = boardstate.executedAtTime
        };
    }

    public void Dispose()
    {
        BidirectionalDictionaryPool<(Team, Piece), Index>.Free(allPiecePositions);
    }
}