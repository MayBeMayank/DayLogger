using DayLogger.Pages;

namespace DayLogger
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("expenses", typeof(ExpensesCalendarPage));
            Routing.RegisterRoute("expenseday", typeof(ExpenseDayPage));
            Routing.RegisterRoute("category", typeof(CategoryPage));
            Routing.RegisterRoute("categories", typeof(CategoriesPage));
            Routing.RegisterRoute("search", typeof(SearchPage));
            Routing.RegisterRoute("insights", typeof(InsightsPage));
            Routing.RegisterRoute("people", typeof(PeoplePage));
            Routing.RegisterRoute("person", typeof(PersonPage));
            Routing.RegisterRoute("backup", typeof(BackupPage));
            Routing.RegisterRoute("reminders", typeof(RemindersPage));
            Routing.RegisterRoute("streaks",typeof(StreaksPage));
            Routing.RegisterRoute("streak", typeof(StreakDetailPage));
            Routing.RegisterRoute("streakbin", typeof(StreakBinPage));
        }
    }
}
