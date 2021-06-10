#nullable enable

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Newtonsoft.Json;

public class BoardStateTests
{
    private static BoardState CreateBoardState(IEnumerable<(Index location, Team team, Piece piece)>? pieces)
    {
        var allPiecePositions = new BidirectionalDictionary<(Team, Piece), Index>();
        if (pieces != null)
        {
            foreach (var piece in pieces)
            {
                allPiecePositions.Add((piece.team, piece.piece), piece.location);
            }
        }

        return new BoardState(allPiecePositions, Team.White, Team.None, Team.None, 0);
    }

    private static BoardState Deserialize(string json)
    {
        List<BoardState> history = new List<BoardState>();
        
        SerializeableGame game = JsonConvert.DeserializeObject<SerializeableGame>(json);
        foreach((Team team, List<SerializedPiece> pieces, Team check, Team checkmate, float duration) in game.serializedBoards)
            history.Add(BoardState.GetBoardStateFromDeserializedBoard(pieces, team, check, checkmate, duration));
        return history.First();
    }

    #region IsChecking tests
    [Test]
    public void NoPiecesTest()
    {
        var bs = CreateBoardState(Array.Empty<(Index, Team, Piece)>());
        Assert.False(bs.IsChecking(Team.White, null));
        Assert.False(bs.IsChecking(Team.Black, null));
    }
    [Test]
    public void KingOnlyNoCheckTest()
    {
        var bs = CreateBoardState(new[] {
            (new Index(1, 'E'), Team.White, Piece.King),
            (new Index(9, 'E'), Team.Black, Piece.King),
        });

        Assert.False(bs.IsChecking(Team.White, null));
        Assert.False(bs.IsChecking(Team.Black, null));
    }

    [Test]
    public void KingCheckEachOtherTest()
    {
        var bs = CreateBoardState(new[] {
            (new Index(5, 'E'), Team.White, Piece.King),
            (new Index(6, 'E'), Team.Black, Piece.King),
        });

        Assert.True(bs.IsChecking(Team.White, null));
        Assert.True(bs.IsChecking(Team.Black, null));
    }

    [Test]
    public void DefaultBoardCheckTest()
    {
        var bs = Deserialize(((TextAsset)Resources.Load("DefaultBoardState", typeof(TextAsset))).text);

        Assert.False(bs.IsChecking(Team.White, null));
        Assert.False(bs.IsChecking(Team.Black, null));
    }

    [Test]
    public void PawnCheckTest()
    {
        var bs = CreateBoardState(new[] {
            (new Index(1, 'A'), Team.White, Piece.King),
            (new Index(5, 'E'), Team.Black, Piece.King),
            (new Index(5, 'F'), Team.White, Piece.Pawn1),
        });

        Assert.True(bs.IsChecking(Team.White, null));
        Assert.False(bs.IsChecking(Team.Black, null));
    }

    [Test]
    public void PawnPromotionCheckTest()
    {
        var bs = CreateBoardState(new[] {
            (new Index(1, 'A'), Team.White, Piece.King),
            (new Index(5, 'E'), Team.Black, Piece.King),
            (new Index(1, 'E'), Team.White, Piece.Pawn1),
        });

        Assert.False(bs.IsChecking(Team.Black, null));
        Assert.False(bs.IsChecking(Team.White, null));
        Assert.True(bs.IsChecking(Team.White, new List<Promotion>() { new Promotion(Team.White, Piece.Pawn1, Piece.Queen, 0) }));
        Assert.False(bs.IsChecking(Team.Black, new List<Promotion>() { new Promotion(Team.White, Piece.Pawn1, Piece.Queen, 0) }));
    }
    #endregion

    #region ApplyMove tests
    [Test]
    public void SimpleMoveTest()
    {
        var bs = CreateBoardState(new[] {
            (new Index(1, 'A'), Team.White, Piece.King),
            (new Index(5, 'E'), Team.Black, Piece.King),
            (new Index(1, 'E'), Team.White, Piece.Pawn1),
        });

        (Team, Piece) piece = (Team.White, Piece.Pawn1);
        (Index target, MoveType) move = (new Index(2, 'E'), MoveType.Move);
        (BoardState newState, List<Promotion> promotions) = bs.ApplyMove(piece, new Index(1, 'E'), move, null);

        Assert.Null(promotions);
        Assert.True(newState.IsOccupiedBy(move.target, piece));
        Assert.False(newState.IsOccupied(new Index(1, 'E')));
    }

