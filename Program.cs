using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


using AUA;
using AUA.Services;
using DancingGoat;
using DancingGoat.EmailComponents;
using DancingGoat.Helpers.Generators;
using DancingGoat.Models;


using CMS.Base;
using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Membership;
using CMS.Websites;


using Kentico.Activities.Web.Mvc;
using Kentico.Commerce.Web.Mvc;
using Kentico.Content.Web.Mvc.Routing;
using Kentico.EmailBuilder.Web.Mvc;
using Kentico.Membership;
using Kentico.OnlineMarketing.Web.Mvc;
using Kentico.PageBuilder.Web.Mvc;
using Kentico.PageBuilder.Web.Mvc.PageTemplates;
using Kentico.Xperience.Mjml;
using Kentico.Web.Mvc;


using Kentico.Xperience.Cloud;
using Kentico.Xperience.ManagementApi;


using XperienceCommunity.DomainAliases;


using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


using Samples.DancingGoat;


// Registers the Page Template for AUA.StandardContentPage. Without this, Kentico
// has no view to render the content type with, which is why every page of this
// type has 404'd regardless of channel or how the page data was created.
[assembly: RegisterPageTemplate(
    identifier: "AUA.StandardContentPage.Default",
    name: "AUA Standard Content Page - Default",
    customViewName: "~/PageTemplates/AUA/StandardContentPageDefault.cshtml",
    ContentTypeNames = new[] { "AUA.StandardContentPage" }
)]


// Registers the Page Template for AUA.LongFormDocumentPage (the ~90-page long-form
// docs batch's "everything else" type). Same fields/rendering as StandardContentPage,
// but a separate template registration is required per content type -- Kentico
// resolves the view strictly by the page's own content type, not by field shape.
[assembly: RegisterPageTemplate(
    identifier: "AUA.LongFormDocumentPage.Default",
    name: "AUA Long Form Document Page - Default",
    customViewName: "~/PageTemplates/AUA/LongFormDocumentPageDefault.cshtml",
    ContentTypeNames = new[] { "AUA.LongFormDocumentPage" }
)]


// Registers the Page Template for AUA.ClinicalGuidanceDocument. Same rationale as
// above -- swap in once ClinicalGuidanceDocument.generated.cs is available to
// confirm its field names match (expected to be identical to LongFormDocumentPage).
[assembly: RegisterPageTemplate(
    identifier: "AUA.ClinicalGuidanceDocument.Default",
    name: "AUA Clinical Guidance Document - Default",
    customViewName: "~/PageTemplates/AUA/ClinicalGuidanceDocumentDefault.cshtml",
    ContentTypeNames = new[] { "AUA.ClinicalGuidanceDocument" }
)]


// Pilot-only alternate template for AUA.ClinicalGuidanceDocument -- resolves
// content through the linked AUA.ClinicalGuidanceContent reusable item
// instead of the page's own fields. Registered as a SEPARATE identifier so
// it's selectable per-page (only the one pilot page uses it, via a targeted
// SQL update in the PILOT_ATOMIC_FILE trigger) without affecting the 45
// existing pages, which keep using .Default above. See
// pilot_atomic_content_model.md.
[assembly: RegisterPageTemplate(
    identifier: "AUA.ClinicalGuidanceDocument.AtomicPilot",
    name: "AUA Clinical Guidance Document - Atomic Pilot (Content Hub-backed)",
    customViewName: "~/PageTemplates/AUA/ClinicalGuidanceDocumentAtomicPilot.cshtml",
    ContentTypeNames = new[] { "AUA.ClinicalGuidanceDocument" }
)]


var builder = WebApplication.CreateBuilder(args);


builder.Services.AddXperienceCloudStoragePathMapping();
builder.Services.AddXperienceCloudChannelRequirements();


if (builder.Environment.IsQa() ||
    builder.Environment.IsUat() ||
    builder.Environment.IsEnvironment(CloudEnvironments.Custom) ||
    builder.Environment.IsEnvironment(CloudEnvironments.Staging) ||
    builder.Environment.IsProduction())
{
    // Optional SaaS-only service (per Kentico's own docs) -- moved inside
    // this environment check because it unconditionally requires an
    // Application Insights connection string. Called outside this block, it
    // throws InvalidOperationException at startup in any environment
    // without one configured (e.g. local Development), which it did after
    // updating to 31.8.4.
    builder.Services.AddXperienceCloudApplicationInsights(builder.Configuration);
    builder.Services.AddKenticoCloud(builder.Configuration);
    builder.Services.AddXperienceCloudSendGrid(builder.Configuration);
    builder.Services.AddXperienceCloudDataProtection(builder.Configuration);
}


builder.Services.AddKentico(features =>
{
    features.UsePageBuilder(new PageBuilderOptions
    {
        DefaultSectionIdentifier = ComponentIdentifiers.SINGLE_COLUMN_SECTION,
        RegisterDefaultSection = false,
        ContentTypeNames = new[]
        {
            LandingPage.CONTENT_TYPE_NAME,
            ContactsPage.CONTENT_TYPE_NAME,
            ArticlePage.CONTENT_TYPE_NAME,
            "AUA.StandardContentPage",
            "AUA.ClinicalGuidanceDocument",
            "AUA.LongFormDocumentPage"
        }
    });


    features.UseEmailBuilder();
    // NOTE: there is no features.UseFormBuilder() -- confirmed against
    // Kentico's own feature-registration reference, which lists exactly 9
    // Use*() methods and Form Builder isn't one of them. Forms aren't a
    // separate "builder" system like Email Builder -- they're just a widget
    // that lives inside Page Builder, so UsePageBuilder() below is all
    // that's needed. (A prior attempt added UseFormBuilder() here and it
    // failed to compile -- CS1061, no such method -- reverted.)
    features.UseWebPageRouting();
    features.UseEmailMarketing();
    features.UseEmailStatisticsLogging();
    features.UseActivityTracking();
    features.UseCommerce();
});


builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);


builder.Services.AddLocalization()
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (type, factory) => factory.Create(typeof(SharedResources));
    });


builder.Services.AddDancingGoatServices();
builder.Services.AddSingleton<IEmailActivityTrackingEvaluator, EmailActivityTrackingEvaluator>();


// XperienceCommunity.DomainAliases -- lets additional domains be assigned to a
// website channel in the admin UI (Channel -> Domain Aliases) and render that
// channel at its normal root. NOT the same thing as the custom host-to-path
// middleware below (that one maps a subdomain to a specific sub-path, e.g.
// membership.localhost -> aua.localhost/membership); this package maps an
// alias domain to the whole channel instead.
builder.Services.AddDomainAliasesExtensionServices();


ConfigureEmailBuilder(builder.Services);
ConfigureMembershipServices(builder.Services, builder.Configuration);


// Backs ExternalAuthController's post-login membership check -- looks a
// verified email up against AUA's membership webservice. See
// Services/AuaMemberLookupService.cs for the still-open TODO on the real
// by-email endpoint.
builder.Services.AddScoped<IAuaMemberLookupService, AuaMemberLookupService>();


if (builder.Environment.IsDevelopment())
{
    builder.Services.Configure<UrlResolveOptions>(options => options.UseSSL = false);


    // Management API (local development only -- do not enable in QA/UAT/staging/production).
    // Secret must match the MANAGEMENT_API_SECRET value used in the MCP server configuration.
    builder.Services.AddKenticoManagementApi(options =>
    {
        options.Secret = "JKGC6kjLT6xrtRJIaDBgBc86OKafGn77br2oxw0IOzg";
    });


}


var app = builder.Build();


app.InitKentico();


// Second one-shot trigger: creates a single test page through Kentico's own
// officially-supported IWebPageManager API instead of raw UMT objects. Used to
// verify a page created "the normal way" resolves correctly, isolating whether
// a routing problem is caused by our hand-built WebPageItem/WebPageUrlPath data
// or something else entirely. Set CREATE_TEST_PAGE=1 to run it.
if (Environment.GetEnvironmentVariable("CREATE_TEST_PAGE") == "1")
{
    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();


    var user = userInfoProvider.Get("administrator");


    // AUA website channel's numeric WebsiteChannelID (CMS_WebsiteChannel.WebsiteChannelID), not the CMS_Channel.ChannelID.
    const int auaWebsiteChannelId = 2;
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    var itemData = new ContentItemData(new Dictionary<string, object>
    {
        { "Title", "Why Urology (API test 2)" },
        { "Headline", "Why Urology (API test 2)" },
        { "MetaDescription", "Diagnostic test page created via IWebPageManager, after the page template was registered." },
        { "MetaKeywords", "test" },
        { "Body", "<p>This page was created through Kentico's own IWebPageManager API, after the AUA.StandardContentPage.Default page template was registered, to verify it picks up the template automatically.</p>" },
        { "NeedsManualContentReview", false }
    });


    var contentItemParameters = new ContentItemParameters("AUA.StandardContentPage", itemData);
    var createPageParameters = new CreateWebPageParameters(
        "Why Urology (API test 2)",
        "en",
        contentItemParameters)
    {
        UrlSlug = "why-urology-apitest2"
    };


    await webPageManager.Create(createPageParameters);


    Console.WriteLine("Test page created via IWebPageManager. Find it in Pages -> AUA Website and publish it from the admin UI, then test the URL.");
    return;
}


// Third one-shot trigger: bulk-creates pages through IWebPageManager (the same
// officially-supported API used by the CREATE_TEST_PAGE trigger above), reading
// a consolidated JSON file (one flat object per page) instead of the raw UMT
// 6-file format. Explicitly publishes each page after creation, since pages
// otherwise sit in Draft and may not be servable on the live site.
// Set IWPM_BATCH_FILE to a JSON file path (e.g. iwpm_single_test.json or
// iwpm_batch.json) and run `dotnet run` normally.
string iwpmBatchFilePath = Environment.GetEnvironmentVariable("IWPM_BATCH_FILE");
if (!string.IsNullOrEmpty(iwpmBatchFilePath))
{
    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    const int auaWebsiteChannelId = 2;
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    var json = await File.ReadAllTextAsync(iwpmBatchFilePath);
    var pages = System.Text.Json.JsonSerializer.Deserialize<List<BatchPageRecord>>(
        json,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;


    int created = 0, published = 0, failed = 0;
    foreach (var p in pages)
    {
        try
        {
            var fields = new Dictionary<string, object>
            {
                { "Title", p.Title },
                { "Headline", p.Headline },
                { "MetaDescription", p.MetaDescription ?? "" },
                { "MetaKeywords", p.MetaKeywords ?? "" },
                { "Body", p.Body },
                { "NeedsManualContentReview", p.NeedsManualContentReview }
            };


            if (!string.IsNullOrEmpty(p.SiteSectionTagGuid))
            {
                fields["SiteSection"] = new List<TagReference>
                {
                    new TagReference { Identifier = Guid.Parse(p.SiteSectionTagGuid) }
                };
            }


            var itemData = new ContentItemData(fields);
            var contentItemParameters = new ContentItemParameters("AUA.StandardContentPage", itemData);
            var createPageParameters = new CreateWebPageParameters(p.DisplayName, "en", contentItemParameters)
            {
                UrlSlug = p.UrlSlug
            };


            int newWebPageItemId = await webPageManager.Create(createPageParameters);
            created++;


            try
            {
                await webPageManager.TryPublish(newWebPageItemId, "en");
                published++;
            }
            catch (Exception publishEx)
            {
                Console.WriteLine($"Created but NOT published: {p.ContentItemName} -- {publishEx.Message}");
            }


            Console.WriteLine($"Created+Published: {p.ContentItemName} -> /{p.UrlSlug}");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"FAILED: {p.ContentItemName} -- {ex.Message}");
        }
    }


    Console.WriteLine($"Done. Created={created} Published={published} Failed={failed} Total={pages.Count}");
    return;
}


