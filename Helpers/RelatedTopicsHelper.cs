using System.Collections.Generic;
using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Websites;

namespace AUA.Helpers
{
    /// <summary>
    /// Resolves the pages linked from an AUA.RelatedTopics item's RelatedPages
    /// field into (Title, Url) pairs for the sidebar card (Task 6).
    ///
    /// UNVERIFIED CAVEAT: RelatedPages is a "Pages and reusable content" /
    /// Combined content selector field NOT restricted to a single content
    /// type, so Kentico's codegen can only type it generically -- assumed
    /// here as IEnumerable&lt;IContentItemFieldsSource&gt;. If RelatedPages
    /// compiles as a different type, paste the error back -- one-file fix.
    ///
    /// URL resolution uses IWebPageFieldsSource.GetUrl().RelativePath --
    /// confirmed via Kentico's own "Retrieve page URLs" doc, which notes
    /// GetUrl() never triggers an extra DB query, so this is preferable to
    /// the direct-SQL pattern used elsewhere in this project (BreadcrumbHelper)
    /// wherever the item is already a resolved IWebPageFieldsSource, as it is
    /// here. Title still has to go through direct SQL across all three AUA
    /// page tables (LEFT JOIN + COALESCE) since GetUrl() only gives the URL,
    /// not the page's own Title field.
    /// </summary>
    public static class RelatedTopicsHelper
    {
        public static List<(string Title, string Url)> ResolveRelatedPages(IEnumerable<IContentItemFieldsSource> pages)
        {
            var result = new List<(string Title, string Url)>();

            if (pages == null)
            {
                return result;
            }

            foreach (var page in pages)
            {
                // A related item could technically be a non-page reusable
                // content item too (RelatedPages isn't restricted to pages
                // only), so this cast can legitimately come back null.
                var webPage = page as IWebPageFieldsSource;
                if (webPage == null)
                {
                    continue;
                }

                int webPageItemId = webPage.SystemFields.WebPageItemID;
                string url = webPage.GetUrl()?.RelativePath;
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                var titleParams = new QueryDataParameters();
                titleParams.Add("@id", webPageItemId);
                var titleResult = ConnectionHelper.ExecuteQuery(
                    @"SELECT TOP 1 COALESCE(scp.Title, lfp.Title, cgd.Title) AS Title
                      FROM CMS_WebPageItem wpi
                      JOIN CMS_ContentItemCommonData ccd ON ccd.ContentItemCommonDataContentItemID = wpi.WebPageItemContentItemID
                      LEFT JOIN AUA_StandardContentPage scp ON scp.ContentItemDataCommonDataID = ccd.ContentItemCommonDataID
                      LEFT JOIN AUA_LongFormDocumentPage lfp ON lfp.ContentItemDataCommonDataID = ccd.ContentItemCommonDataID
                      LEFT JOIN AUA_ClinicalGuidanceDocument cgd ON cgd.ContentItemDataCommonDataID = ccd.ContentItemCommonDataID
                      WHERE wpi.WebPageItemID = @id",
                    titleParams,
                    QueryTypeEnum.SQLQuery);

                string title = url.TrimStart('/');
                if (titleResult != null && titleResult.Tables.Count > 0 && titleResult.Tables[0].Rows.Count > 0)
                {
                    title = titleResult.Tables[0].Rows[0]["Title"]?.ToString() ?? title;
                }

                result.Add((title, url));
            }

            return result;
        }
    }
}
