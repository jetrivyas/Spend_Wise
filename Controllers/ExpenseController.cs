using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Services;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    public class ExpenseController : Controller
    {
        private readonly ExpenseDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ExpenseController(ExpenseDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Expense
        public async Task<IActionResult> Index(string? category, string? search,
            DateTime? startDate, DateTime? endDate, string sortBy = "Date",
            string sortOrder = "desc", int page = 1)
        {
            var userId = _userManager.GetUserId(User)!;
            const int pageSize = 10;
            var query = _context.Expenses.Where(e => e.UserId == userId);

            if (!string.IsNullOrEmpty(category))
                query = query.Where(e => e.Category == category);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(e => e.Title.Contains(search) || (e.Notes != null && e.Notes.Contains(search)));
            if (startDate.HasValue)
                query = query.Where(e => e.Date >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(e => e.Date <= endDate.Value);

            query = (sortBy, sortOrder) switch
            {
                ("Amount",   "asc") => query.OrderBy(e => e.Amount),
                ("Amount",   _)     => query.OrderByDescending(e => e.Amount),
                ("Title",    "asc") => query.OrderBy(e => e.Title),
                ("Title",    _)     => query.OrderByDescending(e => e.Title),
                ("Category", "asc") => query.OrderBy(e => e.Category),
                ("Category", _)     => query.OrderByDescending(e => e.Category),
                (_,          "asc") => query.OrderBy(e => e.Date),
                _                   => query.OrderByDescending(e => e.Date)
            };

            var totalCount  = await query.CountAsync();
            var totalAmount = totalCount > 0 ? (decimal)await query.SumAsync(e => (double)e.Amount) : 0m;
            var expenses    = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return View(new ExpenseFilterViewModel
            {
                Expenses    = expenses,
                Category    = category,
                SearchTerm  = search,
                StartDate   = startDate,
                EndDate     = endDate,
                SortBy      = sortBy,
                SortOrder   = sortOrder,
                TotalAmount = totalAmount,
                CurrentPage = page,
                TotalPages  = (int)Math.Ceiling(totalCount / (double)pageSize),
                PageSize    = pageSize
            });
        }

        // GET: Expense/Create
        public IActionResult Create()
        {
            ViewBag.Categories = ExpenseCategories.All;
            return View(new Expense { Date = DateTime.Today });
        }

        // POST: Expense/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Expense expense)
        {
            if (ModelState.IsValid)
            {
                expense.UserId    = _userManager.GetUserId(User)!;
                // Normalize Date to UTC — form posts DateTime as Unspecified which Postgres rejects
                expense.Date      = DateTime.SpecifyKind(expense.Date.Date, DateTimeKind.Utc);
                expense.CreatedAt = DateTime.UtcNow;
                _context.Add(expense);
                await _context.SaveChangesAsync();
                ExpensePredictionService.InvalidateCache(expense.UserId);
                TempData["Success"] = $"Expense \"{expense.Title}\" added successfully!";
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Categories = ExpenseCategories.All;
            return View(expense);
        }

        // GET: Expense/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var userId  = _userManager.GetUserId(User)!;
            var expense = await _context.Expenses.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (expense == null) return NotFound();
            ViewBag.Categories = ExpenseCategories.All;
            return View(expense);
        }

        // POST: Expense/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Expense expense)
        {
            if (id != expense.Id) return NotFound();
            var userId = _userManager.GetUserId(User)!;

            if (ModelState.IsValid)
            {
                // Ensure the record belongs to this user
                var exists = await _context.Expenses.AnyAsync(e => e.Id == id && e.UserId == userId);
                if (!exists) return NotFound();

                expense.UserId = userId;
                // Normalize Date to UTC
                expense.Date   = DateTime.SpecifyKind(expense.Date.Date, DateTimeKind.Utc);
                try
                {
                    _context.Update(expense);
                    await _context.SaveChangesAsync();
                    ExpensePredictionService.InvalidateCache(userId);
                    TempData["Success"] = $"Expense \"{expense.Title}\" updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Expenses.Any(e => e.Id == expense.Id)) return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Categories = ExpenseCategories.All;
            return View(expense);
        }

        // POST: Expense/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId  = _userManager.GetUserId(User)!;
            var expense = await _context.Expenses.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (expense != null)
            {
                _context.Expenses.Remove(expense);
                await _context.SaveChangesAsync();
                ExpensePredictionService.InvalidateCache(userId);
                TempData["Success"] = "Expense deleted successfully.";
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: Expense/GetCategoryData (AJAX)
        [HttpGet]
        public async Task<IActionResult> GetCategoryData()
        {
            var userId = _userManager.GetUserId(User)!;
            var now    = DateTime.UtcNow.Date;
            var start  = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1), DateTimeKind.Utc);

            var data = await _context.Expenses
                .Where(e => e.UserId == userId && e.Date >= start)
                .GroupBy(e => e.Category)
                .Select(g => new { category = g.Key, total = g.Sum(e => e.Amount) })
                .ToListAsync();

            return Json(data);
        }
    }
}
