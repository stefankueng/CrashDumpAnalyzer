﻿using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace CrashDumpAnalyzer.Controllers
{
    public class HomeController : Controller
    {
        private readonly HomePageService _pages;

        public HomeController(HomePageService pages)
        {
            _pages = pages;
        }

        public async Task<IActionResult> Index(int? deleted, string searchString, int activeTab)
        {
            return View(await _pages.GetIndex(deleted, searchString, activeTab));
        }

        public async Task<IActionResult> Dump(int callstackId)
        {
            return View(await _pages.GetDump(callstackId));
        }

        public async Task<IActionResult> Statistics()
        {
            return View(await _pages.GetStatistics());
        }

        [HttpGet]
        public async Task<IActionResult> GroupedDumpsAsync(int weeksBack = 8)
        {
            return View(await _pages.GetGroupedDumps(weeksBack));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
