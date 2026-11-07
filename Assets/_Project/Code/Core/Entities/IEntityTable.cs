namespace TheEmptying.Core.Entities
{
    public interface IEntityTable
    {
        bool Has(EntityId id);

        /// <summary>No-op if the table has no row for id.</summary>
        void Remove(EntityId id);
    }
}
