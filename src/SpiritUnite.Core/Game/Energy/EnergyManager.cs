using System.Security.Cryptography;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Energy;

public sealed record EnergyDrawOutcome(PB_ENUM_RES_ERROR_CODE Result, RoleEnergyInfo Energy, int DrawCount, BagOutcome Rewards)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;

    public static EnergyDrawOutcome Refused(PB_ENUM_RES_ERROR_CODE result, RoleEnergyInfo energy) =>
        new(result, energy, 0, BagOutcome.Refused(result));
}

public sealed record EnergyChangeOutcome(PB_ENUM_RES_ERROR_CODE Result, RoleEnergyInfo Energy, IReadOnlyList<BagItem> ChangedItems)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
}

public sealed class EnergyManager(EnergyRepository repository, BagManager bag, GameData gameData, TimeProvider clock)
{
    private const int FreeDrawNum = 1;
    private const int DrawRewardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonDrawReward;

    public EnergyRules Rules => EnergyCenter.Rules(gameData);

    public async Task<RoleEnergyInfo> LoadAsync(ulong gameUlongUID)
    {
        var now = clock.GetUtcNow();
        var rules = Rules;
        return (await repository.FindAsync(gameUlongUID))?.FlushedAt(now, rules.WeeklyEnergyFreeCapacity)
            ?? EnergyCenter.Starting(now, rules) with { Accelerators = await repository.FindAcceleratorsAsync(gameUlongUID) };
    }

    public async Task<EnergyDrawOutcome> DrawAsync(LMNRoleInfo role, int drawNum, int drawType, int poolType)
    {
        var current = await LoadAsync(role.GameUlongUID);
        if (!Enum.IsDefined((PB_ENUM_ENM_ENERGY_TYPE)drawType))
        {
            return EnergyDrawOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrEnergyUnknowDrawType, current);
        }

