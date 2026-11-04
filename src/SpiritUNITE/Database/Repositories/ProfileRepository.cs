using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class ProfileRepository(DataOptions options)
{
    public async Task<ProfileStyle> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var saved = await database.GetTable<RoleProfileStyle>()
            .FirstOrDefaultAsync(style => style.GameUlongUID == gameUlongUID);
        var wearCardFaceIDs = await database.GetTable<RoleCardFaceWear>()
            .Where(wear => wear.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(wear => wear.PokemonID, wear => wear.CardFaceID);
        if (saved is null)
        {
            return wearCardFaceIDs.Count == 0
                ? ProfileStyle.CreateDefault()
                : throw new InvalidDataException($"role_card_face_wear rows exist without role_profile_style for uid {gameUlongUID}");
        }

        return new ProfileStyle(
            saved.CurHeadIconID,
            pbCOMDT_PLAYER_CONTESTANT_INFO.Parser.ParseFrom(saved.PlayerContestantInfo),
            pbCOMDT_INDIVIDUAL_LABEL_INFO.Parser.ParseFrom(saved.IndividualLabelInfo).LabelList,
            saved.IsAnonymity,
            saved.IsHideInfo,
            saved.CurCanopyID,
            saved.HeadType,
            saved.CurHeadResID,
            wearCardFaceIDs);
    }

    public async Task SaveAsync(ulong gameUlongUID, ProfileStyle style)
    {
        await using var database = new DataConnection(options);
        await SaveStyleRowAsync(database, gameUlongUID, style);
    }

    public async Task SaveWearCardFaceAsync(ulong gameUlongUID, ProfileStyle style, int pokemonID)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await SaveStyleRowAsync(database, gameUlongUID, style);
        var cardFaceID = style.GetWearCardFaceID(pokemonID);
        if (cardFaceID == ProfileStyle.NoCardFaceID)
        {
            await database.GetTable<RoleCardFaceWear>()
                .Where(wear => wear.GameUlongUID == gameUlongUID && wear.PokemonID == pokemonID)
                .DeleteAsync();
        }
        else
        {
            await database.InsertOrReplaceAsync(new RoleCardFaceWear
            {
                GameUlongUID = gameUlongUID,
                PokemonID = pokemonID,
                CardFaceID = cardFaceID,
            });
        }

        await transaction.CommitAsync();
    }

    private static Task SaveStyleRowAsync(DataConnection database, ulong gameUlongUID, ProfileStyle style)
    {
        var (headType, curHeadResID) = style.CurPersonStyleHead;
        return database.InsertOrReplaceAsync(new RoleProfileStyle
        {
            GameUlongUID = gameUlongUID,
            CurHeadIconID = style.CurHeadIconID,
            PlayerContestantInfo = style.PlayerContestantInfo.ToByteArray(),
            IndividualLabelInfo = style.BuildLableInfo().ToByteArray(),
            IsAnonymity = style.IsAnonymity,
            IsHideInfo = style.IsHideInfo,
            CurCanopyID = style.CurCanopyID,
            HeadType = headType,
            CurHeadResID = curHeadResID,
        });
    }
}
