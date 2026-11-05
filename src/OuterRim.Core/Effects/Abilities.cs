#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Model;

namespace OuterRim.Core.Effects
{
    /// <summary>
    /// An ability on a card, located by the card id and a JSON path inside the card data
    /// (for example "abilities[1]" or "sides[0].abilities[2]"). Plain strings, so frames that
    /// refer to it save and load like any other state.
    /// </summary>
    public sealed class AbilityRef
    {
        public AbilityRef(string cardId, string path, JObject ability)
        {
            CardId = cardId;
            Path = path;
            Ability = ability;
        }

        public string CardId { get; }

        public string Path { get; }

        public JObject Ability { get; }

        public string TimingType => (string?)Ability["timing"]?["type"] ?? "";

        public string Key => CardId + "#" + Path;
    }

    /// <summary>Which abilities a player has: the faceup side of the character, the faceup side of the ship, and each asset.</summary>
    public static class AbilitySources
    {
        public static List<AbilityRef> Of(GameContent content, PlayerState player)
        {
            var result = new List<AbilityRef>();
            if (player.CharacterId.Length > 0 && content.HasCard(player.CharacterId))
            {
                AddList(result, player.CharacterId, content.Card(player.CharacterId), "sides[" + player.CharacterSide + "].abilities");
            }
            if (player.ShipSheetId.Length > 0 && content.HasCard(player.ShipSheetId))
            {
                var sheet = content.Card(player.ShipSheetId);
                var sides = (JArray?)sheet["sides"];
                for (int i = 0; sides != null && i < sides.Count; i++)
                {
                    if ((string?)sides[i]["image"] == player.ShipSideId)
                    {
                        AddList(result, player.ShipSheetId, sheet, "sides[" + i + "].abilities");
                    }
                }
            }
            foreach (var asset in player.Assets)
            {
                if (content.HasCard(asset.CardId))
                {
                    AddList(result, asset.CardId, content.Card(asset.CardId), "abilities");
                }
            }
            return result;
        }

        private static void AddList(List<AbilityRef> result, string cardId, JObject card, string path)
        {
            var list = card.SelectToken(path) as JArray;
            for (int i = 0; list != null && i < list.Count; i++)
            {
                result.Add(new AbilityRef(cardId, path + "[" + i + "]", (JObject)list[i]));
            }
        }

        public static AbilityRef? Find(GameContent content, PlayerState player, string cardId, string path)
        {
            foreach (var a in Of(content, player))
            {
                if (a.CardId == cardId && a.Path == path)
                {
                    return a;
                }
            }
            return null;
        }
    }
}