// Fourth one-shot trigger: rebuilds a real nested folder hierarchy in the
// content tree, then moves the existing flat-root pages under their correct
// parent folder. IMPORTANT: unlike the original version of this trigger,
// folders are NOT created via IWebPageManager.CreateFolder() anymore --
// CreateFolder() produces a WebPageItem with ContentItemContentTypeID =
// NULL, and folders with no content type can never be "Include in routing"
// eligible, so they can never contribute a URL segment to their descendants
// (confirmed empirically via the About Us pilot). The working fix is to
// make every folder position a REAL AUA.StandardContentPage page.
//
// SECOND discovery (from the first full-scale run of this trigger): most of
// the 127 intended folder slugs turned out to already belong to a REAL
// content page from the original 614-page batch -- e.g. "membership" and
// "advocacy" are themselves real section-landing pages, not placeholders we
// need to invent. Kentico enforces URL-slug uniqueness channel-wide (not
// per-parent), so trying to Create() a new placeholder with a slug some
// real page already owns throws "collision with existing one". The fix:
// for every folder position, first check (via direct SQL, see below on why)
// whether a real page already sits at that flat root slug; if so, REUSE it
// (just Move() it into place) instead of creating a duplicate. Only create
// a brand-new placeholder page when no real page already owns that slug.
//
// THIRD discovery: IContentQueryExecutor / PathMatch.Single lookups proved
// unreliable mid-run -- many pages that were still genuinely sitting at
// their original flat root position were nonetheless reported "not found",
// most likely due to some caching/indexing layer not being invalidated
// immediately after Move() calls earlier in the same run. Direct ad-hoc SQL
// via CMS.DataEngine.ConnectionHelper.ExecuteQuery (which reads the live
// CMS_WebPageItem / CMS_WebPageUrlPath tables directly, no query-layer
// caching) is used instead for all "does a page already exist at this root
// slug" lookups in this trigger, for both folders and page moves.
//
// Known data-quality wrinkles handled explicitly below (found by
// cross-referencing every folder's leaf slug against the original
// 614-page batch's slugs and each real page's own intended destination in
// RESTRUCTURE_MOVES_FILE):
//   - "about-us", "about-us/aua-overview", "about-us/leadership", and
//     "about-us/aua-governance/committees" were already correctly
//     positioned by the pilot / a prior partial run, and are no longer at
//     their original flat root slug (so the SQL lookup below can't find
//     them there) -- seeded directly by known WebPageItemID.
//   - "about-us/press-center" and "leadership-and-business/practice-
//     management-and-coding" were duplicate/legacy paths (the real content
//     actually belongs one level deeper, at "about-us/media-center/press-
//     center" and ".../institute-for-leadership-and-business/practice-
//     management-and-coding" respectively) -- removed from
//     restructure_folders.json entirely, and the one page in
//     RESTRUCTURE_MOVES_FILE that referenced each shallow path
//     (contact-us, practice-managers-network) was repointed to the correct
//     deeper path.
//   - "advocacy/get-involved/comment-letters-and-resources/urology" collides
//     with an unrelated real page (also called "urology") destined for a
//     completely different location (about-us/aua-statements) -- given an
//     alternate URL slug when created as a placeholder so the two don't
//     collide.
//
// Set RESTRUCTURE_FOLDERS_FILE and RESTRUCTURE_MOVES_FILE (both JSON) and
// run `dotnet run` normally. Folders in the file MUST be ordered shallowest
// first so parent folders exist before their children are processed.
string restructureFoldersFile = Environment.GetEnvironmentVariable("RESTRUCTURE_FOLDERS_FILE");
string restructureMovesFile = Environment.GetEnvironmentVariable("RESTRUCTURE_MOVES_FILE");
if (!string.IsNullOrEmpty(restructureFoldersFile) && !string.IsNullOrEmpty(restructureMovesFile))
{
    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    const int auaWebsiteChannelId = 2;
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    // Looks up a page with the given LEAF slug ANYWHERE in the tree
    // (matches the exact segment, at any depth) via direct SQL against the
    // live tables -- bypasses IContentQueryExecutor's apparent caching lag.
    //
    // PORTABILITY FIX: this used to only match pages still sitting at ROOT
    // (WebPageItemParentID IS NULL), paired with a hardcoded
    // folderIdsByPath dictionary of specific WebPageItemIDs pre-seeded from
    // this project's own migration history, to skip re-processing folders
    // that had already been moved out of root on a prior partial run. That
    // hardcoded dictionary only means anything on THIS box's already-
    // migrated database -- on a genuinely fresh database those IDs don't
    // exist, so the dictionary has been removed and this lookup now matches
    // the leaf slug regardless of tree depth instead (same pattern already
    // proven elsewhere in this project, e.g. TopicsController's URL
    // resolution), so re-running this trigger correctly finds and reuses a
    // folder page whether it's still at root or already moved, with no
    // hardcoded knowledge required.
    int FindRootPageIdBySlug(string slug)
    {
        var parameters = new QueryDataParameters();
        parameters.Add("@slug", slug);
        parameters.Add("@slugSuffix", "%/" + slug);
        parameters.Add("@channelId", auaWebsiteChannelId);


        var result = ConnectionHelper.ExecuteQuery(
            @"SELECT TOP 1 wpi.WebPageItemID
              FROM CMS_WebPageUrlPath up
              JOIN CMS_WebPageItem wpi ON wpi.WebPageItemID = up.WebPageUrlPathWebPageItemID
              WHERE up.WebPageUrlPathIsLatest = 1
                AND wpi.WebPageItemWebsiteChannelID = @channelId
                AND (up.WebPageUrlPath = @slug OR up.WebPageUrlPath LIKE @slugSuffix)",
            parameters,
            QueryTypeEnum.SQLQuery);


        if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
        {
            return Convert.ToInt32(result.Tables[0].Rows[0]["WebPageItemID"]);
        }


        return 0;
    }


    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };


    var foldersJson = await File.ReadAllTextAsync(restructureFoldersFile);
    var folders = System.Text.Json.JsonSerializer.Deserialize<List<FolderRecord>>(foldersJson, jsonOpts)!;


    var movesJson = await File.ReadAllTextAsync(restructureMovesFile);
    var moves = System.Text.Json.JsonSerializer.Deserialize<List<PageMoveRecord>>(movesJson, jsonOpts)!;


    // Alternate URL slugs for the one known real slug collision (see
    // comment block above).
    var slugOverrides = new Dictionary<string, string>
    {
        ["advocacy/get-involved/comment-letters-and-resources/urology"] = "urology-comment-letters"
    };


    // Filled in as folders are created/reused below -- starts empty on
    // purpose (see the FindRootPageIdBySlug comment above for why the old
    // hardcoded pre-seed was removed).
    var folderIdsByPath = new Dictionary<string, int>();


    int foldersCreated = 0, foldersReused = 0, foldersFailed = 0, foldersPreSeeded = 0;


    foreach (var f in folders)
    {
        if (folderIdsByPath.ContainsKey(f.Path))
        {
            foldersPreSeeded++;
            Console.WriteLine($"Already positioned, skipping: /{f.Path} (id={folderIdsByPath[f.Path]})");
            continue;
        }


        try
        {
            string naturalLeaf = f.Path.Contains('/') ? f.Path[(f.Path.LastIndexOf('/') + 1)..] : f.Path;
            int folderId;
            int existingId = FindRootPageIdBySlug(naturalLeaf);


            if (existingId != 0)
            {
                folderId = existingId;
                foldersReused++;
                Console.WriteLine($"Reusing existing real page as folder: /{f.Path} (id={folderId})");
            }
            else
            {
                string urlSlug = slugOverrides.TryGetValue(f.Path, out var overrideSlug) ? overrideSlug : naturalLeaf;


                var folderFields = new Dictionary<string, object>
                {
                    { "Title", f.DisplayName },
                    { "Headline", f.DisplayName },
                    { "MetaDescription", $"Section index: {f.DisplayName}" },
                    { "MetaKeywords", "" },
                    { "Body", $"<p>Section placeholder page -- serves as a routable parent folder for pages under {f.DisplayName}.</p>" },
                    { "NeedsManualContentReview", false }
                };


                var folderItemData = new ContentItemData(folderFields);
                var folderContentItemParameters = new ContentItemParameters("AUA.StandardContentPage", folderItemData);
                var createFolderPageParameters = new CreateWebPageParameters(f.DisplayName, "en", folderContentItemParameters)
                {
                    UrlSlug = urlSlug
                };


                folderId = await webPageManager.Create(createFolderPageParameters);
                await webPageManager.TryPublish(folderId, "en");
                foldersCreated++;
                Console.WriteLine($"Folder page created: /{f.Path} (id={folderId}, slug={urlSlug})");
            }


            if (!string.IsNullOrEmpty(f.ParentPath) && folderIdsByPath.TryGetValue(f.ParentPath, out int parentId))
            {
                await webPageManager.Move(new MoveWebPageParameters(folderId, parentId));


                try
                {
                    await webPageManager.TryPublish(folderId, "en");
                }
                catch (Exception publishEx)
                {
                    Console.WriteLine($"Moved but republish warning for /{f.Path}: {publishEx.Message}");
                }
            }


            folderIdsByPath[f.Path] = folderId;
        }
        catch (Exception ex)
        {
            foldersFailed++;
            Console.WriteLine($"FOLDER FAILED: /{f.Path} -- {ex.Message}");
        }
    }


    Console.WriteLine($"Folders done. Created={foldersCreated} Reused={foldersReused} PreSeeded={foldersPreSeeded} Failed={foldersFailed} Total={folders.Count}");
    Console.WriteLine("IMPORTANT: re-run fix_template_config.sql now, before testing any newly-created folder page URLs directly.");


    int pagesMoved = 0, pagesFailed = 0, pagesSkippedNoParent = 0, pagesAlreadyMoved = 0;
    foreach (var m in moves)
    {
        try
        {
            if (string.IsNullOrEmpty(m.ParentPath))
            {
                continue; // already root-level, correct as-is
            }


            if (!folderIdsByPath.TryGetValue(m.ParentPath, out int parentId))
            {
                pagesSkippedNoParent++;
                Console.WriteLine($"SKIPPED (no parent folder found): {m.UrlSlug} -> /{m.ParentPath}");
                continue;
            }


            int pageId = FindRootPageIdBySlug(m.UrlSlug);
            if (pageId == 0)
            {
                // Already moved in an earlier run -- not a real failure.
                pagesAlreadyMoved++;
                Console.WriteLine($"NOT FOUND AT ROOT (likely already moved): {m.UrlSlug}");
                continue;
            }


            await webPageManager.Move(new MoveWebPageParameters(pageId, parentId));
            await webPageManager.TryPublish(pageId, "en");


            pagesMoved++;
            Console.WriteLine($"Moved + republished: {m.UrlSlug} -> /{m.ParentPath}/{m.UrlSlug}");
        }
        catch (Exception ex)
        {
            pagesFailed++;
            Console.WriteLine($"MOVE FAILED: {m.UrlSlug} -- {ex.Message}");
        }
    }


    Console.WriteLine($"Pages done. Moved={pagesMoved} AlreadyMoved={pagesAlreadyMoved} Failed={pagesFailed} SkippedNoParent={pagesSkippedNoParent} Total={moves.Count}");
    return;
}


