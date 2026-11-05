using Microsoft.AspNetCore.Mvc;

namespace AUA.Controllers
{
    /// <summary>
    /// Serves the /search page. Not part of the Kentico content tree -- this is a
    /// plain MVC route (registered in Program.cs) rather than a CMS page, since the
    /// page itself has no editable content: results are fetched client-side directly
    /// from Algolia using the Search-Only API key, which is safe to expose in JS.
    /// </summary>
    public class SearchController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
