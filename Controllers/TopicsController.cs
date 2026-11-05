using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AUA;
using CMS.ContentEngine;
using CMS.DataEngine;
using Microsoft.AspNetCore.Mvc;

namespace AUA.Controllers
{
    /// <summary>
    /// Serves /topics?field={fieldCodename}&amp;tag={tagGuid} -- lists every
    /// AUA.StandardContentPage tagged with the given tag on the given field
    /// (SiteSection, Audience, or ClinicalDomain). Linked from the tag pills
    /// in the sidebar (see StandardContentPageDefault.cshtml + TagDisplayHelper).
    ///
    /// Uses WhereContainsTags -- confirmed against Kentico's own
    /// "Filter content based on taxonomies" guide (this is the officially
    /// documented way to filter a content item query by a tag-selector
    /// field), not a guess.
    ///
    /// UNVERIFIED CAVEAT: URL resolution for each matched page reuses the
    /// same direct-SQL pattern as BreadcrumbHelper.GetAncestorTrail (proven
    /// working elsewhere in this project). The tag-filtered query itself
    /// (WhereContainsTags) has NOT been run against this project's actual
    /// data yet -- if it throws, paste the error back.
    /// </summary>
    public class TopicsController : Controller
    {
        private readonly IContentQueryExecutor contentQueryExecutor;

        private const int AuaWebsiteChannelId = 2;

        public TopicsController(IContentQueryExecutor contentQueryExecutor)
        {
            this.contentQueryExecutor = contentQueryExecutor;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string field, string tag)
        {
            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(tag) || !Guid.TryParse(tag, out var tagGuid))
            {
                return BadRequest("field and a valid tag GUID are required.");
            }

            // Resolve the tag's display title for the page heading.
            string tagTitle = tag;
            var titleParams = new QueryDataParameters();
            titleParams.Add("@tagGuid", tagGuid);
            var titleResult = ConnectionHelper.ExecuteQuery(
                "SELECT TOP 1 TagTitle FROM CMS_Tag WHERE TagGUID = @tagGuid",
                titleParams, QueryTypeEnum.SQLQuery);
            if (titleResult != null && titleResult.Tables.Count > 0 && titleResult.Tables[0].Rows.Count > 0)
            {
                tagTitle = titleResult.Tables[0].Rows[0]["TagTitle"]?.ToString() ?? tag;
            }

            var queryBuilder = new ContentItemQueryBuilder()
                .ForContentType(StandardContentPage.CONTENT_TYPE_NAME, config => config
                    .Where(w => w.WhereContainsTags(field, new[] { tagGuid })))
                .InLanguage("en");

            var pages = await contentQueryExecutor.GetMappedResult<StandardContentPage>(queryBuilder);

            var results = new List<TopicResult>();
            foreach (var page in pages ?? Enumerable.Empty<StandardContentPage>())
            {
                var urlParams = new QueryDataParameters();
                urlParams.Add("@id", page.SystemFields.WebPageItemID);
                var urlResult = ConnectionHelper.ExecuteQuery(
                    @"SELECT TOP 1 WebPageUrlPath FROM CMS_WebPageUrlPath
                      WHERE WebPageUrlPathWebPageItemID = @id AND WebPageUrlPathIsLatest = 1",
                    urlParams, QueryTypeEnum.SQLQuery);

                string url = "/";
                if (urlResult != null && urlResult.Tables.Count > 0 && urlResult.Tables[0].Rows.Count > 0)
                {
                    url = "/" + (urlResult.Tables[0].Rows[0]["WebPageUrlPath"]?.ToString() ?? "");
                }

                results.Add(new TopicResult { Title = page.Title, Url = url });
            }

            return View(new TopicsViewModel
            {
                FieldName = field,
                TagTitle = tagTitle,
                Pages = results
            });
        }
    }

    public class TopicsViewModel
    {
        public string FieldName { get; set; } = "";
        public string TagTitle { get; set; } = "";
        public List<TopicResult> Pages { get; set; } = new();
    }

    public class TopicResult
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }
}
