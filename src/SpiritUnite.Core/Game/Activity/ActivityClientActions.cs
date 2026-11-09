using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public static class ActivityClientActions
{
    public const int ActionCount = 1;

    private const int NoParam = 0;

    public static bool IsClientAction(pbFinishCondition condition) =>
        condition.ID is (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdClickTaskButton
            or (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdShareContent
            or (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdObserveFriend;

    public static bool ObservedFriendBattle(pbFinishCondition condition) =>
        condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdObserveFriend;

    public static bool IsValidButtonId(int buttonId) =>
        buttonId != (int)PB_ENUM_RES_TASK_BUTTON_ID.PbResTaskButtonIdNone
        && Enum.IsDefined((PB_ENUM_RES_TASK_BUTTON_ID)buttonId);

    public static Func<pbFinishCondition, bool> ClickedTaskButton(int buttonId) => condition =>
        condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdClickTaskButton
        && condition.ParamD.FirstOrDefault() == buttonId;

    public static bool SharedAnyContent(pbFinishCondition condition) =>
        condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdShareContent
        && condition.ParamA.All(param => param == NoParam)
        && condition.ParamB.All(param => param == NoParam)
        && condition.ParamD.All(param => param == NoParam);
}
