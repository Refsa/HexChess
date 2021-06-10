using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public struct Game 
{
    public Winner winner;
    public List<BoardState> turnHistory;
    public List<Promotion> promotions;
    public GameEndType endType;
    public float timerDuration;
    public bool hasClock;

    public float GetGameLength() => turnHistory[turnHistory.Count - 1].executedAtTime;

    public int GetTurnCount() => Mathf.FloorToInt((float)turnHistory.Count / 2f);

    public Game(
        List<BoardState> history, 
        List<Promotion> promotions = null, 
        Winner winner = Winner.Pending, 
        GameEndType endType = GameEndType.Pending, 
        float timerDuration = 0, 
        bool hasClock = false
    )
    {
        turnHistory = history;
        this.winner = winner;
        this.promotions = promotions == null ? new List<Promotion>() : promotions;
        this.endType = endType;
        this.timerDuration = timerDuration;
        this.hasClock = hasClock;
    }

    public string Serialize()
    {
        List<(Team, List<SerializedPiece>, Team, Team, float)> serializeableBoards = new List<(Team, List<SerializedPiece>, Team, Team, float)>();
        foreach(BoardState bs in turnHistory)
        {
            List<SerializedPiece> serializeableBoardState = bs.GetSerializeable();
            serializeableBoards.Add((bs.currentMove, serializeableBoardState, bs.check, bs.checkmate, bs.executedAtTime));
        }

        return JsonConvert.SerializeObject(new SerializeableGame(serializeableBoards, promotions, winner, endType, timerDuration, hasClock));
    }

    public static Game Deserialize(string json)
    {
        List<BoardState> history = new List<BoardState>();
        
        SerializeableGame game = JsonConvert.DeserializeObject<SerializeableGame>(json);
        foreach((Team team, List<SerializedPiece> pieces, Team check, Team checkmate, float duration) in game.serializedBoards)
            history.Add(BoardState.GetBoardStateFromDeserializedBoard(pieces, team, check, checkmate, duration));
        return new Game(history, game.promotions, game.winner, game.endType, game.timerDuration, game.hasClock);
    }
}

[System.Serializable]
public struct SerializeableGame
{
    public List<(Team, List<SerializedPiece>, Team, Team, float)> serializedBoards;
    public List<Promotion> promotions;
    public Winner winner;
    public GameEndType endType;
    public float timerDuration;
    public bool hasClock;

    public SerializeableGame(
        List<(Team, List<SerializedPiece>, Team, Team, float)> serializedBoards, 
        List<Promotion> promotions, 
        Winner winner = Winner.Pending, 
        GameEndType endType = GameEndType.Pending,
        float timerDuration = 0,
        bool hasClock = false
    )
    {
        this.serializedBoards = serializedBoards;
        this.promotions = promotions;
        this.winner = winner;
        this.endType = endType;
        this.timerDuration = timerDuration;
        this.hasClock = hasClock;
    }
}