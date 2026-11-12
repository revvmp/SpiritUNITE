using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Pokemon;

public sealed record UsedGloryAnnounce(int UsedResID, int UsedResType, int AnnounceID);

public sealed record PersonaliseButtonSet(PB_ENUM_RES_ERROR_CODE Result, int[] PasterIds);

public sealed class CustomizeManager(
    GameData gameData,
    CustomizeRepository repository,
    PokemonAvatarManager pokemonAvatars,
    PokemonManager pokemon,
    RoleInfoStore roles)
{
    public const int DEFAULT_PERSONALISE_BUTTON_ID = 0;
    public const int DefaultGloryAnnounceID = 0;

    private const int ButtonIsUsing = 1;
    private const int ButtonNotUsing = 0;
    private const int NoExclusivePaster = 0;
    private const int UsedResTypePokemon = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon;
    private const int UsedResTypePaster = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster;

    public async Task<pbCOMDT_PERSONALISE_BUTTON_LIST> BuildPersonaliseButtonListAsync(LMNRoleInfo role)
    {
        var usedButtonIDs = (await repository.LoadUsedPersonaliseButtonsAsync(role.GameUlongUID)).Values.ToHashSet();
        return new pbCOMDT_PERSONALISE_BUTTON_LIST
        {
            PersonaliseButtonList =
            {
                OwnedPersonaliseButtons(role).Select(button =>
                    BuildPersonaliseButtonInfo(button.ItemId, usedButtonIDs.Contains(button.ItemId))),
            },
        };
    }

    public static pbSCUpdatePersonaliseButtonNtf? BuildPersonaliseButtonGainNtf(IEnumerable<RoleItemInfo> items)
    {
        RoleItemInfo[] buttons = [.. items.Where(item => item.ItemType == RoleItemType.NewPersonaliseButton)];
        return buttons.Length == 0
            ? null
            : new pbSCUpdatePersonaliseButtonNtf
            {
                PersionaliseButtonInfo = new pbCOMDT_PERSONALISE_BUTTON_LIST
                {
                    PersonaliseButtonList = { buttons.Select(button => BuildPersonaliseButtonInfo(button.ItemId, false)) },
                },
            };
    }

    public static IEnumerable<pbSCAddGloryAnnounceNtf> BuildGloryAnnounceGainNtfs(IEnumerable<RoleItemInfo> items) =>
        items.Where(item => item.ItemType == RoleItemType.GloryAnnounce)
            .Select(announce => new pbSCAddGloryAnnounceNtf
            {
                AnnounceID = announce.ItemId,
                Timelimited = announce.TimeLimited,
                NewTag = announce.NewTag,
            });

    public async Task<PersonaliseButtonSet> SetPersonaliseButtonAsync(LMNRoleInfo role, pbCSSetPersonaliseButtonReq selection)
    {
        int[] pasterIds = [.. (selection.PasterList?.PasterList ?? []).Distinct()];
        if (pasterIds.Length == 0)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, pasterIds);
        }

        var isDefault = selection.ButtonID == DEFAULT_PERSONALISE_BUTTON_ID;
        if (!isDefault && !IsPersonaliseButtonUsableOn(role, selection.ButtonID, pasterIds))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPersonaliseButtonNotValid, pasterIds);
        }

        var usablePasterIDs = await pokemonAvatars.LoadUsablePasterIDsAsync(role);
        if (pasterIds.Any(id => !usablePasterIDs.Contains(id) && !pokemon.CanBattleWith(role, id)))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPersonaliseButtonNotFindPaster, pasterIds);
        }

        await repository.SetPersonaliseButtonAsync(role.GameUlongUID, pasterIds, selection.ButtonID, isDefault);
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, pasterIds);
    }

    public async Task<pbCSDT_GLORY_ANNOUNCE_INFO[]> BuildGloryAnnounceListAsync(LMNRoleInfo role)
    {
        var uses = await repository.LoadUsedGloryAnnouncesAsync(role.GameUlongUID);
        return
        [
            .. role.GetOwnedItems(RoleItemType.GloryAnnounce)
                .Where(announce => gameData.GloryAnnounce.Contains(announce.ItemId))
                .OrderBy(announce => announce.ItemId)
                .Select(announce => BuildGloryAnnounceInfo(announce, uses)),
        ];
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetGloryAnnounceAsync(LMNRoleInfo role, pbCSDT_GLORY_ANNOUNCE_INFO announce)
    {
        var isDefault = announce.AnnounceID == DefaultGloryAnnounceID;
        if (!isDefault && !OwnsGloryAnnounce(role, announce.AnnounceID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        int[] pokemonIds = [.. announce.PokemonList.Distinct()];
        int[] pasterIds = [.. announce.PasterList.Distinct()];
        var usablePasterIDs = await pokemonAvatars.LoadUsablePasterIDsAsync(role);
        if (pokemonIds.Length + pasterIds.Length == 0
            || pokemonIds.Any(id => !pokemon.CanBattleWith(role, id))
            || pasterIds.Any(id => !usablePasterIDs.Contains(id)))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        UsedGloryAnnounce[] uses =
        [
            .. pokemonIds.Select(id => new UsedGloryAnnounce(id, UsedResTypePokemon, announce.AnnounceID)),
            .. pasterIds.Select(id => new UsedGloryAnnounce(id, UsedResTypePaster, announce.AnnounceID)),
        ];
        await repository.SetGloryAnnounceAsync(role.GameUlongUID, uses, isDefault);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ClearGloryAnnounceNewTagAsync(LMNRoleInfo role, int announceID)
    {
        if (!OwnsGloryAnnounce(role, announceID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        if (role.ClearNewTags(RoleItemType.GloryAnnounce, [announceID]).Length > 0)
        {
            await roles.SaveAsync(role);
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private bool OwnsGloryAnnounce(LMNRoleInfo role, int announceID) =>
        gameData.GloryAnnounce.Contains(announceID) && role.OwnsItem(RoleItemType.GloryAnnounce, announceID);

    private bool IsPersonaliseButtonUsableOn(LMNRoleInfo role, int buttonID, int[] pasterIds) =>
        gameData.PersonaliseButton.Find(buttonID) is { } button
        && role.OwnsItem(RoleItemType.NewPersonaliseButton, buttonID)
        && (button.PokemonAvatarId == NoExclusivePaster || pasterIds.All(id => id == button.PokemonAvatarId));

    private IEnumerable<RoleItemInfo> OwnedPersonaliseButtons(LMNRoleInfo role) =>
        role.GetOwnedItems(RoleItemType.NewPersonaliseButton)
            .Where(button => gameData.PersonaliseButton.Contains(button.ItemId))
            .OrderBy(button => button.ItemId);

    private static pbCOMDT_ONE_PERSONALISE_BUTTON_INFO BuildPersonaliseButtonInfo(int buttonID, bool isUsing) => new()
    {
        ButtonID = buttonID,
        ButtonIsUsing = isUsing ? ButtonIsUsing : ButtonNotUsing,
    };

    private static pbCSDT_GLORY_ANNOUNCE_INFO BuildGloryAnnounceInfo(RoleItemInfo announce, IReadOnlyList<UsedGloryAnnounce> uses)
    {
        var usedBy = uses.Where(use => use.AnnounceID == announce.ItemId).ToArray();
        return new pbCSDT_GLORY_ANNOUNCE_INFO
        {
            AnnounceID = announce.ItemId,
            PokemonList = { usedBy.Where(use => use.UsedResType == UsedResTypePokemon).Select(use => use.UsedResID) },
            PasterList = { usedBy.Where(use => use.UsedResType == UsedResTypePaster).Select(use => use.UsedResID) },
            Timelimited = announce.TimeLimited,
            NewTag = announce.NewTag,
        };
    }
}