// Fifth one-shot trigger: rewrites the Body field of already-live pages in
// place (used for the 9 IA-section index pages + the home page, whose
// bodies are full of hardcoded flat href="/slug" links to individual
// content pages -- many of which just moved to nested URLs as part of the
// restructuring trigger above, breaking those links). Takes a flat JSON
// array of { urlSlug, body } records (see BodyUpdateRecord) -- urlSlug
// identifies the page by its CURRENT root-level slug (these pages are not
// being moved, just edited in place), body is the full replacement HTML.
// Uses a direct SQL UPDATE against AUA_StandardContentPage.Body (same
// technique already proven for the page-template-config fix) rather than
// an IWebPageManager "update" call, since this project has already spent
// significant time chasing down obscure/undocumented API method names --
// direct SQL against a table we've already inspected is the reliable path.
// Set UPDATE_BODY_FILE to a JSON file path and run `dotnet run` normally.
string updateBodyFile = Environment.GetEnvironmentVariable("UPDATE_BODY_FILE");
if (!string.IsNullOrEmpty(updateBodyFile))
{
    const int auaWebsiteChannelId = 2;


    int FindRootPageIdBySlugForBodyUpdate(string slug)
    {
        var parameters = new QueryDataParameters();
        parameters.Add("@slug", slug);
        parameters.Add("@channelId", auaWebsiteChannelId);


        var result = ConnectionHelper.ExecuteQuery(
            @"SELECT TOP 1 wpi.WebPageItemID
              FROM CMS_WebPageUrlPath up
              JOIN CMS_WebPageItem wpi ON wpi.WebPageItemID = up.WebPageUrlPathWebPageItemID
              WHERE up.WebPageUrlPathIsLatest = 1
                AND wpi.WebPageItemWebsiteChannelID = @channelId
                AND wpi.WebPageItemParentID IS NULL
                AND up.WebPageUrlPath = @slug",
            parameters,
            QueryTypeEnum.SQLQuery);


        if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
        {
            return Convert.ToInt32(result.Tables[0].Rows[0]["WebPageItemID"]);
        }


        return 0;
    }


    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var updatesJson = await File.ReadAllTextAsync(updateBodyFile);
    var updates = System.Text.Json.JsonSerializer.Deserialize<List<BodyUpdateRecord>>(updatesJson, jsonOpts)!;


    int updated = 0, failed = 0;
    foreach (var u in updates)
    {
        try
        {
            int pageId = FindRootPageIdBySlugForBodyUpdate(u.UrlSlug);
            if (pageId == 0)
            {
                failed++;
                Console.WriteLine($"PAGE NOT FOUND: {u.UrlSlug}");
                continue;
            }


            var parameters = new QueryDataParameters();
            parameters.Add("@body", u.Body);
            parameters.Add("@webPageItemId", pageId);


            int rowsAffected = ConnectionHelper.ExecuteNonQuery(
                @"UPDATE scp
                  SET scp.Body = @body
                  FROM AUA_StandardContentPage scp
                  JOIN CMS_ContentItemCommonData ccd ON ccd.ContentItemCommonDataID = scp.ContentItemDataCommonDataID
                  JOIN CMS_WebPageItem wpi ON wpi.WebPageItemContentItemID = ccd.ContentItemCommonDataContentItemID
                  WHERE wpi.WebPageItemID = @webPageItemId",
                parameters,
                QueryTypeEnum.SQLQuery);


            updated++;
            Console.WriteLine($"Updated body: {u.UrlSlug} (id={pageId}, rowsAffected={rowsAffected})");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"UPDATE FAILED: {u.UrlSlug} -- {ex.Message}");
        }
    }


    Console.WriteLine($"Body updates done. Updated={updated} Failed={failed} Total={updates.Count}");
    return;
}


// Sixth one-shot trigger: recovery/manual-test utility to move a single,
// known WebPageItemID under a given parent (or back to root if
// MOVE_TO_PARENT_ID is omitted/0). Set MOVE_PAGE_ID (required) and optionally
// MOVE_TO_PARENT_ID (numeric WebPageItemID of the new parent; omit or set to
// 0 for root).
string movePageIdRaw = Environment.GetEnvironmentVariable("MOVE_PAGE_ID");
if (!string.IsNullOrEmpty(movePageIdRaw) && int.TryParse(movePageIdRaw, out int movePageId))
{
    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    const int auaWebsiteChannelId = 2;
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    string moveToParentIdRaw = Environment.GetEnvironmentVariable("MOVE_TO_PARENT_ID");
    int moveToParentId = 0;
    if (!string.IsNullOrEmpty(moveToParentIdRaw))
    {
        int.TryParse(moveToParentIdRaw, out moveToParentId);
    }


    await webPageManager.Move(new MoveWebPageParameters(movePageId, moveToParentId));
    await webPageManager.TryPublish(movePageId, "en");


    Console.WriteLine($"Moved WebPageItemID {movePageId} to parent {moveToParentId} (0 = root).");
    return;
}


// Seventh one-shot trigger: bulk-creates the ~90 long-form pages (Clinical
// Guidance documents + everything else that was excluded from the original
// 614-page batch for being too long/structurally different). Each record
// carries its own "contentType" ("ClinicalGuidance" or "LongForm", mapped
// below to the real Kentico content type name) since this batch spans two
// different content types, unlike IWPM_BATCH_FILE above which is hardcoded
// to AUA.StandardContentPage. Each record also carries a "parentPath" --
// unlike the original restructuring trigger, these pages' parents are
// mostly ALREADY nested (not sitting at flat root), so the page lookup here
// matches by full CURRENT url path, not root-only. Set LONGFORM_BATCH_FILE
// to a JSON file path and run `dotnet run` normally.
string longformBatchFile = Environment.GetEnvironmentVariable("LONGFORM_BATCH_FILE");
if (!string.IsNullOrEmpty(longformBatchFile))
{
    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    const int auaWebsiteChannelId = 2;
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    var contentTypeMap = new Dictionary<string, string>
    {
        ["ClinicalGuidance"] = "AUA.ClinicalGuidanceDocument",
        ["LongForm"] = "AUA.LongFormDocumentPage"
    };


    // Finds a page anywhere in the tree (not just root) by its CURRENT full
    // URL path, via direct SQL (see the reliability note on the
    // RESTRUCTURE_FOLDERS_FILE trigger above for why direct SQL is used
    // instead of IContentQueryExecutor).
    int FindPageIdByFullPath(string fullPath)
    {
        var parameters = new QueryDataParameters();
        parameters.Add("@path", fullPath);
        parameters.Add("@channelId", auaWebsiteChannelId);


        var result = ConnectionHelper.ExecuteQuery(
            @"SELECT TOP 1 wpi.WebPageItemID
              FROM CMS_WebPageUrlPath up
              JOIN CMS_WebPageItem wpi ON wpi.WebPageItemID = up.WebPageUrlPathWebPageItemID
              WHERE up.WebPageUrlPathIsLatest = 1
                AND wpi.WebPageItemWebsiteChannelID = @channelId
                AND up.WebPageUrlPath = @path",
            parameters,
            QueryTypeEnum.SQLQuery);


        if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
        {
            return Convert.ToInt32(result.Tables[0].Rows[0]["WebPageItemID"]);
        }


        return 0;
    }


    // Finds a page currently sitting at ROOT (no parent) with the given flat
    // URL slug -- used to make this trigger safely re-runnable: a page
    // already created (but not yet moved, e.g. because "Allowed children"
    // wasn't configured yet on the first run) is reused instead of
    // recreated/duplicated.
    int FindRootPageIdBySlug(string slug)
    {
        var parameters = new QueryDataParameters();
        parameters.Add("@slug", slug);
        parameters.Add("@channelId", auaWebsiteChannelId);


        var result = ConnectionHelper.ExecuteQuery(
            @"SELECT TOP 1 wpi.WebPageItemID
              FROM CMS_WebPageUrlPath up
              JOIN CMS_WebPageItem wpi ON wpi.WebPageItemID = up.WebPageUrlPathWebPageItemID
              WHERE up.WebPageUrlPathIsLatest = 1
                AND wpi.WebPageItemWebsiteChannelID = @channelId
                AND wpi.WebPageItemParentID IS NULL
                AND up.WebPageUrlPath = @slug",
            parameters,
            QueryTypeEnum.SQLQuery);


        if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
        {
            return Convert.ToInt32(result.Tables[0].Rows[0]["WebPageItemID"]);
        }


        return 0;
    }


    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var json = await File.ReadAllTextAsync(longformBatchFile);
    var pages = System.Text.Json.JsonSerializer.Deserialize<List<LongFormPageRecord>>(json, jsonOpts)!;


    int created = 0, reused = 0, published = 0, moved = 0, createFailed = 0, moveFailed = 0;
    foreach (var p in pages)
    {
        int pageId;


        if (!contentTypeMap.TryGetValue(p.ContentType, out var kenticoContentType))
        {
            createFailed++;
            Console.WriteLine($"FAILED: {p.UrlSlug} -- unknown contentType '{p.ContentType}'");
            continue;
        }


        // Idempotency check -- if this slug already exists at root (from a
        // prior partial run), reuse it instead of trying to Create() again
        // (which would throw a URL collision error).
        int existingId = FindRootPageIdBySlug(p.UrlSlug);
        if (existingId != 0)
        {
            pageId = existingId;
            reused++;
            Console.WriteLine($"Already created, reusing: {p.UrlSlug} (id={pageId})");
        }
        else
        {
            try
            {
                var fields = new Dictionary<string, object>
                {
                    { "Title", p.Title },
                    { "Headline", p.Headline },
                    { "MetaDescription", p.MetaDescription ?? "" },
                    { "MetaKeywords", p.MetaKeywords ?? "" },
                    { "Body", p.Body },
                    { "NeedsManualContentReview", p.NeedsManualContentReview }
                };


                if (!string.IsNullOrEmpty(p.SiteSectionTagGuid))
                {
                    fields["SiteSection"] = new List<TagReference>
                    {
                        new TagReference { Identifier = Guid.Parse(p.SiteSectionTagGuid) }
                    };
                }


                var itemData = new ContentItemData(fields);
                var contentItemParameters = new ContentItemParameters(kenticoContentType, itemData);
                var createPageParameters = new CreateWebPageParameters(p.DisplayName, "en", contentItemParameters)
                {
                    UrlSlug = p.UrlSlug
                };


                pageId = await webPageManager.Create(createPageParameters);
                created++;


                try
                {
                    await webPageManager.TryPublish(pageId, "en");
                    published++;
                }
                catch (Exception publishEx)
                {
                    Console.WriteLine($"Created but NOT published: {p.UrlSlug} -- {publishEx.Message}");
                }
            }
            catch (Exception ex)
            {
                createFailed++;
                Console.WriteLine($"CREATE FAILED: {p.UrlSlug} -- {ex.Message}");
                continue;
            }
        }


        if (!string.IsNullOrEmpty(p.ParentPath))
        {
            try
            {
                int parentId = FindPageIdByFullPath(p.ParentPath);
                if (parentId == 0)
                {
                    Console.WriteLine($"PARENT NOT FOUND (left at root): {p.UrlSlug} -> /{p.ParentPath}");
                }
                else
                {
                    await webPageManager.Move(new MoveWebPageParameters(pageId, parentId));
                    try { await webPageManager.TryPublish(pageId, "en"); }
                    catch (Exception publishEx) { Console.WriteLine($"Moved but republish warning for {p.UrlSlug}: {publishEx.Message}"); }
                    moved++;
                    Console.WriteLine($"Moved: {p.UrlSlug} -> /{p.ParentPath}/{p.UrlSlug}");
                }
            }
            catch (Exception ex)
            {
                moveFailed++;
                Console.WriteLine($"MOVE FAILED: {p.UrlSlug} -- {ex.Message}");
            }
        }
    }


    Console.WriteLine($"Done. Created={created} Reused={reused} Published={published} Moved={moved} CreateFailed={createFailed} MoveFailed={moveFailed} Total={pages.Count}");
    Console.WriteLine("IMPORTANT: re-run fix_template_config.sql now, before testing any of these new pages' URLs.");
    return;
}


