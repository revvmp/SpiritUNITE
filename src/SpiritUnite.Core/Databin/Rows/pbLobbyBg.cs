namespace SpiritUnite.Databin;

public sealed record pbLobbyBg(
    int LobbyBgID,
    long OnTime = 0,
    int IsDefault = 0,
    int TransmuteId = 0,
    int TransmuteNum = 0,
    int JumpID = 0);
