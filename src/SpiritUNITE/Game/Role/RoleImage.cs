using SpiritUNITE.Databin;

namespace SpiritUNITE.Game.Role;

public static class RoleImage
{
    public const int NoHeadID = 0;

    private const int MaleGender = 1;

    public static bool IsCreationChoice(GameData gameData, int gender, int modelID, int skinColorID) =>
        gameData.RoleImage.Rows.Any(image =>
            image.Gender == gender && image.ModelID == modelID && image.SkinColorID == skinColorID);

    public static int GetHeadID(GameData gameData, LMNRoleInfo role)
    {
        var avatar = role.RoleAvatarInfo;
        var image = gameData.RoleImage.Rows.FirstOrDefault(image =>
            image.Gender == role.Gender
            && image.ModelID == avatar.RoleBaseModelID
            && image.SkinColorID == avatar.SkinColorID);
        return image is null
            ? NoHeadID
            : gameData.HeadIcon.Rows
                .Where(head => (role.Gender == MaleGender ? head.MaleIcon : head.FemaleIcon) == image.RoleImagePic)
                .Select(head => head.HeadIconID)
                .DefaultIfEmpty(NoHeadID)
                .Max();
    }
}
