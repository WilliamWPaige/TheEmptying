using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Saves;
using TheEmptying.Core.Scheduling;
using TheEmptying.Modules.MapMovement;
using TheEmptying.Modules.Wealth;

namespace TheEmptying.Modules.Land
{
    /// <summary>
    /// Zones painted onto the grid, plots bought or leased from the crown,
    /// and monthly land tax and lease charges. Depends on map/movement (the
    /// grid, and "nearest free spot" to a buyer) and wealth (transfers, and
    /// it fills in wealth's property-value hook).
    /// </summary>
    public sealed class LandModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.land";

        public const string LeaseRatePercentOfPriceSettingId = "setting.land.lease_rate_percent_of_price";
        public const string CrownGroundsSizeSettingId = "setting.land.crown_grounds_size";
        public const string CrownProximityMaxModifierPercentSettingId = "setting.land.crown_proximity_max_modifier_percent";
        public const string CrownProximityFalloffCellsSettingId = "setting.land.crown_proximity_falloff_cells";

        public const string PlotBoughtEventId = "event.land.plot_bought";
        public const string PlotLeasedEventId = "event.land.plot_leased";
        public const string LeaseEndedEventId = "event.land.lease_ended";
        public const string TaxUnpaidEventId = "event.land.tax_unpaid";

        /// <summary>
        /// Land has no declared dependency on People (Wealth depends on
        /// People; Land depends on Wealth, not People directly), so this is
        /// the event's string ID, not PeopleModule.PersonDiedEventId -
        /// events are the one channel a module can listen on without a code
        /// reference.
        /// </summary>
        private const string PersonDiedEventId = "event.people.person_died";

        public static readonly string[] ZoneIds =
        {
            "zone.residential_low", "zone.residential_middle", "zone.residential_high",
            "zone.commercial", "zone.industrial", "zone.farm"
        };

        private sealed class ZoneActionSystem : ISimSystem
        {
            private readonly LandModule _owner;
            public ZoneActionSystem(LandModule owner) => _owner = owner;
            public string Id => ModuleId + ".zone_actions";
            public void Update(long tick) => _owner.ProcessPendingZoneActions();
        }

        private readonly MapMovementModule _mapMovement;
        private readonly WealthModule _wealth;

        private SettingsRegistry _settings;
        private EventBus _events;
        private EntityRegistry _entities;
        private Func<long> _currentTick;

        private MapGrid _grid;
        private string[] _zoneByCell;
        private EntityId?[] _plotByCell;
        private EntityTable<PlotState> _plots;
        private CellRect _crownGrounds;

        private readonly Queue<(CellRect Rect, string ZoneId)> _pendingZoneActions = new Queue<(CellRect Rect, string ZoneId)>();

        private Func<CellRect, double> _waterModifierProvider = _ => 0.0;
        private Func<CellRect, double> _roadModifierProvider = _ => 0.0;

        public LandModule(MapMovementModule mapMovement, WealthModule wealth)
        {
            _mapMovement = mapMovement ?? throw new ArgumentNullException(nameof(mapMovement));
            _wealth = wealth ?? throw new ArgumentNullException(nameof(wealth));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { MapMovementModule.ModuleId, WealthModule.ModuleId };
        public int SectionVersion => 1;

        public CellRect CrownGrounds => _crownGrounds;
        public IEnumerable<KeyValuePair<EntityId, PlotState>> Plots => _plots.Rows();

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _entities = context.Entities;
            _currentTick = () => context.CurrentTick;

            _grid = _mapMovement.Grid;
            _zoneByCell = new string[_grid.Width * _grid.Depth];
            _plotByCell = new EntityId?[_grid.Width * _grid.Depth];
            _plots = new EntityTable<PlotState>(context.Entities);

            int groundsSize = (int)_settings.Get(CrownGroundsSizeSettingId);
            int centerX = _grid.Width / 2;
            int centerY = _grid.Depth / 2;
            _crownGrounds = new CellRect(centerX - groundsSize / 2, centerY - groundsSize / 2, groundsSize, groundsSize);

            _wealth.AddPropertyValueProvider(GetPropertyValue);

            context.Subscribe<NewMonthEvent>(OnNewMonth);
            context.Subscribe(PersonDiedEventId, OnPersonDied);
            context.RegisterSystem(new ZoneActionSystem(this), 1, 0, 0);

            RegisterCommands(context);
        }

        public void Shutdown()
        {
        }

        #region Hooks for later layers

