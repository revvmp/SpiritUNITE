namespace SpiritUnite.Databin;

public sealed record pbScene_OpenTime(
    int Index,
    int SceneId,
    long OpenDate,
    long CloseDate,
    long DisplayDate = 0,
    long HideDate = 0,
    long OpenTime = 0,
    long CloseTime = 0,
    int OpenMode = 0,
    string OpenModeParaA = "",
    int OpenModeParaB = 0,
    int OpenModeParaC = 0,
    int IsShowDate = 0)
{
    public static long Key(int index, int sceneId) => ((long)index << 32) | (uint)sceneId;
}
