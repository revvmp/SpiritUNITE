namespace SpiritUNITE.Databin;

public sealed record pbMvp_Conf(int GameType, int ParamType, int[] ParamVec)
{
    public static long Key(int gameType, int paramType) => ((long)gameType << 32) | (uint)paramType;
}