        public void SetWaterModifierProvider(Func<CellRect, double> provider) => _waterModifierProvider = provider ?? (_ => 0.0);
        public void SetRoadModifierProvider(Func<CellRect, double> provider) => _roadModifierProvider = provider ?? (_ => 0.0);

        #endregion

        #region Zoning

        /// <summary>Queues a zone paint; applied at the start of the next tick, like other player actions. zoneId null means "remove zone."</summary>
        public void RequestSetZone(CellRect rect, string zoneId)
        {
            _pendingZoneActions.Enqueue((rect, zoneId));
        }

        public void RequestRemoveZone(CellRect rect) => RequestSetZone(rect, null);

        private void ProcessPendingZoneActions()
        {
            while (_pendingZoneActions.Count > 0)
            {
                (CellRect rect, string zoneId) = _pendingZoneActions.Dequeue();
                ApplyZoneAction(rect, zoneId);
            }
        }

        private void ApplyZoneAction(CellRect rect, string zoneId)
        {
            foreach (GridPoint cell in rect.Cells())
            {
                if (!_grid.InBounds(cell) || _crownGrounds.Contains(cell))
                {
                    continue;
                }

                int index = _grid.IndexOf(cell);
                if (_plotByCell[index].HasValue)
                {
                    continue; // cells under a plot can't be rezoned
                }

                _zoneByCell[index] = zoneId;
            }
        }

        public string GetZoneAt(GridPoint cell) => _grid.InBounds(cell) ? _zoneByCell[_grid.IndexOf(cell)] : null;

        #endregion

        #region Plots: placement

        public int PlotSize(string zoneId) => (int)_settings.Get(PlotSizeSettingId(zoneId));

