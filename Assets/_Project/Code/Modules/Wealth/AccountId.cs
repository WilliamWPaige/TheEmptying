using System;
using TheEmptying.Core.Entities;

namespace TheEmptying.Modules.Wealth
{
    /// <summary>A money account: either the crown's treasury or a person's (businesses join later).</summary>
    public readonly struct AccountId : IEquatable<AccountId>
    {
        public static readonly AccountId Crown = new AccountId(true, default);

        public bool IsCrown { get; }
        public EntityId Entity { get; }

        private AccountId(bool isCrown, EntityId entity)
        {
            IsCrown = isCrown;
            Entity = entity;
        }

        public static AccountId Person(EntityId id) => new AccountId(false, id);

        public bool Equals(AccountId other) => IsCrown == other.IsCrown && Entity.Equals(other.Entity);
        public override bool Equals(object obj) => obj is AccountId other && Equals(other);
        public override int GetHashCode() => IsCrown ? 1 : Entity.GetHashCode();
        public override string ToString() => IsCrown ? "crown" : Entity.ToString();
    }
}
