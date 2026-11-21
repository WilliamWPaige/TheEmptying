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
        ForagingFood,

        /// <summary>Hungry with no bread on hand: walking to the nearest seller to buy more, instead of self-foraging straight away.</summary>
        Shopping,

        /// <summary>Off-shift free time: walking to, or standing near, another free person - both restore social the whole time they're together.</summary>
        Socializing,

        /// <summary>Off-shift free time: walking somewhere nearby (and, once there, somewhere nearby again), restoring recreation.</summary>
        Strolling,

        /// <summary>Off-shift free time: resting at home outside night hours - the voluntary daytime counterpart to Sleeping.</summary>
        RestingAtHome
    }
}