        private bool FitsZone(CellRect rect, string zoneId)
        {
            foreach (GridPoint cell in rect.Cells())
            {
                if (!_grid.InBounds(cell) || _zoneByCell[_grid.IndexOf(cell)] != zoneId)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsFree(CellRect rect)
        {
            foreach (GridPoint cell in rect.Cells())
            {
                if (_plotByCell[_grid.IndexOf(cell)].HasValue)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The free spot of the zone's standard size, inside a matching zone, nearest to the given cell. False if none exists anywhere on the map.</summary>
        public bool TryFindFreeSpot(string zoneId, GridPoint near, out CellRect rect)
        {
            int size = PlotSize(zoneId);

            var candidates = new List<GridPoint>();
            for (int y = 0; y <= _grid.Depth - size; y++)
            {
                for (int x = 0; x <= _grid.Width - size; x++)
                {
                    candidates.Add(new GridPoint(x, y));
                }
            }

            candidates.Sort((a, b) => SquaredDistance(a, near).CompareTo(SquaredDistance(b, near)));

            foreach (GridPoint origin in candidates)
            {
                var candidate = new CellRect(origin.X, origin.Y, size, size);
                if (FitsZone(candidate, zoneId) && IsFree(candidate))
                {
                    rect = candidate;
                    return true;
                }
            }

            rect = default;
            return false;
        }

        private static long SquaredDistance(GridPoint a, GridPoint b)
        {
            long dx = a.X - b.X;
            long dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        #endregion

        #region Plots: pricing

        public Money GetCurrentPrice(string zoneId, CellRect rect)
        {
            Money basePrice = Money.FromShil(_settings.Get(PlotPriceSettingId(zoneId)));
            double modifierPercent = CrownProximityModifierPercent(rect) + _waterModifierProvider(rect) * 100.0 + _roadModifierProvider(rect) * 100.0;
            return basePrice.ApplyPercent(modifierPercent);
        }

        private double CrownProximityModifierPercent(CellRect rect)
        {
            double maxModifierPercent = _settings.Get(CrownProximityMaxModifierPercentSettingId);
            double falloffCells = _settings.Get(CrownProximityFalloffCellsSettingId);
            if (falloffCells <= 0)
            {
                return 0.0;
            }

            double distance = DistanceFromCrownGroundsEdge(rect);
            double fraction = Math.Max(0.0, 1.0 - distance / falloffCells);
            return maxModifierPercent * fraction;
        }

        /// <summary>Approximates both shapes as circles: cheap, and close enough for a placeholder modifier.</summary>
        private double DistanceFromCrownGroundsEdge(CellRect rect)
        {
            GridPoint rectCenter = rect.Center;
            GridPoint groundsCenter = _crownGrounds.Center;
            double centerDistance = Math.Sqrt(SquaredDistance(rectCenter, groundsCenter));
            double groundsHalfSize = _crownGrounds.Width / 2.0;
            double rectHalfSize = Math.Max(rect.Width, rect.Height) / 2.0;
            return Math.Max(0.0, centerDistance - groundsHalfSize - rectHalfSize);
        }

        private Money GetTaxPerMonth(string zoneId) => Money.FromShil(_settings.Get(TaxPerMonthSettingId(zoneId)));

        private static string StripZonePrefix(string zoneId) => zoneId != null && zoneId.StartsWith("zone.") ? zoneId.Substring("zone.".Length) : zoneId;
        private static string PlotSizeSettingId(string zoneId) => $"setting.land.plot_size.{StripZonePrefix(zoneId)}";
        private static string PlotPriceSettingId(string zoneId) => $"setting.land.plot_price.{StripZonePrefix(zoneId)}";
        private static string TaxPerMonthSettingId(string zoneId) => $"setting.land.tax_per_month.{StripZonePrefix(zoneId)}";

        public Money GetPropertyValue(EntityId person)
        {
            Money total = Money.Zero;
            foreach (KeyValuePair<EntityId, PlotState> row in _plots.Rows())
            {
                if (row.Value.Owner == person)
                {
                    total += GetCurrentPrice(row.Value.ZoneId, row.Value.Rect);
                }
            }

            return total;
        }

        #endregion

        #region Plots: buying and leasing

        public bool TryBuyPlot(EntityId buyer, string zoneId, GridPoint? at, out EntityId plotId, out string error)
        {
            if (_wealth.IsFounder(buyer))
            {
                plotId = EntityId.None;
                error = "A crown servant owns nothing.";
                return false;
            }

            if (!ValidateZoneId(zoneId, out error) || !ResolveSpot(zoneId, buyer, at, out CellRect rect, out error))
            {
                plotId = EntityId.None;
                return false;
            }

            Money price = GetCurrentPrice(zoneId, rect);
            if (!_wealth.TryTransfer(AccountId.Person(buyer), AccountId.Crown, price, TransferReason.Purchase))
            {
                plotId = EntityId.None;
                error = $"{buyer} can't afford {price}.";
                return false;
            }

            plotId = CreatePlot(zoneId, rect);
            _plots.TryGet(plotId, out PlotState plot);
            plot.Owner = buyer;

            _events.Post(PlotBoughtEventId, _currentTick(), new EventData().SetEntity("plot", plotId).SetEntity("buyer", buyer).SetLong("price", price.HundredthsOfShil));
            error = null;
            return true;
        }

        public bool TryLeasePlot(EntityId leaseholder, string zoneId, GridPoint? at, out EntityId plotId, out string error)
        {
            if (!ValidateZoneId(zoneId, out error) || !ResolveSpot(zoneId, leaseholder, at, out CellRect rect, out error))
            {
                plotId = EntityId.None;
                return false;
            }

            plotId = CreatePlot(zoneId, rect);
            _plots.TryGet(plotId, out PlotState plot);
            plot.Leaseholder = leaseholder;

            _events.Post(PlotLeasedEventId, _currentTick(), new EventData().SetEntity("plot", plotId).SetEntity("leaseholder", leaseholder));
            error = null;
            return true;
        }

        private bool ValidateZoneId(string zoneId, out string error)
        {
            if (zoneId != null && _settings.IsDeclared(PlotSizeSettingId(zoneId)))
            {
                error = null;
                return true;
            }

            error = $"Unknown zone '{zoneId}'.";
            return false;
        }

        private bool ResolveSpot(string zoneId, EntityId reference, GridPoint? at, out CellRect rect, out string error)
        {
            if (at.HasValue)
            {
                int size = PlotSize(zoneId);
                rect = new CellRect(at.Value.X, at.Value.Y, size, size);
                if (!FitsZone(rect, zoneId) || !IsFree(rect))
                {
                    error = $"A {size}x{size} {zoneId} plot doesn't fit free at {at.Value}.";
                    return false;
                }

                error = null;
                return true;
            }

            GridPoint near = _mapMovement.IsMover(reference) ? _mapMovement.GetCell(reference) : _crownGrounds.Center;
            if (!TryFindFreeSpot(zoneId, near, out rect))
            {
                error = $"No free spot for a {zoneId} plot anywhere on the map.";
                return false;
            }

            error = null;
            return true;
        }

        private EntityId CreatePlot(string zoneId, CellRect rect)
        {
            EntityId id = _entities.Create(EntityKind.Site);
            var plot = new PlotState { ZoneId = zoneId, Rect = rect };
            _plots.Add(id, plot);

            foreach (GridPoint cell in rect.Cells())
            {
                _plotByCell[_grid.IndexOf(cell)] = id;
            }

            return id;
        }

        public bool TryGetPlot(EntityId id, out PlotState plot) => _plots.TryGet(id, out plot);

        #endregion

        #region Monthly lease and tax charges

        private void OnNewMonth(NewMonthEvent evt)
        {
            long tick = evt.Tick;

            foreach (KeyValuePair<EntityId, PlotState> row in _plots.Rows().ToList())
            {
                EntityId plotId = row.Key;
                PlotState plot = row.Value;

                if (plot.Leaseholder.HasValue)
                {
                    ChargeLease(tick, plotId, plot);
                }
                else if (plot.Owner.HasValue)
                {
                    ChargeTax(tick, plotId, plot);
                }
            }
        }

        private void ChargeLease(long tick, EntityId plotId, PlotState plot)
        {
            double ratePercent = _settings.Get(LeaseRatePercentOfPriceSettingId);
            Money price = GetCurrentPrice(plot.ZoneId, plot.Rect);
            Money leaseAmount = new Money((long)Math.Round(price.HundredthsOfShil * ratePercent / 100.0, MidpointRounding.AwayFromZero));

            EntityId leaseholder = plot.Leaseholder.Value;
            if (!_wealth.TryTransfer(AccountId.Person(leaseholder), AccountId.Crown, leaseAmount, TransferReason.Rent))
            {
                plot.Leaseholder = null;
                _events.Post(LeaseEndedEventId, tick, new EventData().SetEntity("plot", plotId).SetEntity("leaseholder", leaseholder).SetString("reason", "unpaid"));
            }
        }

        /// <summary>Property of the dead: their plots return to the crown (an owned plot) or the lease simply ends (a leased one).</summary>
        private void OnPersonDied(PostedEvent posted)
        {
            EntityId person = posted.Data.GetEntity("person");
            foreach (KeyValuePair<EntityId, PlotState> row in _plots.Rows())
            {
                if (row.Value.Owner == person)
                {
                    row.Value.Owner = null;
                }
                else if (row.Value.Leaseholder == person)
                {
                    row.Value.Leaseholder = null;
                }
            }
        }

        private void ChargeTax(long tick, EntityId plotId, PlotState plot)
        {
            Money tax = GetTaxPerMonth(plot.ZoneId);
            bool hadDebtBefore = plot.TaxDebt.HundredthsOfShil > 0;

            if (!_wealth.TryTransfer(AccountId.Person(plot.Owner.Value), AccountId.Crown, tax, TransferReason.Tax))
            {
                plot.TaxDebt += tax;
                if (!hadDebtBefore)
                {
                    _events.Post(TaxUnpaidEventId, tick, new EventData().SetEntity("plot", plotId).SetEntity("owner", plot.Owner.Value));
                }
            }
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("zones", "zones - a summary of how many cells are zoned as each type.", args =>
            {
                var sb = new System.Text.StringBuilder();
                int unzoned = 0, grounds = 0;
                var counts = ZoneIds.ToDictionary(z => z, _ => 0);

                for (int i = 0; i < _zoneByCell.Length; i++)
                {
                    string zoneId = _zoneByCell[i];
                    var cell = new GridPoint(i % _grid.Width, i / _grid.Width);
                    if (_crownGrounds.Contains(cell))
                    {
                        grounds++;
                    }
                    else if (zoneId != null && counts.ContainsKey(zoneId))
                    {
                        counts[zoneId]++;
                    }
                    else
                    {
                        unzoned++;
                    }
                }

                foreach (string zoneId in ZoneIds)
                {
                    sb.AppendLine($"{zoneId}: {counts[zoneId]} cells");
                }

                sb.AppendLine($"crown grounds: {grounds} cells");
                sb.Append($"unzoned: {unzoned} cells");
                return sb.ToString();
            }));

            context.RegisterCommand(new ConsoleCommand("plots", "plots - lists every plot.", args =>
            {
                List<string> lines = _plots.Rows()
                    .Select(row => $"{row.Key}: {row.Value.ZoneId} at ({row.Value.Rect.X},{row.Value.Rect.Y}) {row.Value.Rect.Width}x{row.Value.Rect.Height}, " +
                                   (row.Value.Owner.HasValue ? $"owner {row.Value.Owner.Value}" : row.Value.Leaseholder.HasValue ? $"leaseholder {row.Value.Leaseholder.Value}" : "crown"))
                    .ToList();

                return lines.Count == 0 ? "No plots." : string.Join("\n", lines);
            }));

            context.RegisterCommand(new ConsoleCommand("plot", "plot <plotId> - shows one plot in full.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id) || !_plots.Has(id))
                {
                    return "Usage: plot <plotId> (e.g. e12.3)";
                }

                return string.Join("\n", Inspect(id));
            }));

            context.RegisterCommand(new ConsoleCommand("buyplot", "buyplot <personId> <zone> [x y] - buys a plot; nearest free spot if x y omitted.", args => HandleBuyOrLease(args, isLease: false)));
            context.RegisterCommand(new ConsoleCommand("leaseplot", "leaseplot <personId> <zone> [x y] - leases a plot; nearest free spot if x y omitted.", args => HandleBuyOrLease(args, isLease: true)));
        }

        private string HandleBuyOrLease(string[] args, bool isLease)
        {
            string usage = $"Usage: {(isLease ? "leaseplot" : "buyplot")} <personId> <zone> [x y]";

            if ((args.Length != 2 && args.Length != 4) || !EntityId.TryParse(args[0], out EntityId personId))
            {
                return usage;
            }

            string zoneId = args[1];
            GridPoint? at = null;
            if (args.Length == 4)
            {
                if (!ConsoleArgs.TryParseInt(args[2], out int x) || !ConsoleArgs.TryParseInt(args[3], out int y))
                {
                    return usage;
                }

                at = new GridPoint(x, y);
            }

            bool success;
            EntityId plotId;
            string error;
            if (isLease)
            {
                success = TryLeasePlot(personId, zoneId, at, out plotId, out error);
            }
            else
            {
                success = TryBuyPlot(personId, zoneId, at, out plotId, out error);
            }

            return success ? $"{(isLease ? "Leased" : "Bought")} plot {plotId}." : error;
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteStringList(_zoneByCell);

            writer.WriteInt(_plots.Count);
            foreach (KeyValuePair<EntityId, PlotState> row in _plots.Rows())
            {
                writer.WriteEntityId(row.Key);
                PlotState plot = row.Value;

                writer.WriteString(plot.ZoneId);
                writer.WriteInt(plot.Rect.X);
                writer.WriteInt(plot.Rect.Y);
                writer.WriteInt(plot.Rect.Width);
                writer.WriteInt(plot.Rect.Height);

                writer.WriteBool(plot.Owner.HasValue);
                if (plot.Owner.HasValue)
                {
                    writer.WriteEntityId(plot.Owner.Value);
                }

                writer.WriteBool(plot.Leaseholder.HasValue);
                if (plot.Leaseholder.HasValue)
                {
                    writer.WriteEntityId(plot.Leaseholder.Value);
                }

                writer.WriteLong(plot.TaxDebt.HundredthsOfShil);
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            _zoneByCell = reader.ReadStringList().ToArray();

            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var plot = new PlotState
                {
                    ZoneId = reader.ReadString(),
                    Rect = new CellRect(reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt())
                };

                if (reader.ReadBool())
                {
                    plot.Owner = reader.ReadEntityId();
                }

                if (reader.ReadBool())
                {
                    plot.Leaseholder = reader.ReadEntityId();
                }

                plot.TaxDebt = Money.FromHundredthsOfShil(reader.ReadLong());

                _plots.Add(id, plot);
                foreach (GridPoint cell in plot.Rect.Cells())
                {
                    _plotByCell[_grid.IndexOf(cell)] = id;
                }
            }
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (!_plots.TryGet(entity, out PlotState plot))
            {
                yield break;
            }

            yield return $"Plot: {plot.ZoneId} at ({plot.Rect.X},{plot.Rect.Y}) {plot.Rect.Width}x{plot.Rect.Height}, price {GetCurrentPrice(plot.ZoneId, plot.Rect)}";

            if (plot.Owner.HasValue)
            {
                yield return $"Owner: {plot.Owner.Value}, tax debt: {plot.TaxDebt}";
            }
            else if (plot.Leaseholder.HasValue)
            {
                yield return $"Leaseholder: {plot.Leaseholder.Value}";
            }
            else
            {
                yield return "Owned by the crown; not sold or leased.";
            }
        }

        #endregion
    }
}
