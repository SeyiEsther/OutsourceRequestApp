namespace OutsourceRequestApp.Models
{
    public static class RequestStatus
    {
        public const string Draft                = "Draft";
        public const string Submitted            = "Submitted";          // Step 2 pending (JF)
        public const string AwaitingLJApproval   = "AwaitingLJApproval"; // Step 3 pending (LJ)
        public const string AwaitingCostImpact   = "AwaitingCostImpact"; // Step 4 pending (SC enters cost)
        public const string FinancePending       = "Finance_Pending";    // Step 5 pending (SG)
        public const string MdPending            = "MD_Pending";         // Step 6 pending (MD)
        public const string Approved             = "Approved";
        public const string Rejected             = "Rejected";
        public const string Cancelled            = "Cancelled";

        public static string Label(string? status) => status switch
        {
            Draft              => "Draft",
            Submitted          => "Awaiting JF",
            AwaitingLJApproval => "Awaiting LJ",
            AwaitingCostImpact => "Cost Impact",
            FinancePending     => "Awaiting SG",
            MdPending          => "Awaiting MD",
            Approved           => "Approved",
            Rejected           => "Rejected",
            Cancelled          => "Cancelled",
            _                  => status ?? ""
        };

        public static string BadgeClass(string? status) => status switch
        {
            Approved                                    => "badge-approved",
            Rejected or Cancelled                       => "badge-rejected",
            Draft                                       => "badge-draft",
            FinancePending or MdPending
                or AwaitingLJApproval or AwaitingCostImpact => "badge-review",
            _                                           => "badge-submitted"
        };

        public static string FilterKey(string? status) => status switch
        {
            Approved           => "approved",
            Rejected           => "rejected",
            Cancelled          => "rejected",
            Draft              => "draft",
            FinancePending or MdPending
                or AwaitingLJApproval or AwaitingCostImpact => "review",
            _                  => "submitted"
        };

        public static bool IsPending(string? status) =>
            status is Submitted or AwaitingLJApproval or AwaitingCostImpact
                   or FinancePending or MdPending;
    }
}
