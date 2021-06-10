using System.Collections.Generic;

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

[System.Serializable]
public struct SerializedBoard
{
    public List<SerializedPiece> pieces;
    public Team currentMove;
    public Team check;
    public Team checkmate;
    public float executedAtTime;
}

[System.Serializable]
public struct SerializedPiece
{
    public Team t;
    public Piece p;
    public Index i;
}

public enum GameEndType
{
    Pending = 0,
    Checkmate = 1,
    Surrender = 2,
    Draw = 3,
    Flagfall = 4,
    Stalemate = 5
}

public enum Winner
{
    Pending = 0,
    White = 1,
    Black = 2,
    Draw = 3,
    None = 4
}