    [Test]
    public void SimpleAttackTest()
    {
        Index victimLocation = new Index(5, 'E');
        Index attackerLocation = new Index(1, 'E');

        var bs = CreateBoardState(new[] {
            (new Index(1, 'A'), Team.White, Piece.King),
            (new Index(9, 'I'), Team.Black, Piece.King),
            (attackerLocation, Team.White, Piece.KingsRook),
            (victimLocation, Team.Black, Piece.Pawn1),
        });

        (Index target, MoveType) move = (victimLocation, MoveType.Attack);
        var attacker = bs.allPiecePositions[attackerLocation];
        (BoardState newState, List<Promotion> promotions) = bs.ApplyMove(attacker, attackerLocation, move, null);

        Assert.Null(promotions);
        Assert.True(newState.IsOccupiedBy(move.target, attacker));
        Assert.False(newState.IsOccupied(attackerLocation));
    }

    [Test]
    public void SimpleDefendTest()
    {
        Index victimLocation = new Index(2, 'E');
        Index defenderLocation = new Index(1, 'E');

        var bs = CreateBoardState(new[] {
            (new Index(9, 'I'), Team.Black, Piece.King),
            (victimLocation, Team.White, Piece.King),
            (defenderLocation, Team.White, Piece.KingsRook),
        });

        (Index target, MoveType) move = (victimLocation, MoveType.Defend);
        var defender = bs.allPiecePositions[defenderLocation];
        var victim = bs.allPiecePositions[victimLocation];
        (BoardState newState, List<Promotion> promotions) = bs.ApplyMove(defender, defenderLocation, move, null);

        Assert.Null(promotions);
        Assert.True(newState.IsOccupiedBy(victimLocation, defender));
        Assert.True(newState.IsOccupiedBy(defenderLocation, victim));
    }

    [Test]
    public void SimpleEnPassantTest()
    {
        Index attackerLocation = new Index(6, 'B');
        Index victimLocation = new Index(6, 'A');

        var bs = CreateBoardState(new[] {
            (new Index(9, 'I'), Team.Black, Piece.King),
            (new Index(1, 'A'), Team.White, Piece.King),
            (victimLocation, Team.Black, Piece.Pawn1),
            (attackerLocation, Team.White, Piece.Pawn1),
        });

        (Index target, MoveType) move = (victimLocation.GetNeighborAt(HexNeighborDirection.Up)!.Value, MoveType.EnPassant);
        var victim = bs.allPiecePositions[victimLocation];
        var attacker = bs.allPiecePositions[attackerLocation];
        (BoardState newState, List<Promotion> promotions) = bs.ApplyMove(attacker, attackerLocation , move, null);

        Assert.Null(promotions);
        Assert.False(newState.IsOccupied(attackerLocation));
        Assert.False(newState.IsOccupied(victimLocation));
        Assert.True(newState.IsOccupiedBy(move.target, attacker));
    }
    #endregion


    #region HasAnyValidMoves tests

    [Test]
    public void AnyValid_EmptyBoardTest()
    {
        var board1 = CreateBoardState(null);
        Assert.False(board1.HasAnyValidMoves(Team.White, null));
        Assert.False(board1.HasAnyValidMoves(Team.Black, null));

        var board2 = CreateBoardState(new[] {
            (new Index(5, 'E'), Team.White, Piece.King),
        });

        Assert.True(board2.HasAnyValidMoves(Team.White, null));
        Assert.False(board2.HasAnyValidMoves(Team.Black, null));

        var board3 = CreateBoardState(new[] {
            (new Index(5, 'E'), Team.Black, Piece.King),
        });

        Assert.False(board3.HasAnyValidMoves(Team.White, null));
        Assert.True(board3.HasAnyValidMoves(Team.Black, null));
    }

    [Test]
    public void AnyValid_BlackStalemateTest()
    {
        var bs = CreateBoardState(new[] {
            (new Index(9, 'A'), Team.White, Piece.King),
            (new Index(3, 'B'), Team.White, Piece.Queen),
            (new Index(1, 'A'), Team.Black, Piece.King),
        });
        Assert.True(bs.HasAnyValidMoves(Team.White, null));
        Assert.False(bs.HasAnyValidMoves(Team.Black, null));
    }

    #endregion
}