// Eighth one-shot trigger: like UPDATE_BODY_FILE above, but updates ALL of a
// live page's core fields (Title/Headline/MetaDescription/MetaKeywords/Body/
// NeedsManualContentReview) in one shot, and looks the page up by its
// CURRENT FULL nested path rather than a root-only slug. Used for the 2
// long-form pages whose slugs happened to already be occupied by a
// placeholder page created during the folder-restructuring trigger
// (`board-of-directors`, `young-urologists-of-the-year-award-winners`) --
// rather than deleting and recreating those pages (which would also require
// re-parenting their existing children), their existing
// AUA.StandardContentPage row is updated in place with the real long-form
// content. Set LONGFORM_SPECIAL_UPDATE_FILE to a JSON file path (see
// LongFormPageRecord's FullPath field) and run `dotnet run` normally.
string longformSpecialUpdateFile = Environment.GetEnvironmentVariable("LONGFORM_SPECIAL_UPDATE_FILE");
if (!string.IsNullOrEmpty(longformSpecialUpdateFile))
{
    const int auaWebsiteChannelId = 2;


    int FindPageIdByFullPathForSpecialUpdate(string fullPath)
    {
        var parameters = new QueryDataParameters();
        parameters.Add("@path", fullPath);
        parameters.Add("@channelId", auaWebsiteChannelId);


        var result = ConnectionHelper.ExecuteQuery(
            @"SELECT TOP 1 wpi.WebPageItemID
              FROM CMS_WebPageUrlPath up
              JOIN CMS_WebPageItem wpi ON wpi.WebPageItemID = up.WebPageUrlPathWebPageItemID
              WHERE up.WebPageUrlPathIsLatest = 1
                AND wpi.WebPageItemWebsiteChannelID = @channelId
                AND up.WebPageUrlPath = @path",
            parameters,
            QueryTypeEnum.SQLQuery);


        if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
        {
            return Convert.ToInt32(result.Tables[0].Rows[0]["WebPageItemID"]);
        }


        return 0;
    }


    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var updatesJson = await File.ReadAllTextAsync(longformSpecialUpdateFile);
    var updates = System.Text.Json.JsonSerializer.Deserialize<List<LongFormPageRecord>>(updatesJson, jsonOpts)!;


    int updated = 0, failed = 0;
    foreach (var u in updates)
    {
        try
        {
            int pageId = FindPageIdByFullPathForSpecialUpdate(u.FullPath);
            if (pageId == 0)
            {
                failed++;
                Console.WriteLine($"PAGE NOT FOUND: {u.FullPath}");
                continue;
            }


            var parameters = new QueryDataParameters();
            parameters.Add("@title", u.Title);
            parameters.Add("@headline", u.Headline);
            parameters.Add("@metaDescription", u.MetaDescription ?? "");
            parameters.Add("@metaKeywords", u.MetaKeywords ?? "");
            parameters.Add("@body", u.Body);
            parameters.Add("@needsReview", u.NeedsManualContentReview);
            parameters.Add("@webPageItemId", pageId);


            int rowsAffected = ConnectionHelper.ExecuteNonQuery(
                @"UPDATE scp
                  SET scp.Title = @title,
                      scp.Headline = @headline,
                      scp.MetaDescription = @metaDescription,
                      scp.MetaKeywords = @metaKeywords,
                      scp.Body = @body,
                      scp.NeedsManualContentReview = @needsReview
                  FROM AUA_StandardContentPage scp
                  JOIN CMS_ContentItemCommonData ccd ON ccd.ContentItemCommonDataID = scp.ContentItemDataCommonDataID
                  JOIN CMS_WebPageItem wpi ON wpi.WebPageItemContentItemID = ccd.ContentItemCommonDataContentItemID
                  WHERE wpi.WebPageItemID = @webPageItemId",
                parameters,
                QueryTypeEnum.SQLQuery);


            updated++;
            Console.WriteLine($"Updated: {u.FullPath} (id={pageId}, rowsAffected={rowsAffected})");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"UPDATE FAILED: {u.FullPath} -- {ex.Message}");
        }
    }


    Console.WriteLine($"Special updates done. Updated={updated} Failed={failed} Total={updates.Count}");
    return;
}


