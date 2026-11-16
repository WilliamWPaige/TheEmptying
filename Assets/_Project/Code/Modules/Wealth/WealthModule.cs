using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Core.Console;
using TheEmptying.Core.Content;
using TheEmptying.Core.Economy;
using TheEmptying.Core.Entities;
using TheEmptying.Core.Events;
using TheEmptying.Core.Modules;
using TheEmptying.Core.Randomness;
using TheEmptying.Core.Saves;
using TheEmptying.Modules.People;

namespace TheEmptying.Modules.Wealth
{
    /// <summary>
    /// Money accounts for the crown and every person, and the monthly class
    /// check (money and power decide lower, middle or upper). Depends on
    /// People: it reads ages for the child-inherits-class hook and registers
    /// itself as People's class provider, so needs gated by class start
    /// applying the moment someone rises.
    /// </summary>
    public sealed class WealthModule : IModule, ISaveableModule, IInspectableModule
    {
        public const string ModuleId = "module.wealth";

        public const string CrownStartingShilSettingId = "setting.wealth.crown_starting_shil";
        public const string ImmigrantStartingShilMinSettingId = "setting.wealth.immigrant_starting_shil_min";
        public const string ImmigrantStartingShilMaxSettingId = "setting.wealth.immigrant_starting_shil_max";
        public const string LaborerWagePerDaySettingId = "setting.wealth.laborer_wage_per_day_shil";
        public const string SkilledWagePerDaySettingId = "setting.wealth.skilled_wage_per_day_shil";
        public const string ExpertWagePerDaySettingId = "setting.wealth.expert_wage_per_day_shil";
        public const string SoldierWagePerDaySettingId = "setting.wealth.soldier_wage_per_day_shil";
        public const string MiddleClassWealthDaysSettingId = "setting.wealth.middle_class_wealth_days";
        public const string UpperClassWealthDaysSettingId = "setting.wealth.upper_class_wealth_days";
        public const string MonthsBelowBeforeDropSettingId = "setting.wealth.months_below_before_drop";
        public const string ContentToStayLowerChanceSettingId = "setting.wealth.content_to_stay_lower_chance";

        public const string ClassChangedEventId = "event.wealth.class_changed";

        private static readonly TransferReason[] AllReasons = (TransferReason[])Enum.GetValues(typeof(TransferReason));

        private sealed class CrownAccountState
        {
            public Money Balance = Money.Zero;
            public MonthlyLedger Current = new MonthlyLedger();
            public MonthlyLedger Previous = new MonthlyLedger();
        }

        private readonly PeopleModule _people;

        private SettingsRegistry _settings;
        private EventBus _events;
        private SimRandom _random;

        private EntityTable<PersonWealthState> _wealth;
        private readonly CrownAccountState _crownAccount = new CrownAccountState();

        private Func<EntityId, SocialClass> _jobTierProvider = _ => SocialClass.Lower;
        private Func<EntityId, SocialClass?> _powerProvider = _ => null;

        /// <summary>Land (plots) and Buildings (construction value) each add their own; summed, not replaced.</summary>
        private readonly List<Func<EntityId, Money>> _propertyValueProviders = new List<Func<EntityId, Money>>();

        public WealthModule(PeopleModule people)
        {
            _people = people ?? throw new ArgumentNullException(nameof(people));
        }

        public string Id => ModuleId;
        public int Version => 1;
        public IReadOnlyList<string> Dependencies { get; } = new[] { PeopleModule.ModuleId };
        public int SectionVersion => 1;

        public void Initialize(ModuleContext context)
        {
            _settings = context.Settings;
            _events = context.Events;
            _random = context.Random;
            _wealth = new EntityTable<PersonWealthState>(context.Entities);

            _people.SetClassProvider(GetClass);

            RegisterCommands(context);
            context.Subscribe<NewMonthEvent>(OnNewMonth);

            if (!context.IsLoading)
            {
                _crownAccount.Balance = Money.FromShil(_settings.Get(CrownStartingShilSettingId));

                int min = (int)_settings.Get(ImmigrantStartingShilMinSettingId);
                int max = (int)_settings.Get(ImmigrantStartingShilMaxSettingId);
                foreach (KeyValuePair<EntityId, PersonState> row in _people.People)
                {
                    var state = new PersonWealthState
                    {
                        Balance = Money.FromShil(_random.NextInt(min, max + 1)),
                        Class = SocialClass.Lower,
                        Ambition = RollAmbition()
                    };
                    _wealth.Add(row.Key, state);
                }
            }
        }

