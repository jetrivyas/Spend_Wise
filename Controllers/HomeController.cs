using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Data;
using ExpenseTracker.Models;

namespace ExpenseTracker.Controllers
{
    public class HomeController : Controller
    {
        private readonly ExpenseDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(ExpenseDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User)!;
            var now = DateTime.Today;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var startOfYear  = new DateTime(now.Year, 1, 1);

            var allExpenses = await _context.Expenses
                .Where(e => e.UserId == userId)
                .ToListAsync();

            var thisMonthExpenses = allExpenses.Where(e => e.Date >= startOfMonth).ToList();
            var thisYearExpenses  = allExpenses.Where(e => e.Date >= startOfYear).ToList();
            var daysInMonth = (now - startOfMonth).Days + 1;

            var monthlyTotals = new List<MonthlyTotal>();
            for (int i = 5; i >= 0; i--)
            {
                var month = now.AddMonths(-i);
                var start = new DateTime(month.Year, month.Month, 1);
                var end   = start.AddMonths(1);
                monthlyTotals.Add(new MonthlyTotal
                {
                    Month       = month.ToString("MMM"),
                    Year        = month.Year,
                    MonthNumber = month.Month,
                    Amount      = allExpenses.Where(e => e.Date >= start && e.Date < end).Sum(e => e.Amount)
                });
            }

            var categoryTotals = thisMonthExpenses
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

            var topCategory = categoryTotals.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
            var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.UserId == userId);

            var vm = new DashboardViewModel
            {
                RecentExpenses        = allExpenses.OrderByDescending(e => e.Date).Take(8).ToList(),
                TotalThisMonth        = thisMonthExpenses.Sum(e => e.Amount),
                TotalThisYear         = thisYearExpenses.Sum(e => e.Amount),
                TotalAllTime          = allExpenses.Sum(e => e.Amount),
                AverageDailyThisMonth = daysInMonth > 0 ? thisMonthExpenses.Sum(e => e.Amount) / daysInMonth : 0,
                CategoryTotals        = categoryTotals,
                MonthlyTotals         = monthlyTotals,
                TopCategory           = topCategory,
                TotalTransactions     = allExpenses.Count,
                Budget                = budget
            };

            return View(vm);
        }
    }
}
