using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AUA.Helpers
{
    /// <summary>
    /// One place views/controllers check real sign-in state -- replaces
    /// SimulatedAuthHelper now that real "Sign in with Google/Facebook"
    /// (ExternalAuthController) is wired up. Two questions this answers,
    /// deliberately kept separate:
    ///   1. Is this visitor signed in at all (IsAuthenticated / GetEmail)?
    ///      True the moment Google/Facebook hands back a verified email,
    ///      regardless of AUA membership status.
    ///   2. Do they have an active AUA membership (IsAuaMember)? This is
    ///      NOT implied by #1 -- it's only true once
    ///      ExternalAuthController's callback has looked the verified email
    ///      up against AUA's membership webservice (IAuaMemberLookupService)
    ///      and stamped the "aua_is_member" claim on the sign-in.
    /// Member-only gating (Task 4) should check IsAuaMember, not
    /// IsAuthenticated -- someone can have a perfectly valid Google login
    /// and still not be an AUA member.
    /// </summary>
    public static class MemberAuthHelper
    {
        public const string IsMemberClaimType = "aua_is_member";
        public const string CustomerIdClaimType = "aua_customer_id";

        public static bool IsAuthenticated(HttpContext context) =>
            context.User?.Identity?.IsAuthenticated == true;

        public static string GetEmail(HttpContext context) =>
            context.User?.FindFirstValue(ClaimTypes.Email);

        public static bool IsAuaMember(HttpContext context) =>
            context.User?.Claims?.Any(c => c.Type == IsMemberClaimType && c.Value == "true") == true;

        public static string GetCustomerId(HttpContext context) =>
            context.User?.FindFirstValue(CustomerIdClaimType);
    }
}