        public void Shutdown()
        {
        }

        #region Hooks for later layers

        public void SetJobTierProvider(Func<EntityId, SocialClass> provider) => _jobTierProvider = provider ?? (_ => SocialClass.Lower);
        public void SetPowerProvider(Func<EntityId, SocialClass?> provider) => _powerProvider = provider ?? (_ => null);

        /// <summary>Adds a source of property value (land, buildings...); every registered source is summed by GetWealth.</summary>
        public void AddPropertyValueProvider(Func<EntityId, Money> provider)
        {
            if (provider != null)
            {
                _propertyValueProviders.Add(provider);
            }
        }

        /// <summary>No births yet: set the child's parent directly, so the monthly class check can apply "children take their parent's class" until adulthood.</summary>
        public void SetParent(EntityId child, EntityId parent) => EnsureAccount(child).ParentId = parent;

        #endregion

        #region Queries

        public Money GetBalance(AccountId account) =>
            account.IsCrown ? _crownAccount.Balance : (_wealth.TryGet(account.Entity, out PersonWealthState s) ? s.Balance : Money.Zero);

        /// <summary>Cash plus every registered source of property value (land, buildings).</summary>
        public Money GetWealth(EntityId person)
        {
            Money total = GetBalance(AccountId.Person(person));
            foreach (Func<EntityId, Money> provider in _propertyValueProviders)
            {
                total += provider(person);
            }

            return total;
        }

        public SocialClass GetClass(EntityId person) => _wealth.TryGet(person, out PersonWealthState s) ? s.Class : SocialClass.Lower;

        public double GetAmbition(EntityId person) => _wealth.TryGet(person, out PersonWealthState s) ? s.Ambition : 0.0;

        public IReadOnlyDictionary<TransferReason, Money> GetLedgerIn(AccountId account, bool currentMonth = true) => Ledger(account, currentMonth).In;
        public IReadOnlyDictionary<TransferReason, Money> GetLedgerOut(AccountId account, bool currentMonth = true) => Ledger(account, currentMonth).Out;

        private MonthlyLedger Ledger(AccountId account, bool currentMonth)
        {
            if (account.IsCrown)
            {
                return currentMonth ? _crownAccount.Current : _crownAccount.Previous;
            }

            if (_wealth.TryGet(account.Entity, out PersonWealthState s))
            {
                return currentMonth ? s.Current : s.Previous;
            }

            return new MonthlyLedger();
        }

        #endregion

        #region Transfers

        /// <summary>Moves money from one account to another. False, with nothing changed, if the payer can't afford it.</summary>
        public bool TryTransfer(AccountId from, AccountId to, Money amount, TransferReason reason)
        {
            if (amount.HundredthsOfShil <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Transfer amount must be positive.");
            }

            if (GetBalance(from) < amount)
            {
                return false;
            }

            SetBalance(from, GetBalance(from) - amount);
            SetBalance(to, GetBalance(to) + amount);
            CurrentLedger(from).RecordOut(reason, amount);
            CurrentLedger(to).RecordIn(reason, amount);
            return true;
        }

        /// <summary>Credits an account from nowhere (an arrival grant, or the "givemoney" test command). No payer, so it can't fail.</summary>
        public void Grant(AccountId to, Money amount, TransferReason reason = TransferReason.Grant)
        {
            if (amount.HundredthsOfShil <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Grant amount must be positive.");
            }

            SetBalance(to, GetBalance(to) + amount);
            CurrentLedger(to).RecordIn(reason, amount);
        }

        private void SetBalance(AccountId account, Money value)
        {
            if (account.IsCrown)
            {
                _crownAccount.Balance = value;
            }
            else
            {
                EnsureAccount(account.Entity).Balance = value;
            }
        }

        private MonthlyLedger CurrentLedger(AccountId account) => account.IsCrown ? _crownAccount.Current : EnsureAccount(account.Entity).Current;

        #endregion

        #region Monthly class check

