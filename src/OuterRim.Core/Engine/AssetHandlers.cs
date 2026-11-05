#nullable enable
using System.Collections.Generic;
using System.Globalization;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// Gaining an asset (R-14.2): it goes in a fitting slot; if none is free, the player discards one of
    /// their cards to open one (R-14.4) or declines the new asset (R-14.5). Forced assets [UB] cannot be
    /// declined (R-25.1).
    /// </summary>
    internal sealed class GainAssetHandler : IResolutionHandler
    {
        private const string Decline = "decline";

        public string Id => Assets.GainHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            var asset = new AssetState
            {
                CardId = frame.Get("card"),
                Kind = frame.Get("kind"),
                Cost = frame.Vars.ContainsKey("cost") ? frame.GetInt("cost") : 0,
                Traits = frame.Vars.TryGetValue("traits", out var t) && t.Length > 0 ? new List<string>(t.Split(',')) : new List<string>(),
            };
            bool forced = frame.Vars.TryGetValue("forced", out var f) && f == "true";
            switch (frame.Step)
            {
                case 0:
                {
                    if (Assets.Valid(ctx.Content, player, Assets.With(player.Assets, asset)) || GoalMakesRoom(ctx, player, asset))
                    {
                        Gain(ctx, player, asset);
                        frame.Step = 2;
                        return StepResult.Call(TriggersHandler.For("asset_gained", player.Id,
                            new Dictionary<string, string> { ["gained_type"] = asset.Kind }));
                    }
                    var options = new List<DecisionOption>();
                    foreach (var a in player.Assets)
                    {
                        if (Assets.Valid(ctx.Content, player, Assets.With(player.Assets, asset, a)))
                        {
                            options.Add(new DecisionOption(a.CardId, a.CardId));
                        }
                    }
                    if (!forced)
                    {
                        options.Add(new DecisionOption(Decline, "rules.decline_asset"));
                    }
                    frame.Step = 1;
                    if (options.Count == 0)
                    {
                        // No single discard opens a slot of this type: only declining is possible.
                        frame.Set("choice", Decline);
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.no_free_slot", options, "choice"));
                }
                case 2:
                    return StepResult.Done();
                default:
                {
                    var choice = frame.Get("choice");
                    if (choice == Decline)
                    {
                        ctx.Events.Add(new AssetDeclinedEvent(player.Id, asset.CardId, asset.Kind));
                        return StepResult.Done();
                    }
                    Assets.Discard(player, Assets.Find(player, choice)!, "open_slot", ctx.Events);
                    Gain(ctx, player, asset);
                    return StepResult.Done();
                }
            }
        }

        /// <summary>
        /// GX1 Short Hauler (R-25.16): "Gain a crew while you have 2 crew ... (keep all 3 crew)": gaining the
        /// asset completes a ship goal whose flipped side has room for it, so no slot is needed now.
        /// </summary>
        private static bool GoalMakesRoom(ResolutionContext ctx, PlayerState player, AssetState asset)
        {
            var context = new Dictionary<string, string> { ["gained_type"] = asset.Kind };
            foreach (var a in Effects.AbilitySources.Of(ctx.Content, player))
            {
                if (!(Goals.Progress(a)?["on_event"] is Newtonsoft.Json.Linq.JObject on) || (string?)on["event"] != "asset_gained") continue;
                if (a.CardId != player.ShipSheetId) continue;
                player.Assets.Add(asset);
                bool met = Effects.Conditions.Eval(ctx.Content, ctx.State, player, on["filter"], null, context);
                var side = player.ShipSideId;
                player.ShipSideId = ctx.Content.OtherShipSide(side).Id;
                bool fits = Assets.Valid(ctx.Content, player, player.Assets);
                player.ShipSideId = side;
                player.Assets.Remove(asset);
                if (met && fits) return true;
            }
            return false;
        }

        private static void Gain(ResolutionContext ctx, PlayerState player, AssetState asset)
        {
            player.Assets.Add(asset);
            ctx.Events.Add(new AssetGainedEvent(player.Id, asset.CardId, asset.Kind));
        }
    }

    /// <summary>R-14.18, R-16.30: while the assets do not fit, the player discards assets of their choice.</summary>
    internal sealed class EnforceSlotsHandler : IResolutionHandler
    {
        public string Id => Assets.FitHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            if (frame.Step == 1)
            {
                Assets.Discard(player, Assets.Find(player, frame.Get("choice"))!, "no_slot", ctx.Events);
                frame.Step = 0;
            }
            if (Assets.AllFit(ctx.Content, player))
            {
                return StepResult.Done();
            }
            var options = new List<DecisionOption>();
            foreach (var a in player.Assets)
            {
                options.Add(new DecisionOption(a.CardId, a.CardId));
            }
            frame.Step = 1;
            return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.too_many_assets", options, "choice"));
        }
    }

    /// <summary>
    /// Buying a ship (R-16.27 to R-16.30, R-16.38): the price is the sheet's cost minus the barter, which
    /// must include the current ship; the old sheet returns to the supply, the new one is placed on its
    /// ship goal side with no damage, and assets that no longer fit are discarded.
    /// Removing the ship's market card from the game is the market's job (PLAN T2.5).
    /// </summary>
    internal sealed class BuyShipHandler : IResolutionHandler
    {
        public string Id => Assets.BuyShipHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            if (frame.Step == 1)
            {
                return StepResult.Done();
            }
            var sheet = frame.Get("sheet");
            var goalSide = ctx.Content.ShipGoalSide(sheet);
            var bartered = new List<AssetState>();
            if (frame.Vars.TryGetValue("barter", out var ids) && ids.Length > 0)
            {
                foreach (var id in ids.Split(','))
                {
                    bartered.Add(Assets.Find(player, id)!);
                }
            }
            int shipValue = ctx.Content.ShipSide(player.ShipSideId).Cost ?? 0;
            int barter = Assets.BarterValue(bartered, out _)!.Value + shipValue; // R-16.38: the current ship is always bartered
            int price = Assets.PriceAfterBarter(goalSide.Cost ?? 0, barter);
            if (!frame.Vars.ContainsKey("paid"))
            {
                PlayerRules.TrySpend(player, price, ctx.Events);
            }
            foreach (var a in bartered)
            {
                Assets.Discard(player, a, "barter", ctx.Events);
            }
            ctx.State.ShipSupply.Remove(sheet);
            ctx.State.ShipSupply.Add(player.ShipSheetId);
            Goals.ClearTokens(player, player.ShipSheetId); // R-18.2: progress does not carry to another ship
            var oldSheet = player.ShipSheetId;
            player.ShipSheetId = sheet;
            player.ShipSideId = goalSide.Id;
            player.ShipDamage = 0;
            ctx.Events.Add(new ShipChangedEvent(player.Id, oldSheet, sheet, goalSide.Id, price));
            var fit = new Frame(Assets.FitHandler);
            fit.Set("player", player.Id);
            frame.Step = 1;
            return StepResult.Call(fit);
        }

        /// <summary>Checks a ship purchase before it starts, so a rejected one changes nothing.</summary>
        internal static string? Validate(Content.GameContent content, GameState state, PlayerState player, string sheet,
            IReadOnlyList<string> barterIds)
        {
            if (!state.ShipSupply.Contains(sheet))
            {
                return "R-17.8: that ship sheet is not in the supply.";
            }
            var bartered = new List<AssetState>();
            foreach (var id in barterIds)
            {
                var a = Assets.Find(player, id);
                if (a == null)
                {
                    return "You do not hold " + id + ".";
                }
                bartered.Add(a);
            }
            var value = Assets.BarterValue(bartered, out var reason);
            if (value == null)
            {
                return reason;
            }
            int shipValue = content.ShipSide(player.ShipSideId).Cost ?? 0;
            int price = Assets.PriceAfterBarter(content.ShipGoalSide(sheet).Cost ?? 0, value.Value + shipValue);
            if (player.Credits < price)
            {
                return "R-9.8: the ship costs " + price.ToString(CultureInfo.InvariantCulture) + " after barter.";
            }
            return null;
        }
    }
}