        if (!EnergyCenter.IsKnownPoolType(poolType))
        {
            return EnergyDrawOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrEnergyDataNotConfig, current);
        }

        var energyType = (PB_ENUM_ENM_ENERGY_TYPE)drawType;
        if (energyType == PB_ENUM_ENM_ENERGY_TYPE.PbEnmEnergyTypeSubscription)
        {
            return EnergyDrawOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrSubscriptionNoFreeEnergyCnt, current);
        }

        var maxDrawNum = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalEnergyMaxDrawNum);
        var isValidDrawNum = energyType == PB_ENUM_ENM_ENERGY_TYPE.PbEnmEnergyTypeFree
            ? drawNum == FreeDrawNum
            : drawNum >= 1 && drawNum <= maxDrawNum;
        if (!isValidDrawNum)
        {
            return EnergyDrawOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, current);
        }

        var now = clock.GetUtcNow();
        var pool = (PB_ENUM_ENUM_ENERGY_POOL_TYPE)poolType;
        var prizes = EnergyCenter.FindPrizes(gameData, pool, EnergyCenter.FindCurrentRewardPoolId(gameData, now, pool));
        if (prizes.Length == 0)
        {
            return EnergyDrawOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrEnergyCurHadNotReward, current);
        }

        var drawn = Enumerable.Range(0, drawNum).Select(_ => Roll(prizes)).ToArray();
        if (bag.PlanResourceGrants(role, drawn.Select(prize => KeyValuePair.Create(prize.ResourceId, (long)prize.ResourceNum))) is not { } plan)
        {
            return EnergyDrawOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrEnergyDataNotConfig, current);
        }

        var rules = Rules;
        var dailyMaxDrawNum = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalEnergyMaxDailyDrawNum);
        var nowSeconds = now.ToUnixTimeSeconds();
        var commit = await repository.DrawAsync(
            role.GameUlongUID,
            EnergyCenter.Starting(now, rules),
            stored => Charge(stored.FlushedAt(now, rules.WeeklyEnergyFreeCapacity), energyType, drawNum, dailyMaxDrawNum, rules.DailyFreeDrawCount),
            BagManager.ToBagChange(plan, nowSeconds, DrawRewardReason),
            nowSeconds);
        if (!commit.Succeeded || commit.Bag is not { } bagCommit)
        {
            return EnergyDrawOutcome.Refused(commit.Result, commit.Energy);
        }

        BagManager.GrantToRole(role, plan, nowSeconds, DrawRewardReason);
        return new EnergyDrawOutcome(commit.Result, commit.Energy, drawNum, BagOutcome.From(bagCommit) with
        {
            Awards = plan.Awards,
            GrantedPokemonIds = plan.PokemonIds,
            GrantedItems = plan.Items,
            AwardReason = DrawRewardReason,
        });
    }

    public async Task<EnergyChangeOutcome> InstallAcceleratorAsync(ulong gameUlongUID, int energyAccelerateRatio)
    {
        var current = await LoadAsync(gameUlongUID);
        if (!EnergyCenter.IsAccelerateRatio(energyAccelerateRatio))
        {
            return new EnergyChangeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, current, []);
        }

        var now = clock.GetUtcNow();
        var rules = Rules;
        var commit = await repository.ChangeAsync(
            gameUlongUID,
            EnergyCenter.Starting(now, rules),
            stored =>
            {
                var flushed = stored.FlushedAt(now, rules.WeeklyEnergyFreeCapacity);
                var installed = flushed.Install(energyAccelerateRatio);
                return installed.InstalledAcceleratorCapacity > 0
                    ? (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, installed)
                    : (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, flushed);
            },
            new BagChange(),
            now.ToUnixTimeSeconds());
        return new EnergyChangeOutcome(commit.Result, commit.Energy, []);
    }

    public async Task<EnergyChangeOutcome> UsePayCapacityItemAsync(ulong gameUlongUID, int resID, ulong gridUniqueID, int resCnt)
    {
        var current = await LoadAsync(gameUlongUID);
        var (validation, config) = await bag.ValidateUseAsync(gameUlongUID, resID, gridUniqueID, resCnt);
        if (config is null)
        {
            return new EnergyChangeOutcome(validation, current, []);
        }

        if (EnergyCenter.FindPayCapacity(config) is not { } payCapacityPerItem)
        {
            return new EnergyChangeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType, current, []);
        }

        var payCapacity = (long)payCapacityPerItem * resCnt;
        if (payCapacity > int.MaxValue - current.EnergyPayCapacity)
        {
            return new EnergyChangeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum, current, []);
        }

        var now = clock.GetUtcNow();
        var rules = Rules;
        var commit = await repository.ChangeAsync(
            gameUlongUID,
            EnergyCenter.Starting(now, rules),
            stored => (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, stored.FlushedAt(now, rules.WeeklyEnergyFreeCapacity).AddPayCapacity((int)payCapacity)),
            new BagChange { Costs = [new BagCost(RoleItemType.OutSideItem, resID, resCnt)] },
            now.ToUnixTimeSeconds());
        return new EnergyChangeOutcome(commit.Result, commit.Energy, commit.Bag?.ChangedItems ?? []);
    }

    public bool IsPayCapacityItem(int resID) =>
        gameData.OutSideItemBase.Find(resID) is { } config && EnergyCenter.FindPayCapacity(config) is not null;

    public async Task<GamePacket[]> BuildAcceleratorGainPushesAsync(ulong gameUlongUID, IEnumerable<BagGrant> grants)
    {
        var installed = grants.Where(grant => grant.InstallsIntoEnergyCenter && grant.Cnt > 0).ToArray();
        if (installed.Length == 0)
        {
            return [];
        }

        var energy = await LoadAsync(gameUlongUID);
        var packets = installed.Select(grant => GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidEnergyAccelerateNtf, new pbSCEnergyAccelerateNtf
        {
            EnergyAccelerateRatio = grant.Accelerator!.EnergyAccelerateRatio,
            EnergyAccelerateAdd = grant.Accelerator.EnergyAccelerateCapacity * grant.Cnt,
            EnergyAccelerateCapacity = energy.Accelerators
                .FirstOrDefault(accelerator => accelerator.EnergyAccelerateRatio == grant.Accelerator.EnergyAccelerateRatio)?.EnergyAccelerateCapacity ?? 0,
            OutSideItemId = grant.ItemId,
            UseCnt = grant.Cnt,
        }));
        return [.. packets, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidEnergyInfoNtf, EnergyCenter.BuildEnergyInfoNtf(energy))];
    }

    private static (PB_ENUM_RES_ERROR_CODE Result, RoleEnergyInfo After) Charge(
        RoleEnergyInfo energy,
        PB_ENUM_ENM_ENERGY_TYPE energyType,
        int drawNum,
        int dailyMaxDrawNum,
        int dailyFreeDrawCount)
    {
        if (energy.TodayDrawNum + drawNum > dailyMaxDrawNum)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrMaximumnumberoflottery, energy);
        }

        if (energyType == PB_ENUM_ENM_ENERGY_TYPE.PbEnmEnergyTypeFree)
        {
            return energy.UnusedDailyFreeDraws(dailyFreeDrawCount) < drawNum
                ? (PB_ENUM_RES_ERROR_CODE.PbErrEnergyTicketNotEnough, energy)
                : (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, energy with
                {
                    TodayDrawNum = energy.TodayDrawNum + drawNum,
                    DailyFreeDrawUseNum = energy.DailyFreeDrawUseNum + drawNum,
                });
        }

        return energy.RaffleTicket < drawNum
            ? (PB_ENUM_RES_ERROR_CODE.PbErrEnergyTicketNotEnough, energy)
            : (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, energy with
            {
                RaffleTicket = energy.RaffleTicket - drawNum,
                TodayDrawNum = energy.TodayDrawNum + drawNum,
            });
    }

    private static EnergyPrize Roll(EnergyPrize[] prizes)
    {
        var pick = RandomNumberGenerator.GetInt32(prizes.Sum(prize => prize.RewardProbability));
        foreach (var prize in prizes)
        {
            if (pick < prize.RewardProbability)
            {
                return prize;
            }

            pick -= prize.RewardProbability;
        }

        return prizes[^1];
    }
}