// Ninth one-shot trigger: seeds the AUA.Menu / AUA.MenuItem reusable content
// items from mega_menu_seed.json, replacing the old static-generated
// _HeaderNav.cshtml mega menu with content-editable data.
//
// IMPORTANT / UNPROVEN: everything else in this project creates WEBSITE
// content (pages, via IWebPageManager) -- this is the first time this
// project creates REUSABLE (non-page) content items, which use a different
// manager API. IContentItemManager / CreateDraftAsync / ContentItemReference
// below are a best-effort guess at Xperience by Kentico's reusable-content
// creation API, modeled on the same shape as IWebPageManager (which IS
// proven throughout this project), but genuinely unverified. If this
// doesn't compile, paste the exact error -- same iterative approach used
// for every other uncertain API call in this project (e.g. WebPageFields.
// WebPageItemID, the missing usings in AskController).
string menuSeedFile = Environment.GetEnvironmentVariable("MENU_SEED_FILE");
if (!string.IsNullOrEmpty(menuSeedFile))
{
    const string menuWorkspaceName = "AUANet.org";


    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var menuUser = userInfoProvider.Get("administrator");


    var contentItemManagerFactory = app.Services.GetRequiredService<IContentItemManagerFactory>();
    var contentItemManager = contentItemManagerFactory.Create(menuUser.UserID);


    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var json = await File.ReadAllTextAsync(menuSeedFile);
    var seed = System.Text.Json.JsonSerializer.Deserialize<MenuSeedData>(json, jsonOpts)!;


    int itemsCreated = 0, itemsFailed = 0;


    // Creates one AUA.MenuItem content item (optionally with ChildItems
    // already resolved to Guids) and returns its ContentItemGUID.
    async Task<Guid?> CreateMenuItem(MenuSeedItem item, List<Guid> childGuids)
    {
        try
        {
            // Scalar fields only at Create() time. Reference fields
            // (ChildItems) confirmed NOT to reliably persist when included
            // in the initial Create() itemData -- see VERIFY diagnostic on
            // the parent Menu's "Item" field, which showed Count=0
            // immediately after creation despite no exception being thrown.
            // Writing reference fields via a follow-up TryUpdateDraft while
            // the item is still in its initial Draft state instead.
            var scalarFields = new Dictionary<string, object>
            {
                { "Label", item.Label },
                { "LinkUrl", item.LinkUrl },
                { "Order", item.Order }
            };


            if (!string.IsNullOrEmpty(item.Description))
            {
                scalarFields["Description"] = item.Description;
            }


            var createParameters = new CreateContentItemParameters(
                contentTypeName: "AUA.MenuItem",
                name: null,
                displayName: item.Label,
                languageName: "en",
                workspaceName: menuWorkspaceName);


            int contentItemId = await contentItemManager.Create(createParameters, new ContentItemData(scalarFields));


            if (childGuids.Count > 0)
            {
                var allFields = new Dictionary<string, object>(scalarFields)
                {
                    { "ChildItems", childGuids.Select(g => new ContentItemReference { Identifier = g }).ToList() }
                };


                bool updated = await contentItemManager.TryUpdateDraft(contentItemId, "en", new ContentItemData(allFields));
                if (!updated)
                {
                    Console.WriteLine($"WARNING: TryUpdateDraft returned false for MenuItem '{item.Label}' -- ChildItems may not have been attached.");
                }
            }


            await contentItemManager.TryPublish(contentItemId, "en");
            // Provider.Get(...).ContentItemGUID is Kentico's own documented
            // pattern for getting the Guid back after Create() returns an
            // int -- see the migration guide's CreateHeroContentItem example.
            Guid contentItemGuid = CMS.ContentEngine.Internal.ContentItemInfo.Provider.Get(contentItemId).ContentItemGUID;


            itemsCreated++;
            Console.WriteLine($"MenuItem created: {item.Label} ({contentItemGuid})");
            return contentItemGuid;
        }
        catch (Exception ex)
        {
            itemsFailed++;
            Console.WriteLine($"MENU ITEM FAILED: {item.Label} -- {ex.Message}");
            return null;
        }
    }


    var topLevelGuids = new List<Guid>();
    foreach (var item in seed.Items.OrderBy(i => i.Order))
    {
        var childGuids = new List<Guid>();
        foreach (var child in (item.Children ?? new List<MenuSeedItem>()).OrderBy(c => c.Order))
        {
            var childGuid = await CreateMenuItem(child, new List<Guid>());
            if (childGuid.HasValue)
            {
                childGuids.Add(childGuid.Value);
            }
        }


        var topGuid = await CreateMenuItem(item, childGuids);
        if (topGuid.HasValue)
        {
            topLevelGuids.Add(topGuid.Value);
        }
    }


    try
    {
        // Field codename confirmed as "Item" (capital I) directly in the
        // field's settings screen -- matches the generated Menu.cs property
        // name. Same fix as CreateMenuItem's ChildItems: write the "Item"
        // reference field via a follow-up TryUpdateDraft instead of
        // relying on Create()'s itemData to persist it (confirmed broken
        // via the VERIFY diagnostic below, which showed Count=0
        // immediately after Create()+TryPublish() with the field included
        // at creation time).
        var scalarMenuFields = new Dictionary<string, object>
        {
            { "Title", seed.MenuTitle }
        };


        var menuCreateParameters = new CreateContentItemParameters(
            contentTypeName: "AUA.Menu",
            name: null,
            displayName: seed.MenuTitle,
            languageName: "en",
            workspaceName: menuWorkspaceName);


        int menuItemId = await contentItemManager.Create(menuCreateParameters, new ContentItemData(scalarMenuFields));


        var allMenuFields = new Dictionary<string, object>(scalarMenuFields)
        {
            { "Item", topLevelGuids.Select(g => new ContentItemReference { Identifier = g }).ToList() }
        };


        bool menuUpdated = await contentItemManager.TryUpdateDraft(menuItemId, "en", new ContentItemData(allMenuFields));
        if (!menuUpdated)
        {
            Console.WriteLine($"WARNING: TryUpdateDraft returned false for Menu '{seed.MenuTitle}' -- Item field may not have been attached.");
        }


        await contentItemManager.TryPublish(menuItemId, "en");
        Guid menuGuid = CMS.ContentEngine.Internal.ContentItemInfo.Provider.Get(menuItemId).ContentItemGUID;


        Console.WriteLine($"Menu created: {seed.MenuTitle} ({menuGuid})");


        // DIAGNOSTIC: query the item straight back in this same process,
        // the exact same way _HeaderNav.cshtml does, so we get ground truth
        // on whether the "Item" field actually persisted -- independent of
        // the website's own separate process/caching. Remove once resolved.
        try
        {
            var verifyQueryExecutor = app.Services.GetRequiredService<IContentQueryExecutor>();
            var verifyBuilder = new ContentItemQueryBuilder()
                .ForContentType(Menu.CONTENT_TYPE_NAME, config => config
                    .Where(w => w.WhereEquals(nameof(Menu.Title), seed.MenuTitle))
                    .WithLinkedItems(2))
                .InLanguage("en");
            var verifyResult = await verifyQueryExecutor.GetMappedResult<Menu>(verifyBuilder);
            var verifyMenu = verifyResult?.FirstOrDefault();
            var verifyCount = verifyMenu?.Item?.Count() ?? -1;
            Console.WriteLine($"VERIFY: queried back '{seed.MenuTitle}' immediately -- Item.Count={verifyCount}");
        }
        catch (Exception verifyEx)
        {
            Console.WriteLine($"VERIFY QUERY FAILED: {verifyEx.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"MENU FAILED: {ex.Message}");
    }


    Console.WriteLine($"Done. ItemsCreated={itemsCreated} ItemsFailed={itemsFailed} TopLevelItems={topLevelGuids.Count}");
    return;
}


// Tenth one-shot trigger: the atomic-content-model pilot (see
// pilot_atomic_content_model.md). Fully non-destructive -- creates ONE new
// AUA.ClinicalGuidanceContent reusable item, ONE new page referencing it,
// and points that single page (only) at a separate pilot page template.
// None of the 45 existing live Clinical Guidance pages are touched.
//
// Same unproven-API caveat as MENU_SEED_FILE for the reusable-item creation
// half. The second half (setting a single page's template config via
// targeted SQL) reuses the already-proven fix_template_config.sql technique,
// just scoped to one WebPageItemID instead of all NULL rows.
string pilotAtomicFile = Environment.GetEnvironmentVariable("PILOT_ATOMIC_FILE");
if (!string.IsNullOrEmpty(pilotAtomicFile))
{
    const int auaWebsiteChannelId = 2;
    const string pilotTemplateIdentifier = "AUA.ClinicalGuidanceDocument.AtomicPilot";
    const string pilotWorkspaceName = "AUANet.org";


    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    var contentItemManagerFactory = app.Services.GetRequiredService<IContentItemManagerFactory>();
    var contentItemManager = contentItemManagerFactory.Create(user.UserID);


    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var json = await File.ReadAllTextAsync(pilotAtomicFile);
    var pilot = System.Text.Json.JsonSerializer.Deserialize<PilotAtomicContent>(json, jsonOpts)!;


    try
    {
        // Step 1: create the reusable content item -- the "real" content,
        // now living in Content Hub instead of directly on a page.
        var contentFields = new Dictionary<string, object>
        {
            { "Title", pilot.SourceTitle },
            { "Headline", pilot.SourceHeadline },
            { "MetaDescription", pilot.SourceMetaDescription ?? "" },
            { "MetaKeywords", pilot.SourceMetaKeywords ?? "" },
            { "Body", pilot.SourceBody },
            { "NeedsManualContentReview", false }
        };


        if (!string.IsNullOrEmpty(pilot.SourceSiteSectionTagGuid))
        {
            contentFields["SiteSection"] = new List<TagReference>
            {
                new TagReference { Identifier = Guid.Parse(pilot.SourceSiteSectionTagGuid) }
            };
        }


        var contentItemData = new ContentItemData(contentFields);
        var contentCreateParameters = new CreateContentItemParameters(
            contentTypeName: "AUA.ClinicalGuidanceContent",
            name: null,
            displayName: pilot.SourceTitle,
            languageName: "en",
            workspaceName: pilotWorkspaceName);


        int contentItemId = await contentItemManager.Create(contentCreateParameters, contentItemData);
        await contentItemManager.TryPublish(contentItemId, "en");
        Guid contentItemGuid = CMS.ContentEngine.Internal.ContentItemInfo.Provider.Get(contentItemId).ContentItemGUID;
        Console.WriteLine($"Reusable content item created: {pilot.SourceTitle} ({contentItemGuid})");


        // Step 2: create ONE new page (the "thin container"), referencing
        // the reusable item above via the new GuidanceContent field instead
        // of storing Title/Body directly. Minimal own Title for admin UI
        // usability.
        var pageFields = new Dictionary<string, object>
        {
            { "Title", pilot.SourceTitle },
            { "Headline", pilot.SourceHeadline },
            { "MetaDescription", "" },
            { "MetaKeywords", "" },
            { "Body", "" }, // deliberately empty -- the pilot template reads from GuidanceContent instead
            { "NeedsManualContentReview", false },
            { "GuidanceContent", new List<ContentItemReference> { new ContentItemReference { Identifier = contentItemGuid } } }
        };


        var pageItemData = new ContentItemData(pageFields);
        var pageContentItemParameters = new ContentItemParameters("AUA.ClinicalGuidanceDocument", pageItemData);
        var createPageParameters = new CreateWebPageParameters(pilot.SourceTitle, "en", pageContentItemParameters)
        {
            UrlSlug = pilot.PilotUrlSlug
        };


        int pageId = await webPageManager.Create(createPageParameters);
        await webPageManager.TryPublish(pageId, "en");
        Console.WriteLine($"Pilot page created: /{pilot.PilotUrlSlug} (id={pageId})");


        // Step 3: point just this one page at the pilot template, via
        // direct SQL targeted at its own WebPageItemID -- same technique as
        // fix_template_config.sql, just scoped to one row.
        var templateParams = new QueryDataParameters();
        templateParams.Add("@config", $"{{\"identifier\":\"{pilotTemplateIdentifier}\"}}");
        templateParams.Add("@webPageItemId", pageId);


        int rowsAffected = ConnectionHelper.ExecuteNonQuery(
            @"UPDATE ccd
              SET ccd.ContentItemCommonDataVisualBuilderTemplateConfiguration = @config
              FROM CMS_ContentItemCommonData ccd
              JOIN CMS_WebPageItem wpi ON wpi.WebPageItemContentItemID = ccd.ContentItemCommonDataContentItemID
              WHERE wpi.WebPageItemID = @webPageItemId",
            templateParams,
            QueryTypeEnum.SQLQuery);


        Console.WriteLine($"Pilot template config set (rowsAffected={rowsAffected}). Visit /{pilot.PilotUrlSlug} to verify, and check Content Hub for the new AUA.ClinicalGuidanceContent item.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"PILOT FAILED: {ex.Message}");
    }


    return;
}


// Eleventh one-shot trigger: migrates ALL existing live Clinical Guidance
// pages (not the one pilot test page) to the atomic content model -- i.e.
// creates a matching AUA.ClinicalGuidanceContent reusable item for each one
// and points that page's GuidanceContent field at it.
//
// DELIBERATELY NON-VISUAL: every page stays on the existing
// AUA.ClinicalGuidanceDocument.Default template, and its own Title/Body/etc.
// fields are written back unchanged -- nothing changes for site visitors.
// This just gets the content into Content Hub and linked, per Kentico's
// hybrid model (pages can store data directly AND reference reusable
// content at the same time). Switching templates to actually render from
// Content Hub is a separate, later decision -- not part of this pass.
//
// KNOWN LIMITATION: the SiteSection taxonomy field is NOT copied to the new
// reusable items in this pass (left blank). Reading/replicating a
// tag-selector field via raw SQL wasn't worth the added risk for a field
// that isn't displayed anywhere yet -- can be backfilled later if needed.
//
// Reuses the IContentItemManager.Create/TryPublish pattern already proven
// in the pilot, plus IWebPageManager.TryUpdateDraft + TryPublish (per
// Kentico's own Pages API docs) to update each existing page's
// GuidanceContent field without touching anything else on it.
string migrateAllClinicalGuidanceFlag = Environment.GetEnvironmentVariable("MIGRATE_ALL_CLINICAL_GUIDANCE");
if (!string.IsNullOrEmpty(migrateAllClinicalGuidanceFlag))
{
    const int auaWebsiteChannelId = 2;
    const string migrationWorkspaceName = "AUANet.org";
    // The one pilot test page created earlier already has its own matching
    // Content Hub item -- skip it here so it doesn't get a duplicate.
    const string pilotSlugToSkip = "clinical-guidance-atomic-pilot";


    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    var contentItemManagerFactory = app.Services.GetRequiredService<IContentItemManagerFactory>();
    var contentItemManager = contentItemManagerFactory.Create(user.UserID);


    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    // Fetches every existing AUA.ClinicalGuidanceDocument page's current
    // field values via direct SQL, same join shape already proven
    // throughout this project (AUA_<Type> -> CMS_ContentItemCommonData ->
    // CMS_WebPageItem), plus the page's own URL slug so the pilot page can
    // be excluded by name.
    var queryParams = new QueryDataParameters();
    queryParams.Add("@channelId", auaWebsiteChannelId);


    var migrationQueryResult = ConnectionHelper.ExecuteQuery(
        @"SELECT wpi.WebPageItemID, up.WebPageUrlPath,
                 cgd.Title, cgd.Headline, cgd.MetaDescription, cgd.MetaKeywords,
                 cgd.Body, cgd.NeedsManualContentReview, cgd.GuidanceContent
          FROM AUA_ClinicalGuidanceDocument cgd
          JOIN CMS_ContentItemCommonData ccd ON ccd.ContentItemCommonDataID = cgd.ContentItemDataCommonDataID
          JOIN CMS_WebPageItem wpi ON wpi.WebPageItemContentItemID = ccd.ContentItemCommonDataContentItemID
          JOIN CMS_WebPageUrlPath up ON up.WebPageUrlPathWebPageItemID = wpi.WebPageItemID
          WHERE wpi.WebPageItemWebsiteChannelID = @channelId
            AND up.WebPageUrlPathIsLatest = 1",
        queryParams,
        QueryTypeEnum.SQLQuery);


    int migrated = 0, skipped = 0, failed = 0;
    // The query joins in one row per URL path, and this site has /es/...
    // language-variant paths alongside the /en ones -- dedupe by
    // WebPageItemID so each page is only processed once, regardless of how
    // many URL path rows it joined in.
    var processedPageIds = new HashSet<int>();


    if (migrationQueryResult != null && migrationQueryResult.Tables.Count > 0)
    {
        foreach (System.Data.DataRow row in migrationQueryResult.Tables[0].Rows)
        {
            int pageId = Convert.ToInt32(row["WebPageItemID"]);
            string urlPath = row["WebPageUrlPath"]?.ToString() ?? "";


            if (!processedPageIds.Add(pageId))
            {
                continue; // already handled this page via a different URL path row
            }


            if (urlPath.TrimEnd('/').EndsWith(pilotSlugToSkip, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                Console.WriteLine($"Skipping pilot page: /{urlPath} (id={pageId})");
                continue;
            }


            // Makes this trigger safe to re-run: pages already migrated on a
            // prior run have a non-empty GuidanceContent value, so skip them
            // instead of creating a second duplicate Content Hub item.
            string existingGuidanceContent = row["GuidanceContent"] == DBNull.Value ? "" : row["GuidanceContent"].ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(existingGuidanceContent) && existingGuidanceContent.Trim() != "[]")
            {
                skipped++;
                Console.WriteLine($"Already migrated, skipping: /{urlPath} (id={pageId})");
                continue;
            }


            string title = row["Title"]?.ToString() ?? "";
            string headline = row["Headline"]?.ToString() ?? "";
            string metaDescription = row["MetaDescription"]?.ToString() ?? "";
            string metaKeywords = row["MetaKeywords"]?.ToString() ?? "";
            string body = row["Body"]?.ToString() ?? "";
            bool needsReview = row["NeedsManualContentReview"] != DBNull.Value && Convert.ToBoolean(row["NeedsManualContentReview"]);


            try
            {
                // Step 1: create the matching reusable Content Hub item.
                var contentFields = new Dictionary<string, object>
                {
                    { "Title", title },
                    { "Headline", headline },
                    { "MetaDescription", metaDescription },
                    { "MetaKeywords", metaKeywords },
                    { "Body", body },
                    { "NeedsManualContentReview", needsReview }
                };
                // CMS_ContentItemLanguageMetadata.ContentItemLanguageMetadataDisplayName
                // is capped around 100 chars -- several Clinical Guidance
                // titles are longer. Only the admin-UI display name needs
                // truncating; the real Title field above keeps the full text.
                string displayNameSafe = title.Length > 100 ? title.Substring(0, 100) : title;


                var contentItemData = new ContentItemData(contentFields);
                var contentCreateParameters = new CreateContentItemParameters(
                    contentTypeName: "AUA.ClinicalGuidanceContent",
                    name: null,
                    displayName: displayNameSafe,
                    languageName: "en",
                    workspaceName: migrationWorkspaceName);


                int contentItemId = await contentItemManager.Create(contentCreateParameters, contentItemData);
                await contentItemManager.TryPublish(contentItemId, "en");
                Guid contentItemGuid = CMS.ContentEngine.Internal.ContentItemInfo.Provider.Get(contentItemId).ContentItemGUID;


                // Step 2: point this existing page's GuidanceContent field at
                // the new item -- everything else on the page is written
                // back unchanged.
                var pageFields = new Dictionary<string, object>
                {
                    { "Title", title },
                    { "Headline", headline },
                    { "MetaDescription", metaDescription },
                    { "MetaKeywords", metaKeywords },
                    { "Body", body },
                    { "NeedsManualContentReview", needsReview },
                    { "GuidanceContent", new List<ContentItemReference> { new ContentItemReference { Identifier = contentItemGuid } } }
                };
                var updatedPageData = new ContentItemData(pageFields);


                // These are live Published pages, not existing drafts --
                // TryUpdateDraft requires a draft to already exist, so check
                // one out first. Ignoring the return value here is fine:
                // if a draft already exists for some reason, TryUpdateDraft
                // below still succeeds against it either way.
                await webPageManager.TryCreateDraft(pageId, "en");


                bool draftUpdated = await webPageManager.TryUpdateDraft(pageId, "en", new UpdateDraftData(updatedPageData));
                if (!draftUpdated)
                {
                    throw new Exception("TryUpdateDraft returned false");
                }
                await webPageManager.TryPublish(pageId, "en");


                migrated++;
                Console.WriteLine($"Migrated: /{urlPath} (page={pageId}, contentItem={contentItemGuid})");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"MIGRATION FAILED: /{urlPath} (page={pageId}) -- {ex.Message}");
            }
        }
    }


    Console.WriteLine($"Done. Migrated={migrated} Skipped={skipped} Failed={failed}");
    return;
}


// Migrates every AUA.StandardContentPage (614 pages) to the same atomic
// content model Clinical Guidance already uses -- see
// MIGRATE_ALL_CLINICAL_GUIDANCE above for the full rationale/pattern this
// copies exactly, just retargeted at AUA_StandardContentPage /
// AUA.StandardContent / the new PageContent link field.
//
// Taxonomy tag fields (SiteSection, Audience, ClinicalDomain) deliberately
// stay on the PAGE, not the new reusable item -- same call already made for
// Clinical Guidance (see that trigger's KNOWN LIMITATION comment). The Tag
// sidebar in StandardContentPageDefault.cshtml reads currentPage.SiteSection
// / .Audience / .ClinicalDomain directly and is unaffected by this
// migration either way.
//
// PREREQUISITES (admin UI, before running this) -- see
// CONTENT_HUB_MIGRATION_SETUP.md:
//   1. New reusable content type AUA.StandardContent: Title, Headline,
//      MetaDescription, MetaKeywords, Body, NeedsManualContentReview.
//   2. New field PageContent on AUA.StandardContentPage (Pages and reusable
//      content -> AUA.StandardContent, Combined content selector, single item).
//   3. Codegen both (ReusableContentTypes for AUA.StandardContent,
//      PageContentTypes for AUA.StandardContentPage).
//
// PERFORMANCE NOTE: 614 pages, each 2 API round-trips (create Content Hub
// item + create/update/publish draft page) -- expect this to take a while
// (tens of minutes, not seconds). Idempotent/safe to stop and re-run: any
// page whose PageContent is already non-empty gets skipped, so a partial
// run just picks up where it left off.
string migrateAllStandardContentFlag = Environment.GetEnvironmentVariable("MIGRATE_ALL_STANDARD_CONTENT");
if (!string.IsNullOrEmpty(migrateAllStandardContentFlag))
{
    const int auaWebsiteChannelId = 2;
    const string migrationWorkspaceName = "AUANet.org";


    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    var contentItemManagerFactory = app.Services.GetRequiredService<IContentItemManagerFactory>();
    var contentItemManager = contentItemManagerFactory.Create(user.UserID);


    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    var queryParams = new QueryDataParameters();
    queryParams.Add("@channelId", auaWebsiteChannelId);


    var migrationQueryResult = ConnectionHelper.ExecuteQuery(
        @"SELECT wpi.WebPageItemID, up.WebPageUrlPath,
                 scp.Title, scp.Headline, scp.MetaDescription, scp.MetaKeywords,
                 scp.Body, scp.NeedsManualContentReview, scp.PageContent
          FROM AUA_StandardContentPage scp
          JOIN CMS_ContentItemCommonData ccd ON ccd.ContentItemCommonDataID = scp.ContentItemDataCommonDataID
          JOIN CMS_WebPageItem wpi ON wpi.WebPageItemContentItemID = ccd.ContentItemCommonDataContentItemID
          JOIN CMS_WebPageUrlPath up ON up.WebPageUrlPathWebPageItemID = wpi.WebPageItemID
          WHERE wpi.WebPageItemWebsiteChannelID = @channelId
            AND up.WebPageUrlPathIsLatest = 1",
        queryParams,
        QueryTypeEnum.SQLQuery);


    int migrated = 0, skipped = 0, failed = 0;
    var processedPageIds = new HashSet<int>();


    if (migrationQueryResult != null && migrationQueryResult.Tables.Count > 0)
    {
        foreach (System.Data.DataRow row in migrationQueryResult.Tables[0].Rows)
        {
            int pageId = Convert.ToInt32(row["WebPageItemID"]);
            string urlPath = row["WebPageUrlPath"]?.ToString() ?? "";


            if (!processedPageIds.Add(pageId))
            {
                continue; // already handled this page via a different URL path row
            }


            string existingPageContent = row["PageContent"] == DBNull.Value ? "" : row["PageContent"].ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(existingPageContent) && existingPageContent.Trim() != "[]")
            {
                skipped++;
                Console.WriteLine($"Already migrated, skipping: /{urlPath} (id={pageId})");
                continue;
            }


            string title = row["Title"]?.ToString() ?? "";
            string headline = row["Headline"]?.ToString() ?? "";
            string metaDescription = row["MetaDescription"]?.ToString() ?? "";
            string metaKeywords = row["MetaKeywords"]?.ToString() ?? "";
            string body = row["Body"]?.ToString() ?? "";
            bool needsReview = row["NeedsManualContentReview"] != DBNull.Value && Convert.ToBoolean(row["NeedsManualContentReview"]);


            try
            {
                var contentFields = new Dictionary<string, object>
                {
                    { "Title", title },
                    { "Headline", headline },
                    { "MetaDescription", metaDescription },
                    { "MetaKeywords", metaKeywords },
                    { "Body", body },
                    { "NeedsManualContentReview", needsReview }
                };
                string displayNameSafe = title.Length > 100 ? title.Substring(0, 100) : title;


                var contentItemData = new ContentItemData(contentFields);
                var contentCreateParameters = new CreateContentItemParameters(
                    contentTypeName: "AUA.StandardContent",
                    name: null,
                    displayName: displayNameSafe,
                    languageName: "en",
                    workspaceName: migrationWorkspaceName);


                int contentItemId = await contentItemManager.Create(contentCreateParameters, contentItemData);
                await contentItemManager.TryPublish(contentItemId, "en");
                Guid contentItemGuid = CMS.ContentEngine.Internal.ContentItemInfo.Provider.Get(contentItemId).ContentItemGUID;


                var pageFields = new Dictionary<string, object>
                {
                    { "Title", title },
                    { "Headline", headline },
                    { "MetaDescription", metaDescription },
                    { "MetaKeywords", metaKeywords },
                    { "Body", body },
                    { "NeedsManualContentReview", needsReview },
                    { "PageContent", new List<ContentItemReference> { new ContentItemReference { Identifier = contentItemGuid } } }
                };
                var updatedPageData = new ContentItemData(pageFields);


                await webPageManager.TryCreateDraft(pageId, "en");
                bool draftUpdated = await webPageManager.TryUpdateDraft(pageId, "en", new UpdateDraftData(updatedPageData));
                if (!draftUpdated)
                {
                    throw new Exception("TryUpdateDraft returned false");
                }
                await webPageManager.TryPublish(pageId, "en");


                migrated++;
                if (migrated % 25 == 0)
                {
                    Console.WriteLine($"...{migrated} migrated so far (still running)");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"MIGRATION FAILED: /{urlPath} (page={pageId}) -- {ex.Message}");
            }
        }
    }


    Console.WriteLine($"Done. Migrated={migrated} Skipped={skipped} Failed={failed}");
    return;
}


// Same migration, retargeted at AUA.LongFormDocumentPage (~45 pages) / new
// reusable type AUA.LongFormContent / new link field DocumentContent -- see
// MIGRATE_ALL_STANDARD_CONTENT immediately above for the full comment/
// rationale, not repeated here.
//
// PREREQUISITES -- see CONTENT_HUB_MIGRATION_SETUP.md:
//   1. New reusable content type AUA.LongFormContent: Title, Headline,
//      MetaDescription, MetaKeywords, Body, NeedsManualContentReview.
//   2. New field DocumentContent on AUA.LongFormDocumentPage (Pages and
//      reusable content -> AUA.LongFormContent, Combined content selector,
//      single item).
//   3. Codegen both.
string migrateAllLongformContentFlag = Environment.GetEnvironmentVariable("MIGRATE_ALL_LONGFORM_CONTENT");
if (!string.IsNullOrEmpty(migrateAllLongformContentFlag))
{
    const int auaWebsiteChannelId = 2;
    const string migrationWorkspaceName = "AUANet.org";


    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    var contentItemManagerFactory = app.Services.GetRequiredService<IContentItemManagerFactory>();
    var contentItemManager = contentItemManagerFactory.Create(user.UserID);


    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    var queryParams = new QueryDataParameters();
    queryParams.Add("@channelId", auaWebsiteChannelId);


    var migrationQueryResult = ConnectionHelper.ExecuteQuery(
        @"SELECT wpi.WebPageItemID, up.WebPageUrlPath,
                 lfp.Title, lfp.Headline, lfp.MetaDescription, lfp.MetaKeywords,
                 lfp.Body, lfp.NeedsManualContentReview, lfp.DocumentContent
          FROM AUA_LongFormDocumentPage lfp
          JOIN CMS_ContentItemCommonData ccd ON ccd.ContentItemCommonDataID = lfp.ContentItemDataCommonDataID
          JOIN CMS_WebPageItem wpi ON wpi.WebPageItemContentItemID = ccd.ContentItemCommonDataContentItemID
          JOIN CMS_WebPageUrlPath up ON up.WebPageUrlPathWebPageItemID = wpi.WebPageItemID
          WHERE wpi.WebPageItemWebsiteChannelID = @channelId
            AND up.WebPageUrlPathIsLatest = 1",
        queryParams,
        QueryTypeEnum.SQLQuery);


    int migrated = 0, skipped = 0, failed = 0;
    var processedPageIds = new HashSet<int>();


    if (migrationQueryResult != null && migrationQueryResult.Tables.Count > 0)
    {
        foreach (System.Data.DataRow row in migrationQueryResult.Tables[0].Rows)
        {
            int pageId = Convert.ToInt32(row["WebPageItemID"]);
            string urlPath = row["WebPageUrlPath"]?.ToString() ?? "";


            if (!processedPageIds.Add(pageId))
            {
                continue;
            }


            string existingDocumentContent = row["DocumentContent"] == DBNull.Value ? "" : row["DocumentContent"].ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(existingDocumentContent) && existingDocumentContent.Trim() != "[]")
            {
                skipped++;
                Console.WriteLine($"Already migrated, skipping: /{urlPath} (id={pageId})");
                continue;
            }


            string title = row["Title"]?.ToString() ?? "";
            string headline = row["Headline"]?.ToString() ?? "";
            string metaDescription = row["MetaDescription"]?.ToString() ?? "";
            string metaKeywords = row["MetaKeywords"]?.ToString() ?? "";
            string body = row["Body"]?.ToString() ?? "";
            bool needsReview = row["NeedsManualContentReview"] != DBNull.Value && Convert.ToBoolean(row["NeedsManualContentReview"]);


            try
            {
                var contentFields = new Dictionary<string, object>
                {
                    { "Title", title },
                    { "Headline", headline },
                    { "MetaDescription", metaDescription },
                    { "MetaKeywords", metaKeywords },
                    { "Body", body },
                    { "NeedsManualContentReview", needsReview }
                };
                string displayNameSafe = title.Length > 100 ? title.Substring(0, 100) : title;


                var contentItemData = new ContentItemData(contentFields);
                var contentCreateParameters = new CreateContentItemParameters(
                    contentTypeName: "AUA.LongFormContent",
                    name: null,
                    displayName: displayNameSafe,
                    languageName: "en",
                    workspaceName: migrationWorkspaceName);


                int contentItemId = await contentItemManager.Create(contentCreateParameters, contentItemData);
                await contentItemManager.TryPublish(contentItemId, "en");
                Guid contentItemGuid = CMS.ContentEngine.Internal.ContentItemInfo.Provider.Get(contentItemId).ContentItemGUID;


                var pageFields = new Dictionary<string, object>
                {
                    { "Title", title },
                    { "Headline", headline },
                    { "MetaDescription", metaDescription },
                    { "MetaKeywords", metaKeywords },
                    { "Body", body },
                    { "NeedsManualContentReview", needsReview },
                    { "DocumentContent", new List<ContentItemReference> { new ContentItemReference { Identifier = contentItemGuid } } }
                };
                var updatedPageData = new ContentItemData(pageFields);


                await webPageManager.TryCreateDraft(pageId, "en");
                bool draftUpdated = await webPageManager.TryUpdateDraft(pageId, "en", new UpdateDraftData(updatedPageData));
                if (!draftUpdated)
                {
                    throw new Exception("TryUpdateDraft returned false");
                }
                await webPageManager.TryPublish(pageId, "en");


                migrated++;
                Console.WriteLine($"Migrated: /{urlPath} (page={pageId}, contentItem={contentItemGuid})");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"MIGRATION FAILED: /{urlPath} (page={pageId}) -- {ex.Message}");
            }
        }
    }


    Console.WriteLine($"Done. Migrated={migrated} Skipped={skipped} Failed={failed}");
    return;
}