        private void OnNewMonth(NewMonthEvent evt)
        {
            long tick = evt.Tick;

            foreach (KeyValuePair<EntityId, PersonState> row in _people.People)
            {
                EnsureAccount(row.Key);
            }

            Money laborerWage = Money.FromShil(_settings.Get(LaborerWagePerDaySettingId));
            double middleDays = _settings.Get(MiddleClassWealthDaysSettingId);
            double upperDays = _settings.Get(UpperClassWealthDaysSettingId);
            int monthsBeforeDrop = (int)_settings.Get(MonthsBelowBeforeDropSettingId);
            double adultAgeYears = _settings.Get(PeopleModule.AdultAgeYearsSettingId);

            foreach (KeyValuePair<EntityId, PersonWealthState> row in _wealth.Rows().ToList())
            {
                EntityId id = row.Key;
                PersonWealthState state = row.Value;

                SocialClass qualifyingTier = ComputeQualifyingTier(id, state, laborerWage, middleDays, upperDays, adultAgeYears);

                SocialClass oldClass = state.Class;
                if (qualifyingTier > oldClass)
                {
                    state.Class = qualifyingTier;
                    state.MonthsBelowClass = 0;
                }
                else if (qualifyingTier < oldClass)
                {
                    state.MonthsBelowClass++;
                    if (state.MonthsBelowClass >= monthsBeforeDrop)
                    {
                        state.Class = qualifyingTier;
                        state.MonthsBelowClass = 0;
                    }
                }
                else
                {
                    state.MonthsBelowClass = 0;
                }

                if (state.Class != oldClass)
                {
                    _events.Post(ClassChangedEventId, tick, new EventData()
                        .SetEntity("person", id)
                        .SetString("oldClass", oldClass.ToString())
                        .SetString("newClass", state.Class.ToString()));
                }
            }

            _crownAccount.Previous = _crownAccount.Current;
            _crownAccount.Current = new MonthlyLedger();

            foreach (PersonWealthState state in _wealth.Rows().Select(r => r.Value))
            {
                state.Previous = state.Current;
                state.Current = new MonthlyLedger();
            }
        }

        private SocialClass ComputeQualifyingTier(EntityId id, PersonWealthState state, Money laborerWage, double middleDays, double upperDays, double adultAgeYears)
        {
            bool isChild = _people.TryGetPerson(id, out PersonState person) && person.AgeLifeYears < adultAgeYears;
            if (isChild && state.ParentId.HasValue && _wealth.TryGet(state.ParentId.Value, out PersonWealthState parentState))
            {
                return parentState.Class;
            }

            SocialClass wealthTier = WealthTier(GetWealth(id), laborerWage, middleDays, upperDays);
            SocialClass jobTier = _jobTierProvider(id);
            SocialClass tier = wealthTier > jobTier ? wealthTier : jobTier;

            SocialClass? powerTier = _powerProvider(id);
            if (powerTier.HasValue && powerTier.Value > tier)
            {
                tier = powerTier.Value;
            }

            return tier;
        }

        private static SocialClass WealthTier(Money wealth, Money laborerWage, double middleDays, double upperDays)
        {
            if (laborerWage.HundredthsOfShil <= 0)
            {
                return SocialClass.Lower;
            }

            double days = (double)wealth.HundredthsOfShil / laborerWage.HundredthsOfShil;
            if (days >= upperDays)
            {
                return SocialClass.Upper;
            }

            return days >= middleDays ? SocialClass.Middle : SocialClass.Lower;
        }

        #endregion

        #region Accounts

        private PersonWealthState EnsureAccount(EntityId id)
        {
            if (!_wealth.TryGet(id, out PersonWealthState state))
            {
                state = new PersonWealthState { Ambition = RollAmbition() };
                _wealth.Add(id, state);
            }

            return state;
        }

        private double RollAmbition()
        {
            return _random.Chance((float)_settings.Get(ContentToStayLowerChanceSettingId)) ? 0.0 : _random.NextFloat();
        }

        #endregion

        #region Console commands

        private void RegisterCommands(ModuleContext context)
        {
            context.RegisterCommand(new ConsoleCommand("wealth", "wealth <entityId> - shows an account's balance, wealth, class and ambition.", args =>
            {
                if (args.Length != 1 || !EntityId.TryParse(args[0], out EntityId id))
                {
                    return "Usage: wealth <entityId> (e.g. e12.3)";
                }

                return string.Join("\n", Inspect(id));
            }));

            context.RegisterCommand(new ConsoleCommand("crown", "crown - shows the treasury and this month's ledger.", args =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Treasury: {_crownAccount.Balance}");
                AppendLedger(sb, _crownAccount.Current);
                return sb.ToString().TrimEnd();
            }));

