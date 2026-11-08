using SpiritUnite.Database.Repositories;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Recharge;

public sealed class RechargeManager(CouponsFlowRepository couponsFlows)
{
    public async Task<pbSCCouponsFlowRsp> BuildCouponsFlowRspAsync(ulong gameUlongUID) => new()
    {
        ResultInfo = new pbResultInfo(),
        CouponsFlowInfo = { await couponsFlows.LoadLatestAsync(gameUlongUID, CouponsFlow.MaxGemUseLogNum) },
    };
}
