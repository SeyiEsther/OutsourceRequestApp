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

        /// <summary>RAG badge class (g/a/r/u) for the shared .rag badge component.</summary>
        public static string RagClass(string? status) => status switch
        {
            Approved                                                               => "g",
            Rejected or Cancelled                                                  => "r",
            ProductionPending or CostCompactPending or SourcingPending or MdPending => "a",
            _                                                                       => "u"
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

        /// <summary>
        /// The status a request sits in while awaiting the given approver role
        /// (WP/PROD/BUYER/SOURCING/MD). Single source of truth for this mapping —
        /// previously duplicated inline in four different places (MyApprovals,
        /// HomeController, NavContextViewComponent, ReminderService), which made
        /// it easy for one copy to drift from the others.
        /// </summary>
        public static string? PendingStatusForRole(string? roleKey) => roleKey switch
        {
            "WP"       => Submitted,
            "PROD"     => ProductionPending,
            "BUYER"    => CostCompactPending,
            "SOURCING" => SourcingPending,
            "MD"       => MdPending,
            _          => null
        };

        /// <summary>The inverse of <see cref="PendingStatusForRole"/> — which role's
        /// action a pending status is currently waiting on.</summary>
        public static string? RoleKeyForPendingStatus(string? status) => status switch
        {
            Submitted          => "WP",
            ProductionPending  => "PROD",
            CostCompactPending => "BUYER",
            SourcingPending    => "SOURCING",
            MdPending          => "MD",
            _                  => null
        };

        /// <summary>Human-friendly job title for an approver role key.</summary>
        public static string RoleLabel(string? roleKey) => roleKey switch
        {
            "WP"       => "Work Preparation Manager",
            "PROD"     => "Production Manager",
            "BUYER"    => "Strategic Buyer",
            "SOURCING" => "Sourcing & Procurement",
            "MD"       => "Managing Director",
            _          => ""
        };
    }
}