// Twelfth one-shot trigger: writes Audience + Clinical Domain taxonomy tag
// assignments onto already-imported AUA.StandardContentPage pages, from the
// heuristic keyword-tagging pass (auanet_tag_audience_domain.py) -- HIGH
// CONFIDENCE TAGS ONLY, per Pat's call to commit to just those rather than
// the looser "possible" tier.
//
// *** VERIFY THIS FIRST, BEFORE ANYTHING ELSE ***
// RUNBOOK.md (dated 2026-07-30, from earlier this project) claims a
// "AUA.ClinicalDomain" taxonomy -- with these exact same 12 terms, same
// order -- was already imported via UMT, well before today's tagging
// discussion. Run this in SSMS against the live DB before assuming anything:
//     SELECT TaxonomyName, TaxonomyDescription, TaxonomyGUID FROM CMS_Taxonomy;
//     SELECT t.TagTitle FROM CMS_Tag t JOIN CMS_Taxonomy tx ON tx.TaxonomyID = t.TagTaxonomyID WHERE tx.TaxonomyName = 'AUA.ClinicalDomain';
// If AUA.ClinicalDomain is already there with all 12 terms, SKIP creating it
// again -- just add the Tag field to AUA.StandardContentPage pointing at the
// existing taxonomy. If it's NOT there (import never actually ran, or this
// is a different DB), create it fresh using the taxonomy NAME
// "AUA.ClinicalDomain" specifically (matching the historical file), not a
// new name, in case anything else still references the old GUID/name later.
// "Audience" is NOT in that historical file -- that one is genuinely new,
// build it fresh. Named "AUA.Audience" below for consistency with the
// AUA.SiteSection / AUA.ClinicalDomain naming convention already established
// in this project.
//
// PREREQUISITES -- do these in the admin UI BEFORE running this trigger,
// this will fail without them:
//   1. Verify/create taxonomy "AUA.Audience" with these 10 terms: Member,
//      Resident/Fellow, Medical Student, International, Advanced Practice
//      Provider, Researcher, Patient, Active/Associate, Affiliate, Research
//      Scientist.
//   2. Verify/create taxonomy "AUA.ClinicalDomain" with these 12 terms:
//      Upper Tract Reconstruction, Lower Tract Reconstruction,
//      Urolithiasis/Endourology, Female Voiding Dysfunction & Pelvic Floor,
//      BPH & Male Voiding Dysfunction, Sexual Dysfunction/Infertility/
//      Andrology, Renal & Adrenal Oncology, Prostate Oncology, Penile &
//      Testis Oncology, Bladder & Urothelial Oncology, Pediatrics,
//      Transplant/Renovascular/Misc. Benign.
//   3. Add a Tag selector field "Audience" (allow multiple, taxonomy =
//      AUA.Audience) to AUA.StandardContentPage.
//   4. Add a Tag selector field "ClinicalDomain" (allow multiple, taxonomy =
//      AUA.ClinicalDomain) to AUA.StandardContentPage.
//   5. Regenerate code files for AUA.StandardContentPage.
//
// Finds each page by its LEAF url slug (last path segment) rather than
// trying to reconstruct its current full nested path from the historical
// restructuring files -- Kentico enforces URL-slug uniqueness channel-wide
// (see the restructuring trigger's own comments above), so this is safe
// regardless of how deep the page ended up after the folder restructure.
//
// Tag GUIDs are looked up by title AT RUNTIME via direct SQL against
// CMS_Tag/CMS_Taxonomy, not hardcoded -- these two taxonomies get created
// fresh via the admin UI right before this runs, so there's no way to know
// their GUIDs ahead of time (and UMT, which used to handle this kind of
// fixed-GUID taxonomy import, was removed from this project). The
// CMS_Tag/CMS_Taxonomy column names below (TagTitle, TagGUID, TagTaxonomyID,
// TaxonomyName, TaxonomyID) are a best-effort guess based on the shape of
// every other CMS_* table already confirmed working in this file -- if this
// throws a column-not-found error, run
// "SELECT TOP 5 * FROM CMS_Tag" in SSMS to see the real column names and
// paste them back.
//
// Set AUDIENCE_DOMAIN_TAG_FILE to audience_domain_tag_trigger_input.json and
// run `dotnet run` normally.
string audienceDomainTagFile = Environment.GetEnvironmentVariable("AUDIENCE_DOMAIN_TAG_FILE");
if (!string.IsNullOrEmpty(audienceDomainTagFile))
{
    const int auaWebsiteChannelId = 2;
    // Confirmed via direct query against the live DB: the Audience taxonomy
    // was actually created as "Audience", not "AUA.Audience" -- fixed after
    // the first run showed 92 unresolved tags, all Audience. ClinicalDomain
    // still assumed to match the pre-existing "AUA.ClinicalDomain" name from
    // the earlier UMT import (that one worked correctly on the first run --
    // 62 pages got updated, which could only be the ClinicalDomain half
    // succeeding while Audience failed). Worth a quick confirm too:
    //   SELECT TaxonomyName FROM CMS_Taxonomy;
    const string audienceTaxonomyName = "Audience";
    const string clinicalDomainTaxonomyName = "AUA.ClinicalDomain";


    var userInfoProvider = app.Services.GetRequiredService<IUserInfoProvider>();
    var user = userInfoProvider.Get("administrator");


    var webPageManagerFactory = app.Services.GetRequiredService<IWebPageManagerFactory>();
    var webPageManager = webPageManagerFactory.Create(auaWebsiteChannelId, user.UserID);


    // Finds a page anywhere in the tree by its LEAF url slug -- see comment
    // block above for why this is safe.
    int FindPageIdByLeafSlugForTagging(string slug)
    {
        var parameters = new QueryDataParameters();
        parameters.Add("@slug", slug);
        parameters.Add("@slugSuffix", "%/" + slug);
        parameters.Add("@channelId", auaWebsiteChannelId);


        var result = ConnectionHelper.ExecuteQuery(
            @"SELECT TOP 1 wpi.WebPageItemID
              FROM CMS_WebPageUrlPath up
              JOIN CMS_WebPageItem wpi ON wpi.WebPageItemID = up.WebPageUrlPathWebPageItemID
              WHERE up.WebPageUrlPathIsLatest = 1
                AND wpi.WebPageItemWebsiteChannelID = @channelId
                AND (up.WebPageUrlPath = @slug OR up.WebPageUrlPath LIKE @slugSuffix)",
            parameters,
            QueryTypeEnum.SQLQuery);


        if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
        {
            return Convert.ToInt32(result.Tables[0].Rows[0]["WebPageItemID"]);
        }


        return 0;
    }


    // Looks up a tag's GUID by its display title within a given taxonomy.
    // See the big comment block above this trigger re: unconfirmed column
    // names -- paste back the error if this doesn't match your schema.
    Guid? FindTagGuidByTitle(string taxonomyName, string tagTitle)
    {
        var parameters = new QueryDataParameters();
        parameters.Add("@taxonomyName", taxonomyName);
        parameters.Add("@tagTitle", tagTitle);


        var result = ConnectionHelper.ExecuteQuery(
            @"SELECT TOP 1 t.TagGUID
              FROM CMS_Tag t
              JOIN CMS_Taxonomy tx ON tx.TaxonomyID = t.TagTaxonomyID
              WHERE tx.TaxonomyName = @taxonomyName
                AND t.TagTitle = @tagTitle",
            parameters,
            QueryTypeEnum.SQLQuery);


        if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
        {
            return (Guid)result.Tables[0].Rows[0]["TagGUID"];
        }


        return null;
    }


    var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var json = await File.ReadAllTextAsync(audienceDomainTagFile);
    var records = System.Text.Json.JsonSerializer.Deserialize<List<AudienceDomainTagRecord>>(json, jsonOpts)!;


    int updated = 0, notFound = 0, unresolvedTags = 0, failed = 0;
    foreach (var rec in records)
    {
        var audienceTerms = rec.AudienceTerms ?? new List<string>();
        var domainTerms = rec.ClinicalDomainTerms ?? new List<string>();


        try
        {
            int pageId = FindPageIdByLeafSlugForTagging(rec.Slug);
            if (pageId == 0)
            {
                notFound++;
                Console.WriteLine($"PAGE NOT FOUND: {rec.Slug}");
                continue;
            }


            var audienceRefs = audienceTerms
                .Select(t => (term: t, guid: FindTagGuidByTitle(audienceTaxonomyName, t)))
                .ToList();
            var domainRefs = domainTerms
                .Select(t => (term: t, guid: FindTagGuidByTitle(clinicalDomainTaxonomyName, t)))
                .ToList();


            foreach (var r in audienceRefs.Concat(domainRefs).Where(r => !r.guid.HasValue))
            {
                unresolvedTags++;
                Console.WriteLine($"WARNING: tag '{r.term}' not found in its taxonomy (check spelling matches exactly) -- {rec.Slug}");
            }


            var fields = new Dictionary<string, object>();
            var resolvedAudience = audienceRefs.Where(r => r.guid.HasValue)
                .Select(r => new ContentItemReference { Identifier = r.guid!.Value }).ToList();
            var resolvedDomain = domainRefs.Where(r => r.guid.HasValue)
                .Select(r => new ContentItemReference { Identifier = r.guid!.Value }).ToList();


            if (resolvedAudience.Count > 0) fields["Audience"] = resolvedAudience;
            if (resolvedDomain.Count > 0) fields["ClinicalDomain"] = resolvedDomain;


            if (fields.Count == 0)
            {
                Console.WriteLine($"SKIPPED (no tags resolved): {rec.Slug}");
                continue;
            }


            // These are live Published pages -- TryUpdateDraft requires a
            // draft to already exist, so check one out first (same pattern
            // as the atomic-migration trigger above).
            await webPageManager.TryCreateDraft(pageId, "en");


            bool draftUpdated = await webPageManager.TryUpdateDraft(pageId, "en", new UpdateDraftData(new ContentItemData(fields)));
            if (!draftUpdated)
            {
                Console.WriteLine($"WARNING: TryUpdateDraft returned false for {rec.Slug}");
            }
            await webPageManager.TryPublish(pageId, "en");


            updated++;
            Console.WriteLine($"Tagged: {rec.Slug} -- Audience=[{string.Join(", ", audienceTerms)}] ClinicalDomain=[{string.Join(", ", domainTerms)}]");
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"FAILED: {rec.Slug} -- {ex.Message}");
        }
    }


    Console.WriteLine($"Done. Updated={updated} NotFound={notFound} UnresolvedTags={unresolvedTags} Failed={failed} Total={records.Count}");
    return;
}


