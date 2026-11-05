using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using AUA.Services;

using Kentico.Membership;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AUA.Controllers
{
    /// <summary>
    /// Real "Sign in with Google" / "Sign in with Facebook" -- replaces
    /// SimulatedAuthController now that this is wired up (delete
    /// SimulatedAuthController.cs and Helpers/SimulatedAuthHelper.cs, see
    /// OAUTH_SETUP.md). Uses the external-login flow already built into
    /// ASP.NET Core Identity / Kentico.Membership on top of this project's
    /// existing ApplicationUser/ApplicationRole setup
    /// (ConfigureMembershipServices in Program.cs) -- Google/Facebook only
    /// verify who the visitor is; no separate account or password gets
    /// created by the visitor themselves.
    ///
    /// Flow:
    ///   1. /external-auth/login?provider=Google -> Challenge() redirects to
    ///      Google's own sign-in page.
    ///   2. Google redirects back to /external-auth/callback with a signed
    ///      token; GetExternalLoginInfoAsync() reads the verified claims
    ///      (including email) from it.
    ///   3. If this email already has a local member row + linked login,
    ///      sign straight in. Otherwise create the local member row on the
    ///      fly (EmailConfirmed = true, since Google/Facebook already
    ///      verified it) and link the login so next time is step 3, not
    ///      this one.
    ///   4. Call IAuaMemberLookupService with the verified email and stamp
    ///      the result onto the sign-in as extra claims (aua_is_member,
    ///      aua_customer_id) -- see MemberAuthHelper for how templates read
    ///      these back out.
    /// </summary>
    public class ExternalAuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuaMemberLookupService _memberLookupService;

        public ExternalAuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IAuaMemberLookupService memberLookupService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _memberLookupService = memberLookupService;
        }

        // provider must match a scheme name registered in
        // ConfigureMembershipServices -- "Google" or "Facebook" as
        // registered there (ASP.NET Core's AddGoogle()/AddFacebook()
        // default their scheme name to that string).
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string provider, string returnUrl = "/home")
        {
            if (string.IsNullOrEmpty(provider))
            {
                return BadRequest("provider is required, e.g. ?provider=Google");
            }

            returnUrl = IsSafeReturnUrl(returnUrl) ? returnUrl : "/home";
            var redirectUrl = Url.Action(nameof(Callback), "ExternalAuth", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return Challenge(properties, provider);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Callback(string returnUrl = "/home", string remoteError = null)
        {
            returnUrl = IsSafeReturnUrl(returnUrl) ? returnUrl : "/home";

            if (!string.IsNullOrEmpty(remoteError))
            {
                return RedirectWithError(returnUrl, $"Error from external provider: {remoteError}");
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                return RedirectWithError(returnUrl, "Could not load external login information. Try signing in again.");
            }

            string email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
            {
                return RedirectWithError(returnUrl, $"{info.LoginProvider} did not return an email address for this account.");
            }

            var memberInfo = await _memberLookupService.LookupByEmailAsync(email);
            var extraClaims = BuildMembershipClaims(memberInfo);

            // Already linked from a previous sign-in -- straight through.
            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);

            if (signInResult.Succeeded)
            {
                // ExternalLoginSignInAsync signs in using claims from the
                // stored user record, not the freshly-looked-up membership
                // claims above -- re-sign-in with those attached so an
                // updated membership status takes effect this session
                // rather than only on next account creation.
                var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                if (existingUser != null)
                {
                    await _signInManager.SignInWithClaimsAsync(existingUser, isPersistent: true, extraClaims);
                }
                return LocalRedirect(returnUrl);
            }

            // No linked login yet -- find or create the local member by
            // email, then link this external login to it.
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true // Google/Facebook already verified this address.
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                    return RedirectWithError(returnUrl, $"Could not create an account for {email}: {errors}");
                }
            }

            var addLoginResult = await _userManager.AddLoginAsync(user, info);
            if (!addLoginResult.Succeeded)
            {
                var errors = string.Join("; ", addLoginResult.Errors.Select(e => e.Description));
                return RedirectWithError(returnUrl, $"Could not link {info.LoginProvider} to {email}: {errors}");
            }

            await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);
            return LocalRedirect(returnUrl);
        }

        [HttpGet]
        public async Task<IActionResult> Logout(string returnUrl = "/home")
        {
            await _signInManager.SignOutAsync();
            return LocalRedirect(IsSafeReturnUrl(returnUrl) ? returnUrl : "/home");
        }

        private static List<Claim> BuildMembershipClaims(AuaMemberInfo memberInfo)
        {
            var claims = new List<Claim>
            {
                new Claim(AUA.Helpers.MemberAuthHelper.IsMemberClaimType, memberInfo?.IsMember == true ? "true" : "false")
            };

            if (!string.IsNullOrEmpty(memberInfo?.CustomerId))
            {
                claims.Add(new Claim(AUA.Helpers.MemberAuthHelper.CustomerIdClaimType, memberInfo.CustomerId));
            }

            return claims;
        }

        private IActionResult RedirectWithError(string returnUrl, string message)
        {
            TempData["ExternalLoginError"] = message;
            return LocalRedirect(returnUrl);
        }

        private static bool IsSafeReturnUrl(string url) =>
            !string.IsNullOrEmpty(url) && url.StartsWith("/") && !url.StartsWith("//");
    }
}