            context.RegisterCommand(new ConsoleCommand("givemoney", "givemoney <entityId|crown> <shil> - grants money, for testing.", args =>
            {
                if (args.Length != 2 || !ConsoleArgs.TryParseDouble(args[1], out double shil) || shil <= 0)
                {
                    return "Usage: givemoney <entityId|crown> <shil>";
                }

                AccountId target;
                if (string.Equals(args[0], "crown", StringComparison.OrdinalIgnoreCase))
                {
                    target = AccountId.Crown;
                }
                else if (EntityId.TryParse(args[0], out EntityId id))
                {
                    target = AccountId.Person(id);
                }
                else
                {
                    return "Usage: givemoney <entityId|crown> <shil>";
                }

                Grant(target, Money.FromShil(shil));
                return $"Granted {Money.FromShil(shil)} to {(target.IsCrown ? "crown" : target.Entity.ToString())}.";
            }));
        }

        private static void AppendLedger(System.Text.StringBuilder sb, MonthlyLedger ledger)
        {
            foreach (TransferReason reason in AllReasons)
            {
                Money inAmount = ledger.In.TryGetValue(reason, out Money i) ? i : Money.Zero;
                Money outAmount = ledger.Out.TryGetValue(reason, out Money o) ? o : Money.Zero;
                if (inAmount != Money.Zero || outAmount != Money.Zero)
                {
                    sb.AppendLine($"  {reason}: in {inAmount}, out {outAmount}");
                }
            }
        }

        #endregion

        #region Saves

        public void Write(SaveWriter writer)
        {
            writer.WriteLong(_crownAccount.Balance.HundredthsOfShil);
            WriteLedger(writer, _crownAccount.Current);
            WriteLedger(writer, _crownAccount.Previous);

            writer.WriteInt(_wealth.Count);
            foreach (KeyValuePair<EntityId, PersonWealthState> row in _wealth.Rows())
            {
                writer.WriteEntityId(row.Key);
                PersonWealthState state = row.Value;

                writer.WriteLong(state.Balance.HundredthsOfShil);
                WriteLedger(writer, state.Current);
                WriteLedger(writer, state.Previous);
                writer.WriteInt((int)state.Class);
                writer.WriteInt(state.MonthsBelowClass);
                writer.WriteDouble(state.Ambition);
                writer.WriteBool(state.ParentId.HasValue);
                if (state.ParentId.HasValue)
                {
                    writer.WriteEntityId(state.ParentId.Value);
                }
            }
        }

        public void Read(SaveReader reader, int savedVersion)
        {
            _crownAccount.Balance = Money.FromHundredthsOfShil(reader.ReadLong());
            _crownAccount.Current = ReadLedger(reader);
            _crownAccount.Previous = ReadLedger(reader);

            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                EntityId id = reader.ReadEntityId();
                var state = new PersonWealthState
                {
                    Balance = Money.FromHundredthsOfShil(reader.ReadLong()),
                    Current = ReadLedger(reader),
                    Previous = ReadLedger(reader),
                    Class = (SocialClass)reader.ReadInt(),
                    MonthsBelowClass = reader.ReadInt(),
                    Ambition = reader.ReadDouble()
                };

                if (reader.ReadBool())
                {
                    state.ParentId = reader.ReadEntityId();
                }

                _wealth.Add(id, state);
            }
        }

        private static void WriteLedger(SaveWriter writer, MonthlyLedger ledger)
        {
            foreach (TransferReason reason in AllReasons)
            {
                writer.WriteLong(ledger.In.TryGetValue(reason, out Money i) ? i.HundredthsOfShil : 0);
                writer.WriteLong(ledger.Out.TryGetValue(reason, out Money o) ? o.HundredthsOfShil : 0);
            }
        }

        private static MonthlyLedger ReadLedger(SaveReader reader)
        {
            var ledger = new MonthlyLedger();
            foreach (TransferReason reason in AllReasons)
            {
                long inHundredths = reader.ReadLong();
                long outHundredths = reader.ReadLong();
                if (inHundredths != 0)
                {
                    ledger.In[reason] = Money.FromHundredthsOfShil(inHundredths);
                }

                if (outHundredths != 0)
                {
                    ledger.Out[reason] = Money.FromHundredthsOfShil(outHundredths);
                }
            }

            return ledger;
        }

        #endregion

        #region Inspector

        public IEnumerable<string> Inspect(EntityId entity)
        {
            if (!_wealth.TryGet(entity, out PersonWealthState state))
            {
                yield break;
            }

            yield return $"Balance: {state.Balance}, wealth: {GetWealth(entity)}, class: {state.Class}";
            yield return $"Ambition: {state.Ambition:F2}, months below class: {state.MonthsBelowClass}";
        }

        #endregion
    }
}
