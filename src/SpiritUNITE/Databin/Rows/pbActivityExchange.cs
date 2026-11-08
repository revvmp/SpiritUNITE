namespace SpiritUNITE.Databin;

public sealed record pbActivityExchange(
    int ExchangeID,
    int ExchangeGroupID,
    pbFromRes[] FromRes,
    int ToResID,
    int ToResNum,
    pbToDst[] ToDst,
    int MaxDoneNum = 0,
    int CustomTime = 0,
    long ValidStartTime = 0,
    long ValidEndTime = 0,
    int NeedPvePrivilege = 0,
    int PvePrivilegeExtra = 0);

public sealed record pbFromRes(int ID = 0, int Num = 0);

public sealed record pbToDst(int ResID = 0, int ResNum = 0);
