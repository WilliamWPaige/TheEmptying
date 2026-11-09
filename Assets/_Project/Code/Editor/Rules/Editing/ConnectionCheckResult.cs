namespace TheEmptying.Editor.Rules.Editing
{
    /// <summary>Whether a candidate wire is allowed, and why not if it isn't.</summary>
    public readonly struct ConnectionCheckResult
    {
        public bool IsAllowed { get; }
        public string Reason { get; }

        public ConnectionCheckResult(bool isAllowed, string reason)
        {
            IsAllowed = isAllowed;
            Reason = reason;
        }

        public static ConnectionCheckResult Allow() => new ConnectionCheckResult(true, null);
        public static ConnectionCheckResult Reject(string reason) => new ConnectionCheckResult(false, reason);
    }
}
