using System.Collections;

namespace AUA.Helpers
{
    /// <summary>
    /// Single place that turns a Content item asset field's value into a
    /// usable URL, for the hero image feature (Task 5).
    ///
    /// BEST-EFFORT GUESS, not fully confirmed: Kentico's docs describe a
    /// "ContentItemAsset" type for asset fields. Content item asset fields
    /// commonly resolve as a COLLECTION even when only one item is allowed
    /// in the admin UI (the underlying storage always supports multiple
    /// attachments), so GetUrl accepts either a single asset object or an
    /// IEnumerable of them and takes the first item in the latter case.
    /// If currentPage.HeroImage turns out to be a different shape entirely,
    /// replace the body of GetSingleAssetUrl with whatever actually works --
    /// this is isolated here on purpose so that's a one-file change.
    /// </summary>
    public static class HeroImageAssetUrlRetriever
    {
        public static string GetUrl(dynamic assetOrAssets)
        {
            if (assetOrAssets == null)
            {
                return null;
            }

            if (assetOrAssets is IEnumerable enumerable && !(assetOrAssets is string))
            {
                object first = null;
                foreach (var item in enumerable)
                {
                    first = item;
                    break;
                }

                return first == null ? null : GetSingleAssetUrl(first);
            }

            return GetSingleAssetUrl(assetOrAssets);
        }

        private static string GetSingleAssetUrl(dynamic asset)
        {
            // Try the most likely property names in order -- remove this
            // fallback chain once the real shape is confirmed and just
            // return the right one directly.
            try { return asset.Url; }
            catch { /* fall through */ }

            try { return asset.Path; }
            catch { /* fall through */ }

            return null;
        }
    }
}
