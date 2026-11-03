using SpiritUNITE.Game.Data;

namespace SpiritUNITE.Game.Role;

public static class RoleImage
{
    public static bool IsCreationChoice(GameData gameData, int gender, int modelID, int skinColorID) =>
        gameData.RoleImage.Rows.Any(image =>
            image.Gender == gender && image.ModelID == modelID && image.SkinColorID == skinColorID);
}
