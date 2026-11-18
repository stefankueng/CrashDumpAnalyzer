using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrashDumpAnalyzer.Controllers
{
    [AllowAnonymous]
    [Route("Settings")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public class SettingsController(SettingsService settings, IConfiguration configuration, ILogger<SettingsController> logger) : Controller
    {
        public static bool CanAccess(HttpContext context, IConfiguration configuration) =>
            SetupService.IsLocalRequest(context) ||
            (configuration.GetValue<bool>("UserAuthEnabled") && context.User.Identity?.IsAuthenticated == true && context.User.IsInRole("Admin"));

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!CanAccess(HttpContext, configuration))
                return NotFound();
            try
            {
                return View(await settings.LoadAsync());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                logger.LogError(ex, "Could not load settings.");
                return Problem("Could not load settings. Check the server logs and appsettings.json.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(SettingsModel model)
        {
            if (!CanAccess(HttpContext, configuration))
                return NotFound();
            if (ModelState.IsValid)
            {
                try
                {
                    await settings.SaveAsync(model);
                    TempData["SettingsSaved"] = "Settings saved. Restart the application to apply them.";
                    return RedirectToAction(nameof(Index));
                }
                catch (ValidationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                {
                    logger.LogError(ex, "Could not save settings.");
                    ModelState.AddModelError(string.Empty, "Settings could not be saved. Check the server logs and file permissions.");
                }
            }
            model.IssuePassword = model.IssueBearerToken = model.IssueToken = model.SmtpPassword = null;
            ModelState.Remove(nameof(model.SmtpPassword));
            ModelState.Remove(nameof(model.IssuePassword));
            ModelState.Remove(nameof(model.IssueBearerToken));
            ModelState.Remove(nameof(model.IssueToken));
            return View(model);
        }
    }
}