// Both of these rewrite the incoming request's Host/Path before Kentico's own
// routing (app.UseKentico() below) resolves a website channel from it, so
// they have to run early in the pipeline -- before UseStaticFiles even,
// to be safe.


// 1. XperienceCommunity.DomainAliases -- rewrites an aliased domain's Host
// header to the associated channel's real domain. Configure aliases in the
// admin UI under the channel's Domain Aliases section.
app.UseDomainAliasesExtension();


// Internal Host rewrite: aua.lvh.me -> aua.localhost. Google/Facebook OAuth
// rejects *.localhost redirect URIs, so aua.lvh.me is used there instead --
// but the app still needs to serve it as the existing AUA channel. Unlike
// the membership.localhost redirect below, this is a silent in-process
// rewrite, NOT a real HTTP redirect: the browser's address bar must stay on
// aua.lvh.me the whole time, or the OAuth callback (which Google sends
// straight to aua.lvh.me/signin-google) would never land correctly.
// Replaces XperienceCommunity.DomainAliases for this one alias -- that
// package has a real cast bug (BQ.Xperience.DomainAliases vs
// XperienceCommunity.DomainAliases.Models) that crashes on any actual alias
// data, confirmed across both 2.0.0 and 2.1.0.
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host.Equals("aua.lvh.me", StringComparison.OrdinalIgnoreCase))
    {
        context.Request.Host = context.Request.Host.Port.HasValue
            ? new HostString("aua.localhost", context.Request.Host.Port.Value)
            : new HostString("aua.localhost");
    }


    await next();
});


