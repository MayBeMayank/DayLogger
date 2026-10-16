using DayLogger.Pages;

namespace DayLogger
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("meals", typeof(MealsCalendarPage));
            Routing.RegisterRoute("mealday", typeof(MealDayPage));
            Routing.RegisterRoute("expenses", typeof(ExpensesCalendarPage));
            Routing.RegisterRoute("expenseday", typeof(ExpenseDayPage));
            Routing.RegisterRoute("backup", typeof(BackupPage));
            Routing.RegisterRoute("reminders", typeof(RemindersPage));
            Routing.RegisterRoute("streaks",typeof(StreaksPage));
            Routing.RegisterRoute("streak", typeof(StreakDetailPage));
            Routing.RegisterRoute("streakbin", typeof(StreakBinPage));
        }
    }
}
