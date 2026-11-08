namespace SpiritUnite.Databin;

public sealed record pbBattlePassInfo(int SeasonID, int[] OutSideItemId, int[] DiscountPrice, int ExtraPassDropID = 0, int HighPassDropID = 0);
