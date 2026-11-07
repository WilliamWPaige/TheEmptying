using System;

namespace TheEmptying.Core.Content
{
    /// <summary>Thrown in Development ErrorMode when content loading finds any validation problem.</summary>
    public sealed class ContentValidationException : Exception
    {
        public ContentLoadReport Report { get; }

        public ContentValidationException(string message, ContentLoadReport report) : base(message)
        {
            Report = report;
        }
    }
}
