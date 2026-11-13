using Microsoft.Extensions.Logging;
using SpiritUnite.Databin;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Badge;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.BattlePass;
using SpiritUnite.Game.Energy;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Energy;

public sealed class EnergyHandlers(
    RoleInfoStore roles,
    EnergyManager energies,
    BadgeManager badges,
    StarRoadManager starRoad,
    TaskUpdates taskUpdates,
    ActivityManager activityManager,
    ActivityProgressPushes activityProgressPushes,
    BattlePassTasks battlePassTasks,
    GameData gameData,
    TimeProvider clock,
    ILogger<EnergyHandlers> logger)
{
    private const int NewBadgePictureRedDotBadgeNum = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetEnergyInfoReq)]
    public async Task<GamePacket[]> OnGetEnergyInfoReq(GamePacket request)
    {
        pbCSGetEnergyInfoReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var energy = await energies.LoadAsync(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetEnergyInfoRsp, EnergyCenter.BuildGetEnergyInfoRsp(energy))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCurRewardPoolReq)]
    public GamePacket[] OnCurRewardPoolReq(GamePacket request)
    {
        var query = pbCSCurRewardPoolReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var response = new pbSCCurRewardPoolRsp { ResultInfo = new pbResultInfo(), PoolType = query.PoolType };
        if (EnergyCenter.IsKnownPoolType(query.PoolType))
        {
            response.RewardPoolID = EnergyCenter.FindCurrentRewardPoolId(gameData, clock.GetUtcNow(), (PB_ENUM_ENUM_ENERGY_POOL_TYPE)query.PoolType);
        }
        else
        {
            logger.LogInformation("Energy reward pool type {PoolType} from uid {Uid} is unknown", query.PoolType, request.Uid);
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrEnergyDataNotConfig;
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCurRewardPoolRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidEnergyDrawReq)]
    public async Task<GamePacket[]> OnEnergyDrawReq(GamePacket request)
    {
        var draw = pbCSEnergyDrawReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var pictureBefore = await badges.BuildGetBadgePictureRspAsync(role.GameUlongUID);
        var outcome = await energies.DrawAsync(role, draw.DrawNum, draw.DrawType, draw.PoolType);
        logger.LogInformation(
            "Energy draw x{DrawNum} type {DrawType} pool {PoolType} by uid {Uid}: {Result}, {RaffleTicket} tickets left, rewards {Rewards}",
            draw.DrawNum,
            draw.DrawType,
            draw.PoolType,
            role.GameUlongUID,
            outcome.Result,
            outcome.Energy.RaffleTicket,
            string.Join(", ", outcome.Rewards.Awards.Select(award => $"{award.FinallyAwardInfo.ResID}x{award.FinallyAwardInfo.ResCnt}")));
        var response = new pbSCEnergyDrawRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            LeftTick = outcome.Energy.RaffleTicket,
            DrawType = draw.DrawType,
            PoolType = draw.PoolType,
        };
        if (!outcome.Succeeded)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidEnergyDrawRsp, response)];
        }

        response.RewardDetail.Add(outcome.Rewards.Awards.Select(award =>
        {
            var detail = award.Clone();
            detail.MailAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO();
            return detail;
        }));
        var replies = new ReplyList(request);
        await AddRewardPushesAsync(replies, role, outcome.Rewards, pictureBefore.BadgePictureInfo);
        await AddDrawTaskPushesAsync(replies, role, outcome.DrawCount);
        replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, LobbyNotices.BuildRedDotReplaceNtf(
            PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdEnergyDraw,
            outcome.Energy.CountDrawRedDot(energies.Rules.DailyFreeDrawCount)));
        replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidEnergyInfoNtf, EnergyCenter.BuildEnergyInfoNtf(outcome.Energy));
        replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidEnergyDrawRsp, response);
        return replies.ToArray();
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseEnergyAccelerateReq)]
    public async Task<GamePacket[]> OnUseEnergyAccelerateReq(GamePacket request)
    {
        var accelerate = pbCSUseEnergyAccelerateReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await energies.InstallAcceleratorAsync(role.GameUlongUID, accelerate.EnergyAccelerateRatio);
        logger.LogInformation(
            "Energy accelerate ratio {EnergyAccelerateRatio} install by uid {Uid}: {Result}, capacity {EnergyAccelerateCapacity}",
            accelerate.EnergyAccelerateRatio,
            role.GameUlongUID,
            outcome.Result,
            outcome.Energy.InstalledAcceleratorCapacity);
        var response = new pbSCUseEnergyAccelerateRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            EnergyAccelerateInfo = outcome.Succeeded
                ? EnergyCenter.BuildAccelerateInfo(new EnergyAccelerator(outcome.Energy.EnergyAccelerateRatio, outcome.Energy.InstalledAcceleratorCapacity))
                : new pbCOMDT_ENERGY_ACCELERATE_INFO(),
        };
        return outcome.Succeeded
            ?
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidEnergyInfoNtf, EnergyCenter.BuildEnergyInfoNtf(outcome.Energy)),
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseEnergyAccelerateRsp, response),
            ]
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseEnergyAccelerateRsp, response)];
    }

    private async Task AddRewardPushesAsync(ReplyList replies, LMNRoleInfo role, BagOutcome rewards, pbCOMDT_BADGE_PICTURE_INFO pictureBefore)
    {
        var grantedBadges = rewards.ChangedItems.Where(item => item.ItemType == RoleItemType.Badge).ToArray();
        if (grantedBadges.Length > 0)
        {
            var pictureAfter = await badges.BuildGainedBadgePictureNtfAsync(role.GameUlongUID);
            if (!pictureAfter.BadgePictureInfo.Equals(pictureBefore))
            {
                replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, LobbyNotices.BuildRedDotReplaceNtf(
                    PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdBadgePicture,
                    NewBadgePictureRedDotBadgeNum));
            }

            replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await BuildBadgeGroupTasksAsync(role, grantedBadges));
            replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidUpdateBadgeNotify, BadgeManager.BuildUpdateBadgeNotify(grantedBadges));
            replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidGetBadgePictureNtf, pictureAfter);
        }

        if (rewards is { GrantedWalletCoins: true, Wallet: { } wallet })
        {
            replies.AddRange(wallet.BuildCoinValPushes(rewards.BeforeCoinNums.Keys));
        }

        var grantedItems = rewards.ChangedItems.Where(item => item.ItemType == RoleItemType.OutSideItem).ToArray();
        if (grantedItems.Length > 0)
        {
            replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(grantedItems));
        }

        if (rewards.Wallet is { } specialCoinWallet)
        {
            foreach (var specialCoinID in rewards.GrantedSpecialCoinIDs)
            {
                replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf, SpecialCoin.BuildSpecialCoinChangeNtf(
                    specialCoinID,
                    rewards.BeforeCoinNums[specialCoinID],
                    specialCoinWallet.GetCoinVal(specialCoinID)));
            }

            if (rewards.GrantedPokemonIds.Count == 0
                && rewards.BeforeCoinNums.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
            {
                replies.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
            }
        }

        foreach (var pokemonId in rewards.GrantedPokemonIds)
        {
            replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            });
        }

        if (rewards.GrantedPokemonIds.Count > 0)
        {
            replies.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            replies.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }

        foreach (var avatar in rewards.GrantedItems.Where(item => item.ItemType == RoleItemType.AvatarItem))
        {
            replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
            {
                UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain,
                AvatarInfo = avatar.ToAvatarInfo(),
            });
        }

        replies.AddRange(PokemonAvatarManager.BuildGainPushes(rewards.GrantedItems));
        replies.AddRange(ProfileGainPushes.Build(gameData, role, rewards.GrantedItems));
        replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role));
        replies.Add(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = rewards.AwardReason,
            ResourcesList = { rewards.Awards },
        });
    }

    private async Task<pbSCUpdateTaskInfoNtf> BuildBadgeGroupTasksAsync(LMNRoleInfo role, IEnumerable<BagItem> grantedBadges)
    {
        var tasks = new Dictionary<int, pbCOMDT_TASK_INFO>();
        var qualities = grantedBadges
            .Select(badge => gameData.BadgeBase.Find(badge.ItemId)?.BadgeQuality)
            .OfType<int>()
            .Distinct();
        foreach (var quality in qualities)
        {
            foreach (var task in (await taskUpdates.BuildBadgeGroupUpdateTaskInfoNtfAsync(role, quality)).TaskInfoList)
            {
                tasks[task.TaskId] = task;
            }
        }

        return new pbSCUpdateTaskInfoNtf { TaskInfoList = { tasks.Values.OrderBy(task => task.TaskId) } };
    }

    private async Task AddDrawTaskPushesAsync(ReplyList replies, LMNRoleInfo role, int drawCount)
    {
        var activityProgress = await activityManager.RecordConditionAsync(role, PB_ENUM_RES_CONDITION_ID.PbResConditionIdEnergeDraw, drawCount);
        replies.AddRange(await activityProgressPushes.BuildAsync(role, activityProgress));
        var battlePassProgress = await battlePassTasks.RecordConditionAsync(role, PB_ENUM_RES_CONDITION_ID.PbResConditionIdEnergeDraw, drawCount);
        if (battlePassProgress.Length > 0)
        {
            replies.Add(TaskUpdates.BuildUpdateTaskInfoNtf(battlePassProgress));
        }
    }
}
