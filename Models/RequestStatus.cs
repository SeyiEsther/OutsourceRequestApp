namespace OutsourceRequestApp.Models
{
    /// <summary>
    /// Central definition of all request status values used throughout the workflow.
    /// Use these constants instead of raw strings to prevent typos and make
    /// refactoring safe.
    ///
    /// Approval chain: Submitted (John Fisher / Work Prep) -&gt; ProductionPending (Lukasz Jaworski)
    /// -&gt; CostCompactPending (Chris Welland / Strategic Buyer) -&gt; SourcingPending (Simon Graham)
    /// -&gt; MdPending (Patrick MacDonough) -&gt; Approved.
    /// </summary>
    public static class RequestStatus
    {
        public const string Submitted          = "Submitted";
        public const string ProductionPending   = "Production_Pending";
        public const string CostCompactPending  = "CostCompact_Pending";
        public const string SourcingPending     = "Sourcing_Pending";
        public const string MdPending           = "MD_Pending";
        public const string Approved            = "Approved";
        public const string Rejected            = "Rejected";
        public const string Cancelled           = "Cancelled";

        /// <summary>Human-friendly display label for a status value.</summary>
        public static string Label(string? status) => status switch
        {
            ProductionPending  => "Production Review",
            CostCompactPending => "Cost Compact Review",
            SourcingPending    => "Sourcing Review",
            MdPending          => "MD Review",
            _                  => status ?? ""
        };

        /// <summary>CSS badge class for the shared badge component.</summary>
        public static string BadgeClass(string? status) => status switch
        {
            Approved                                                               => "badge-approved",
            Rejected or Cancelled                                                  => "badge-rejected",
            ProductionPending or CostCompactPending or SourcingPending or MdPending => "badge-review",
            _                                                                       => "badge-submitted"
        };

        /// <summary>JS/data-status filter key used in the Index table.</summary>
        public static string FilterKey(string? status) => status switch
        {
            Approved                                                               => "approved",
            Rejected or Cancelled                                                  => "rejected",
            ProductionPending or CostCompactPending or SourcingPending or MdPending => "review",
            _                                                                       => "submitted"
        };

        /// <summary>Returns true if the request is still awaiting any action.</summary>
        public static bool IsPending(string? status) =>
            status is Submitted or ProductionPending or CostCompactPending or SourcingPending or MdPending;
    }
}
