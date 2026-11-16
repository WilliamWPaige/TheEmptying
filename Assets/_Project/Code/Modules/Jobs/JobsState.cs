namespace TheEmptying.Modules.Jobs
{
    /// <summary>One building's job slot groups. A group is null when its building type defines no slots of that kind.</summary>
    public sealed class JobsState
    {
        /// <summary>Temporary, while the building is a construction site; null once it's finished.</summary>
        public JobSlotGroup Builder;

        public JobSlotGroup Worker;
        public JobSlotGroup Manager;
        public JobSlotGroup Trainer;
    }
}
