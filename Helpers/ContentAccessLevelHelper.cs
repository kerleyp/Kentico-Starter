using Microsoft.AspNetCore.Http;

namespace AUA.Helpers
{
    /// <summary>
    /// The 3 gating levels for the AccessLevel field on AUA.MemberGatingFields
    /// (replaces the old plain MembersOnly boolean). Ordered from least to
    /// most restrictive on purpose -- MembersOnly is a strict superset of
    /// RequiresLogin, not a separate, unrelated check: a confirmed AUA
    /// member is by definition signed in, so anything gated at
    /// RequiresLogin is automatically visible to members too. See
    /// VisitorMeetsAccessLevel below for where that's enforced.
    /// </summary>
    public enum ContentAccessLevel
    {
        Public = 0,
        RequiresLogin = 1,
        MembersOnly = 2
    }

    public static class ContentAccessLevelHelper
    {
        // Must match the Value strings configured on the AccessLevel
        // field's Dropdown selector in the admin (see ACCESS_LEVEL_SETUP.md)
        // exactly, including case.
        public const string PublicValue = "Public";
        public const string RequiresLoginValue = "RequiresLogin";
        public const string MembersOnlyValue = "MembersOnly";

        /// <summary>
        /// Unrecognized/blank values (including every page created before
        /// this field existed) fall back to Public -- the safe default, so
        /// nothing gets accidentally locked out by a missing value.
        /// </summary>
        public static ContentAccessLevel Parse(string rawValue) =>
            rawValue switch
            {
                RequiresLoginValue => ContentAccessLevel.RequiresLogin,
                MembersOnlyValue => ContentAccessLevel.MembersOnly,
                _ => ContentAccessLevel.Public
            };

        /// <summary>
        /// True if the current visitor's sign-in/membership state satisfies
        /// the given access level. Deliberately does NOT special-case
        /// MembersOnly-implies-RequiresLogin here with an OR -- it doesn't
        /// need to: MemberAuthHelper.IsAuthenticated(context) is already
        /// true for any signed-in AUA member (membership is stamped as
        /// extra claims on top of a real sign-in, not instead of one), so
        /// the RequiresLogin case below is satisfied by members
        /// automatically.
        /// </summary>
        public static bool VisitorMeetsAccessLevel(ContentAccessLevel level, HttpContext context) =>
            level switch
            {
                ContentAccessLevel.Public => true,
                ContentAccessLevel.RequiresLogin => MemberAuthHelper.IsAuthenticated(context),
                ContentAccessLevel.MembersOnly => MemberAuthHelper.IsAuaMember(context),
                _ => true
            };
    }
}
