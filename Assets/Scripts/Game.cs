using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public struct Game 
{
    public Winner winner;
    public List<BoardState> turnHistory;
    public List<Promotion> promotions;
    public int GetTurnCount() => Mathf.FloorToInt((float)turnHistory.Count / 2f);

    public Game(List<BoardState> history, List<Promotion> promotions = null, Winner winner = Winner.Pending)
    {
        turnHistory = history;
        this.winner = winner;
        this.promotions = promotions == null ? new List<Promotion>() : promotions;
    }

    public static string Serialize(List<BoardState> turnHistory, List<Promotion> promotions, Winner winner = Winner.Pending)
    {
        List<(Team, List<SerializedPiece>, Team, Team, float)> serializeableBoards = new List<(Team, List<SerializedPiece>, Team, Team, float)>();
        foreach(BoardState bs in turnHistory)
        {
            List<SerializedPiece> serializeableBoardState = bs.GetSerializeable();
            serializeableBoards.Add((bs.currentMove, serializeableBoardState, bs.check, bs.checkmate, bs.executedAtTime));
        }

        return JsonConvert.SerializeObject(new SerializeableGame(serializeableBoards, promotions, winner));
    }

    public static Game Deserialize(string json)
    {
        List<BoardState> history = new List<BoardState>();
        
        SerializeableGame game = JsonConvert.DeserializeObject<SerializeableGame>(json);
        foreach((Team team, List<SerializedPiece> pieces, Team check, Team checkmate, float duration) in game.serializedBoards)
            history.Add(BoardState.GetBoardStateFromDeserializedBoard(pieces, team, check, checkmate, duration));
        return new Game(history,game.promotions, game.winner);
    }
}

[System.Serializable]
public struct SerializeableGame
{
    public List<(Team, List<SerializedPiece>, Team, Team, float)> serializedBoards;
    public List<Promotion> promotions;
    public Winner winner;

    public SerializeableGame(List<(Team, List<SerializedPiece>, Team, Team, float)> serializedBoards, List<Promotion> promotions, Winner winner = Winner.Pending)
    {
        this.serializedBoards = serializedBoards;
        this.promotions = promotions;
        this.winner = winner;
    }
}

public enum Winner {
    Pending = 0,
    White = 1,
    Black = 2,
    Draw = 3
}