namespace SpiritUNITE.Databin;

public sealed record pbRankedMatchMapOpenTime(
    int Index,
    int MapID,
    long OpenTime,
    long CloseTime,
    int SeasonID = 0,
    int TrainMapID = 0);
