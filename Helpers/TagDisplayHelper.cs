using System;
using System.Collections.Generic;
using CMS.DataEngine;

namespace AUA.Helpers
{
    /// <summary>
    /// Resolves tag GUIDs (from a page's TagReference-typed fields, e.g.
    /// currentPage.SiteSection / Audience / ClinicalDomain) into display
    /// titles + topic-index links, via direct SQL against CMS_Tag -- same
    /// "trust direct SQL over an uncertain generated-property shape" pattern
    /// used throughout this project's Program.cs triggers.
    ///
    /// UNVERIFIED CAVEAT: this assumes currentPage.SiteSection etc. read back
    /// as IEnumerable&lt;TagReference&gt; (the same type used to WRITE these
    /// fields everywhere else in this project) with a readable .Identifier
    /// Guid. That symmetry is a reasonable bet but hasn't actually been
    /// confirmed against this project's generated StandardContentPage class
    /// -- if currentPage.SiteSection turns out to be a different shape, the
    /// compiler error will say so plainly (paste it back).
    /// </summary>
    public static class TagDisplayHelper
    {
        public record TagDisplay(string Title, string TaxonomyFieldName, Guid Identifier);

        /// <summary>
        /// Resolves each tag GUID's display Title via CMS_Tag. taxonomyFieldName
        /// is the AUA.StandardContentPage field codename (e.g. "SiteSection",
        /// "Audience", "ClinicalDomain") -- used only to build the /topics link,
        /// not part of the SQL lookup itself.
        /// </summary>
        public static List<TagDisplay> ResolveTags(IEnumerable<Guid> tagGuids, string taxonomyFieldName)
        {
            var result = new List<TagDisplay>();

            foreach (var guid in tagGuids)
            {
                var parameters = new QueryDataParameters();
                parameters.Add("@tagGuid", guid);

                var queryResult = ConnectionHelper.ExecuteQuery(
                    "SELECT TOP 1 TagTitle FROM CMS_Tag WHERE TagGUID = @tagGuid",
                    parameters,
                    QueryTypeEnum.SQLQuery);

                if (queryResult != null && queryResult.Tables.Count > 0 && queryResult.Tables[0].Rows.Count > 0)
                {
                    string title = queryResult.Tables[0].Rows[0]["TagTitle"].ToString() ?? "";
                    result.Add(new TagDisplay(title, taxonomyFieldName, guid));
                }
            }

            return result;
        }

        /// <summary>
        /// Builds the /topics link for a resolved tag -- see TopicsController.
        /// </summary>
        public static string TopicUrl(TagDisplay tag) =>
            $"/topics?field={Uri.EscapeDataString(tag.TaxonomyFieldName)}&tag={Uri.EscapeDataString(tag.Identifier.ToString())}";
    }
}
