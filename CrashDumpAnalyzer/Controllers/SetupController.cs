using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Text.Json;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrashDumpAnalyzer.Controllers
{
    [AllowAnonymous]
    [Route("Setup")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public class SetupController(SetupService setup, ILogger<SetupController> logger) : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            if (!setup.Required || !SetupService.IsLocalRequest(HttpContext))
                return NotFound();
            return setup.Completed ? View("Complete") : View(setup.GetDefaults());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(SetupModel model)
        {
            if (!setup.Required || setup.Completed || !SetupService.IsLocalRequest(HttpContext))
                return NotFound();
            if (ModelState.IsValid)
            {
                try
                {
                    await setup.SaveAsync(model);
                    return RedirectToAction(nameof(Index));
                }
                catch (ValidationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException or JsonException)
                {
                    logger.LogError(ex, "Initial setup failed while initializing the database or saving appsettings.json.");
                    ModelState.AddModelError(string.Empty, "Setup could not be saved. Check the server logs, database path, and write permissions for the installation folder, then retry.");
                }
            }
            model.AdminPassword = null;
            model.ConfirmPassword = null;
            model.SmtpPassword = null;
            ModelState.SetModelValue(nameof(model.AdminPassword), null, null);
            ModelState.SetModelValue(nameof(model.ConfirmPassword), null, null);
            ModelState.SetModelValue(nameof(model.SmtpPassword), null, null);
            return View(model);
        }
    }
}
