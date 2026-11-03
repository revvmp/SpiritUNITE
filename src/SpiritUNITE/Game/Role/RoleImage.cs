using System.Collections.Frozen;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public static class RoleImage
{
    private const int MaleBaseModelID = 20191219;
    private const int FamaleBaseModelID = 20200501;

    private const int Male = (int)PB_ENUM_RES_GENDER_TYPE.PbResGenderTypeMale;
    private const int Female = (int)PB_ENUM_RES_GENDER_TYPE.PbResGenderTypeFemale;

    private static readonly FrozenSet<RoleImageInfo> RoleImages =
    [
        new(Male, MaleBaseModelID, 7),
        new(Male, MaleBaseModelID, 8),
        new(Male, MaleBaseModelID, 9),
        new(Male, MaleBaseModelID, 10),
        new(Female, FamaleBaseModelID, 7),
        new(Female, FamaleBaseModelID, 8),
        new(Female, FamaleBaseModelID, 9),
        new(Female, FamaleBaseModelID, 10),
    ];

    public static bool IsCreationChoice(int gender, int modelID, int skinColorID) =>
        RoleImages.Contains(new RoleImageInfo(gender, modelID, skinColorID));

    private readonly record struct RoleImageInfo(int Gender, int ModelID, int SkinColorID);
}