// 2. Custom subdomain-to-path REDIRECT -- membership.localhost is not a
// separate channel or a domain alias, it's meant to send visitors to
// aua.localhost/membership. DomainAliases (above) can't do this -- it maps
// a domain to a channel's root, not to an arbitrary sub-path.
//
// This used to be an internal Host/Path rewrite (serve the aua.localhost
// content while staying on membership.localhost) instead of a real
// redirect, but that broke everything downstream of the first page load:
// the browser's address bar still said membership.localhost, so any
// absolute URL Kentico generated (using the internally-rewritten
// aua.localhost host), any domain-scoped cookie, etc. didn't match what the
// browser thought its own origin was. A real HTTP redirect avoids all of
// that -- once redirected, the browser is genuinely on aua.localhost, so
// everything behaves exactly like a normal visit there.
//
// Port is read from the INCOMING request rather than hardcoded, since
// Kestrel serves all hostnames on the same port in this dev setup (and in
// production there's typically no port at all, which this also handles).
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host.Equals("membership.localhost", StringComparison.OrdinalIgnoreCase))
    {
        var targetHost = context.Request.Host.Port.HasValue
            ? $"aua.localhost:{context.Request.Host.Port.Value}"
            : "aua.localhost";


        var targetPath = context.Request.Path.StartsWithSegments("/membership")
            ? context.Request.Path.Value
            : "/membership" + context.Request.Path;


        var redirectUrl = $"{context.Request.Scheme}://{targetHost}{targetPath}{context.Request.QueryString}";


        // permanent: false (302) while this is still being worked out --
        // switch to a 301 only once confirmed stable, since browsers cache
        // permanent redirects aggressively and a wrong 301 is annoying to
        // undo during testing.
        context.Response.Redirect(redirectUrl, permanent: false);
        return;
    }


    await next();
});


app.UseStaticFiles();


app.UseCookiePolicy();


app.UseAuthentication();


if (builder.Environment.IsDevelopment())
{
    // Management API (local development only -- see the matching registration above).
    app.UseKenticoManagementApi();
}


if (builder.Environment.IsQa() ||
    builder.Environment.IsUat() ||
    builder.Environment.IsEnvironment(CloudEnvironments.Custom) ||
    builder.Environment.IsEnvironment(CloudEnvironments.Staging) ||
    builder.Environment.IsProduction())
{
    app.UseKenticoCloud();
}


app.UseKentico();


app.UseAuthorization();


app.UseStatusCodePagesWithReExecute("/error/{0}");


app.Kentico().MapRoutes();


app.MapControllerRoute(
   name: "error",
   pattern: "error/{code}",
   defaults: new { controller = "HttpErrors", action = "Error" }
);


// Plain MVC route for /search -- not a Kentico content page, since the page has
// no editable content: results come from Algolia client-side via the Search-Only
// API key. Must be registered before the generic DEFAULT_ROUTE_NAME/
// DEFAULT_ROUTE_WITHOUT_LANGUAGE_PREFIX_NAME routes below, which require both a
// {controller} and {action} segment and wouldn't match a bare "/search" on their own.
app.MapControllerRoute(
   name: "search",
   pattern: "search",
   defaults: new { controller = "Search", action = "Index" }
);


// /ask -- question-answering page backed by Claude + the same Algolia index as
// /search. Two actions (Index for the page, Query for the POST endpoint the page's
// JS calls), so this route allows the action segment to vary instead of being
// hardcoded like the "search" route above.
app.MapControllerRoute(
   name: "ask",
   pattern: "ask/{action=Index}",
   defaults: new { controller = "Ask" }
);


// /api/account-lookup -- server-side proxy the home page's AJAX button calls.
// Same reasoning as the "search"/"ask" routes above: must be registered before
// the generic DEFAULT_ROUTE_NAME routes below.
app.MapControllerRoute(
   name: "account-lookup",
   pattern: "api/account-lookup",
   defaults: new { controller = "AccountLookup", action = "Lookup" }
);


// Real "Sign in with Google/Facebook" -- see ExternalAuthController.
// /external-auth/login (?provider=Google|Facebook), /external-auth/callback,
// /external-auth/logout, all GET.
app.MapControllerRoute(
   name: "external-auth",
   pattern: "external-auth/{action=Login}",
   defaults: new { controller = "ExternalAuth" }
);


// /topics?field=X&tag=Y -- lists pages sharing a tag, linked from the
// sidebar tag pills. See TopicsController.
app.MapControllerRoute(
   name: "topics",
   pattern: "topics",
   defaults: new { controller = "Topics", action = "Index" }
);


app.MapControllerRoute(
    name: DancingGoatConstants.DEFAULT_ROUTE_NAME,
    pattern: $"{{{WebPageRoutingOptions.LANGUAGE_ROUTE_VALUE_KEY}}}/{{controller}}/{{action}}",
    constraints: new
    {
        controller = DancingGoatConstants.CONSTRAINT_FOR_NON_ROUTER_PAGE_CONTROLLERS
    }
);


app.MapControllerRoute(
    name: DancingGoatConstants.DEFAULT_ROUTE_WITHOUT_LANGUAGE_PREFIX_NAME,
    pattern: "{controller}/{action}",
    constraints: new
    {
        controller = DancingGoatConstants.CONSTRAINT_FOR_NON_ROUTER_PAGE_CONTROLLERS
    }
);


app.Run();




static void ConfigureMembershipServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 0;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequiredUniqueChars = 0;
        // Ensures, that disabled member cannot sign in.
        options.SignIn.RequireConfirmedAccount = true;
    })
        .AddUserStore<ApplicationUserStore<ApplicationUser>>()
        .AddRoleStore<ApplicationRoleStore<ApplicationRole>>()
        .AddUserManager<UserManager<ApplicationUser>>()
        .AddRoleManager<RoleManager<ApplicationRole>>()
        .AddSignInManager<SignInManager<ApplicationUser>>();


    // Real "Sign in with Google/Facebook" -- see ExternalAuthController.
    // SignInScheme = IdentityConstants.ExternalScheme is required: it tells
    // ASP.NET Core to hold the provider's claims in a short-lived external
    // cookie until ExternalAuthController.Callback finishes linking/signing
    // in, rather than trying to sign the visitor straight into the app's
    // own cookie before a local ApplicationUser necessarily even exists.
    //
    // Credentials come from dotnet user-secrets (never appsettings.json),
    // keys "Authentication:Google:ClientId" / "Authentication:Google:ClientSecret"
    // and "Authentication:Facebook:AppId" / "Authentication:Facebook:AppSecret"
    // -- see OAUTH_SETUP.md for the exact commands and where to get real values
    // from Google Cloud Console / Facebook Developer app.
    services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = configuration["Authentication:Google:ClientId"];
            options.ClientSecret = configuration["Authentication:Google:ClientSecret"];
            options.SignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddFacebook(options =>
        {
            options.AppId = configuration["Authentication:Facebook:AppId"];
            options.AppSecret = configuration["Authentication:Facebook:AppSecret"];
            options.SignInScheme = IdentityConstants.ExternalScheme;
        });


    services.ConfigureApplicationCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.LoginPath = new PathString("/account/login");
        options.AccessDeniedPath = new PathString("/error/403");
        options.Events.OnRedirectToLogin = ctx =>
        {
            var factory = ctx.HttpContext.RequestServices.GetRequiredService<IUrlHelperFactory>();
            var urlHelper = factory.GetUrlHelper(new ActionContext(ctx.HttpContext, new RouteData(ctx.HttpContext.Request.RouteValues), new ActionDescriptor()));
            var url = urlHelper.Action("Login", "Account") + new Uri(ctx.RedirectUri).Query;


            ctx.Response.Redirect(url);


            return Task.CompletedTask;
        };
    });


    services.Configure<AdminIdentityOptions>(options =>
    {
        // The expiration time span of 8 hours is set for demo purposes only. In production environments, set expiration according to best practices.
        options.AuthenticationOptions.ExpireTimeSpan = TimeSpan.FromHours(8);


        // The forbidden passwords are set for demo purposes only. In production environments, set password options according to best practices.
        var companySpecificKeywords = new List<string> { "kentico", "dancinggoat", "admin", "coffee" };
        var specificNumberCombinations = new List<string> { "2023", "23", "2024", "24", "2025", "25" };
        options.PasswordOptions.ForbiddenPasswords = ForbiddenPasswordGenerator.Generate(companySpecificKeywords, specificNumberCombinations);
    });


    services.AddAuthorization();
}




static void ConfigureEmailBuilder(IServiceCollection services)
{
    services.Configure((EmailBuilderOptions options) =>
    {
        options.AllowedEmailContentTypeNames = ["DancingGoat.BuilderEmail"];
        options.RegisterDefaultSection = false;
        options.DefaultSectionIdentifier = DancingGoatFullWidthEmailSection.IDENTIFIER;
    });


    services.AddMjmlForEmails();
}


// Flat record shape for the IWPM_BATCH_FILE trigger -- one object per page,
// merged ahead of time from the raw UMT batch_step1-6.json files so the same
// underlying (already-truncated, already-tagged) content can be replayed
// through IWebPageManager instead.
public class BatchPageRecord
{
    public string ContentItemName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string UrlSlug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Headline { get; set; } = "";
    public string MetaDescription { get; set; } = "";
    public string MetaKeywords { get; set; } = "";
    public string Body { get; set; } = "";
    public bool NeedsManualContentReview { get; set; }
    public string SiteSectionTagGuid { get; set; }
    public string ContentItemGuid { get; set; } = "";
}


// Shapes for the RESTRUCTURE_FOLDERS_FILE / RESTRUCTURE_MOVES_FILE trigger.
public class FolderRecord
{
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ParentPath { get; set; }
}


public class PageMoveRecord
{
    public string UrlSlug { get; set; } = "";
    public string ParentPath { get; set; }
}


// Shape for the UPDATE_BODY_FILE trigger.
public class BodyUpdateRecord
{
    public string UrlSlug { get; set; } = "";
    public string Body { get; set; } = "";
}


// Shape for the LONGFORM_BATCH_FILE / LONGFORM_SPECIAL_UPDATE_FILE triggers.
public class LongFormPageRecord
{
    public string ContentType { get; set; } = ""; // "ClinicalGuidance" or "LongForm"
    public string ContentItemName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string UrlSlug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Headline { get; set; } = "";
    public string MetaDescription { get; set; } = "";
    public string MetaKeywords { get; set; } = "";
    public string Body { get; set; } = "";
    public bool NeedsManualContentReview { get; set; }
    public string SiteSectionTagGuid { get; set; }
    public string ContentItemGuid { get; set; } = "";
    public string ParentPath { get; set; } // used by LONGFORM_BATCH_FILE
    public string FullPath { get; set; } // used by LONGFORM_SPECIAL_UPDATE_FILE
}


// Shape for the AUDIENCE_DOMAIN_TAG_FILE trigger.
public class AudienceDomainTagRecord
{
    public string Slug { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> AudienceTerms { get; set; } = new();
    public List<string> ClinicalDomainTerms { get; set; } = new();
}


// Shapes for the MENU_SEED_FILE trigger.
public class MenuSeedData
{
    public string MenuTitle { get; set; } = "";
    public List<MenuSeedItem> Items { get; set; } = new();
}


public class MenuSeedItem
{
    public string Label { get; set; } = "";
    public string LinkUrl { get; set; } = "";
    public int Order { get; set; }
    // Optional -- only used by menus that need it (e.g. the "AUA Websites"
    // utility menu). Requires the additive Description field on
    // AUA.MenuItem; existing menus/items that don't set this are unaffected.
    public string? Description { get; set; }
    public List<MenuSeedItem> Children { get; set; } = new();
}


// Shape for the PILOT_ATOMIC_FILE trigger.
public class PilotAtomicContent
{
    public string SourceTitle { get; set; } = "";
    public string SourceHeadline { get; set; } = "";
    public string SourceMetaDescription { get; set; }
    public string SourceMetaKeywords { get; set; }
    public string SourceBody { get; set; } = "";
    public string SourceSiteSectionTagGuid { get; set; }
    public string PilotUrlSlug { get; set; } = "";
}




