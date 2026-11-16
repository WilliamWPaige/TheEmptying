namespace TheEmptying.Modules.Wealth
{
    /// <summary>Why money moved between two accounts, for the monthly ledger.</summary>
    public enum TransferReason
    {
        Wage,
        Rent,
        Tax,
        Purchase,
        Grant,
        Loan,
        Other
    }
}
