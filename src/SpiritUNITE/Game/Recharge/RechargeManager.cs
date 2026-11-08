using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Recharge;

public sealed class RechargeManager(CouponsFlowRepository couponsFlows)
{
    public async Task<pbSCCouponsFlowRsp> BuildCouponsFlowRspAsync(ulong gameUlongUID) => new()
    {
        ResultInfo = new pbResultInfo(),
        CouponsFlowInfo = { await couponsFlows.LoadLatestAsync(gameUlongUID, CouponsFlow.MaxGemUseLogNum) },
    };
}
