namespace OutsourceRequestApp.Services
{
    /// <summary>
    /// Fuzzy person-name matcher ported from the sibling TL Portal (live and
    /// proven there). Used as a fallback when matching the signed-in user
    /// against an admin-typed name — e.g. the "Full name" an admin entered for
    /// an approver — so a nickname, a middle initial, or a minor formatting
    /// difference doesn't lock someone out of a role they're clearly assigned to.
    /// </summary>
    public static class PortalNameMatcher
    {
        public static bool Matches(string? configured, string? actual)
        {
            var a = Normalize(configured);
            var b = Normalize(actual);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            if (FullNamesEquivalent(a, b))
                return true;

            if (!b.Contains(' ') && a.Contains(' '))
                return FirstNamesCompatible(b, FirstNameOf(a));

            return false;
        }

        public static string Normalize(string? value) =>
            string.Join(' ', (value ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        static bool FullNamesEquivalent(string configured, string actual)
        {
            if (!configured.Contains(' ') || !actual.Contains(' '))
                return false;

            if (!string.Equals(LastNameOf(configured), LastNameOf(actual), StringComparison.OrdinalIgnoreCase))
                return false;

            return FirstNamesCompatible(FirstNameOf(configured), FirstNameOf(actual));
        }

        static bool FirstNamesCompatible(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            if (NicknamesEquivalent(a, b))
                return true;

            var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
            return shorter.Length >= 3 &&
                   longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase);
        }

        // Common nickname ↔ formal-name pairs. AD often stores the formal name
        // ("Michael") while an admin types the everyday name ("Mike"), which
        // plain prefix matching can't bridge (Mich… vs Mike).
        static readonly Dictionary<string, string> NicknameCanonical =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["mike"] = "michael", ["mick"] = "michael", ["micky"] = "michael", ["mickey"] = "michael",
                ["micheal"] = "michael", // very common letter-swap misspelling
                ["nick"] = "nicholas", ["nicky"] = "nicholas", ["nik"] = "nicholas",
                ["tony"] = "anthony",
                ["jim"] = "james", ["jimmy"] = "james", ["jamie"] = "james",
                ["steve"] = "steven", ["stevie"] = "steven",
                ["bob"] = "robert", ["rob"] = "robert", ["bobby"] = "robert", ["robbie"] = "robert",
                ["bill"] = "william", ["will"] = "william", ["billy"] = "william", ["willy"] = "william",
                ["dick"] = "richard", ["rich"] = "richard", ["rick"] = "richard", ["richie"] = "richard",
                ["dave"] = "david",
                ["dan"] = "daniel", ["danny"] = "daniel",
                ["tom"] = "thomas", ["tommy"] = "thomas",
                ["chris"] = "christopher",
                ["matt"] = "matthew",
                ["andy"] = "andrew", ["drew"] = "andrew",
                ["ben"] = "benjamin",
                ["sam"] = "samuel",
                ["joe"] = "joseph", ["joey"] = "joseph",
                ["ed"] = "edward", ["eddie"] = "edward", ["ted"] = "edward",
                ["ken"] = "kenneth", ["kenny"] = "kenneth",
                ["greg"] = "gregory",
                ["pat"] = "patrick", ["paddy"] = "patrick",
                ["tim"] = "timothy", ["timmy"] = "timothy",
                ["ron"] = "ronald", ["ronnie"] = "ronald",
                ["don"] = "donald", ["donnie"] = "donald",
                ["fred"] = "frederick", ["freddie"] = "frederick",
                ["charlie"] = "charles", ["chuck"] = "charles",
                ["jon"] = "jonathan", ["johnny"] = "john", ["jack"] = "john",
                ["alex"] = "alexander", ["sandy"] = "alexander",
                ["phil"] = "philip", ["philip"] = "phillip",
                ["nate"] = "nathan",
                ["gabe"] = "gabriel",
                ["vic"] = "victor",
                ["les"] = "leslie",
                ["si"] = "simon",
            };

        static bool NicknamesEquivalent(string a, string b)
        {
            var ca = NicknameCanonical.GetValueOrDefault(a, a);
            var cb = NicknameCanonical.GetValueOrDefault(b, b);
            return string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase);
        }

        static string FirstNameOf(string fullName)
        {
            var i = fullName.LastIndexOf(' ');
            return i <= 0 ? fullName : fullName[..i];
        }

        static string LastNameOf(string fullName)
        {
            var i = fullName.LastIndexOf(' ');
            return i < 0 ? fullName : fullName[(i + 1)..];
        }
    }
}
