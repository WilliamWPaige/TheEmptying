namespace TheEmptying.Modules.People
{
    public enum PersonActivity
    {
        Idle,
        Sleeping,
        Eating,
        Wandering,
        GoingHome,
        Commuting,
        Working,

        /// <summary>Self-foraging: no bread available, so digging grubs on the spot instead of whatever else they'd be doing.</summary>
        ForagingFood
    }
}
