using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Profile;

public sealed class ProfileStyle(
    int curHeadIconID,
    pbCOMDT_PLAYER_CONTESTANT_INFO playerContestantInfo,
    IEnumerable<int> labelList,
    int isAnonymity,
    int isHideInfo,
    int curCanopyID,
    int headType,
    int curHeadResID,
    IReadOnlyDictionary<int, int> wearCardFaceIDs)
{
    public const int DefaultHeadIconID = (int)PB_MACRO_COM_DEFAULT_HEAD_ICON_ID.PbComDefaultHeadIconId;
    public const int NoCanopyID = 0;
    public const int NoHeadResID = 0;
    public const int NoCardFaceID = 0;
    public const int DefaultHeadType = (int)PB_ENUM_ENUM_SOCIAL_HEAD_TYPE.PbEnumSocialHeadTypeSubscription;

    private readonly Lock gate = new();
    private readonly Dictionary<int, int> mWearCardFaceIDs = new(wearCardFaceIDs);
    private pbCOMDT_PLAYER_CONTESTANT_INFO mPlayerContestantInfo = playerContestantInfo;
    private int[] mLabelList = [.. labelList];
    private int mCurHeadIconID = curHeadIconID;
    private int mIsAnonymity = isAnonymity;
    private int mIsHideInfo = isHideInfo;
    private int mCurCanopyID = curCanopyID;
    private int mHeadType = headType;
    private int mCurHeadResID = curHeadResID;

    public static ProfileStyle CreateDefault() => new(
        DefaultHeadIconID,
        new pbCOMDT_PLAYER_CONTESTANT_INFO(),
        [],
        0,
        0,
        NoCanopyID,
        DefaultHeadType,
        NoHeadResID,
        new Dictionary<int, int>());

    public int CurHeadIconID
    {
        get
        {
            lock (gate)
            {
                return mCurHeadIconID;
            }
        }
        set
        {
            lock (gate)
            {
                mCurHeadIconID = value;
            }
        }
    }

    public int IsAnonymity
    {
        get
        {
            lock (gate)
            {
                return mIsAnonymity;
            }
        }
        set
        {
            lock (gate)
            {
                mIsAnonymity = value;
            }
        }
    }

    public int IsHideInfo
    {
        get
        {
            lock (gate)
            {
                return mIsHideInfo;
            }
        }
        set
        {
            lock (gate)
            {
                mIsHideInfo = value;
            }
        }
    }

    public int CurCanopyID
    {
        get
        {
            lock (gate)
            {
                return mCurCanopyID;
            }
        }
        set
        {
            lock (gate)
            {
                mCurCanopyID = value;
            }
        }
    }

    public (int HeadType, int CurHeadResID) CurPersonStyleHead
    {
        get
        {
            lock (gate)
            {
                return (mHeadType, mCurHeadResID);
            }
        }
        set
        {
            lock (gate)
            {
                (mHeadType, mCurHeadResID) = value;
            }
        }
    }

    public pbCOMDT_PLAYER_CONTESTANT_INFO PlayerContestantInfo
    {
        get
        {
            lock (gate)
            {
                return mPlayerContestantInfo.Clone();
            }
        }
        set
        {
            lock (gate)
            {
                mPlayerContestantInfo = value.Clone();
            }
        }
    }

    public int[] LabelList
    {
        get
        {
            lock (gate)
            {
                return [.. mLabelList];
            }
        }
        set
        {
            lock (gate)
            {
                mLabelList = [.. value];
            }
        }
    }

    public pbCOMDT_INDIVIDUAL_LABEL_INFO BuildLableInfo() => new() { LabelList = { LabelList } };

    public pbCOMDT_ROLE_HEAD_URL_INFO BuildUserPlantInfo()
    {
        var (headType, curHeadResID) = CurPersonStyleHead;
        return new pbCOMDT_ROLE_HEAD_URL_INFO
        {
            HeadType = headType,
            CurPersonStyleInfo = new pbCOMDT_PLAYER_PERSON_STYLE_CUR_INFO { CurHeadResID = curHeadResID },
            NPSetting = new pbCOMDT_NOBLE_PREVILEG_GAME_SETTING_INFO(),
        };
    }

    public int GetWearCardFaceID(int pokemonID)
    {
        lock (gate)
        {
            return mWearCardFaceIDs.GetValueOrDefault(pokemonID, NoCardFaceID);
        }
    }

    public void SetWearCardFaceID(int pokemonID, int cardFaceID)
    {
        lock (gate)
        {
            if (cardFaceID == NoCardFaceID)
            {
                mWearCardFaceIDs.Remove(pokemonID);
            }
            else
            {
                mWearCardFaceIDs[pokemonID] = cardFaceID;
            }
        }
    }
}
