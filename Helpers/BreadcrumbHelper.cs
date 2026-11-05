using System;
using System.Collections.Generic;
using System.Linq;
using CMS.DataEngine;

namespace AUA.Helpers
{
    /// <summary>
    /// Builds a breadcrumb trail for a page, given its WebPageItemID. Reuses the
    /// same direct-SQL approach already established throughout this project's
    /// Program.cs triggers (CMS_WebPageUrlPath / CMS_WebPageItem), rather than a
    /// Kentico ancestor-lookup API, for consistency and reliability.
    ///
    /// Every folder/ancestor position in this site's content tree was built as a
    /// real AUA.StandardContentPage (see the folder-restructuring RUNBOOK) -- only
    /// the final/leaf segment (the page itself) can be one of the other content
    /// types. So ancestor titles are looked up against AUA_StandardContentPage
    /// specifically; the leaf page's own title should come from the calling
    /// view's already-loaded `currentPage.Title` instead of a further query.
    /// </summary>
    public static class BreadcrumbHelper
    {
        /// <summary>
        /// Returns (Title, Url) pairs for every ANCESTOR of the given page, in
        /// root-to-leaf order (does not include the page itself -- add that
        /// separately in the view using the already-loaded currentPage.Title).
        /// </summary>
        public static List<(string Title, string Url)> GetAncestorTrail(int webPageItemId, int websiteChannelId)
        {
            var trail = new List<(string Title, string Url)>();

            var pathParams = new QueryDataParameters();
            pathParams.Add("@id", webPageItemId);

            var pathResult = ConnectionHelper.ExecuteQuery(
                @"SELECT TOP 1 up.WebPageUrlPath
                  FROM CMS_WebPageUrlPath up
                  WHERE up.WebPageUrlPathWebPageItemID = @id
                    AND up.WebPageUrlPathIsLatest = 1",
                pathParams,
                QueryTypeEnum.SQLQuery);

            if (pathResult == null || pathResult.Tables.Count == 0 || pathResult.Tables[0].Rows.Count == 0)
            {
                return trail;
            }

            string fullPath = pathResult.Tables[0].Rows[0]["WebPageUrlPath"]?.ToString() ?? "";
            var segments = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // All segments except the last one (the current page itself).
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string prefix = string.Join("/", segments.Take(i + 1));

                var titleParams = new QueryDataParameters();
                titleParams.Add("@path", prefix);
                titleParams.Add("@channelId", websiteChannelId);

                var titleResult = ConnectionHelper.ExecuteQuery(
                    @"SELECT TOP 1 scp.Title
                      FROM CMS_WebPageUrlPath up
                      JOIN CMS_WebPageItem wpi ON wpi.WebPageItemID = up.WebPageUrlPathWebPageItemID
                      JOIN CMS_ContentItemCommonData ccd ON ccd.ContentItemCommonDataContentItemID = wpi.WebPageItemContentItemID
                      JOIN AUA_StandardContentPage scp ON scp.ContentItemDataCommonDataID = ccd.ContentItemCommonDataID
                      WHERE up.WebPageUrlPathIsLatest = 1
                        AND wpi.WebPageItemWebsiteChannelID = @channelId
                        AND up.WebPageUrlPath = @path",
                    titleParams,
                    QueryTypeEnum.SQLQuery);

                string title = (titleResult != null && titleResult.Tables.Count > 0 && titleResult.Tables[0].Rows.Count > 0)
                    ? titleResult.Tables[0].Rows[0]["Title"]?.ToString()
                    : null;

                trail.Add((string.IsNullOrEmpty(title) ? prefix : title, "/" + prefix));
            }

            return trail;
        }
    }
}
