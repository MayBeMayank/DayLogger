using DayLogger.Services;

namespace DayLogger.Pages;

public partial class BackupPage : ContentPage
{
    readonly BackupService _backup;

    public BackupPage(BackupService backup)
    {
        InitializeComponent();
        _backup = backup;
    }

    void OnPasswordChanged(object? sender, TextChangedEventArgs e) => RepeatBox.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

    void Busy(bool busy, string? status = null)
    {
        BackupButton.IsEnabled = RestoreButton.IsEnabled = !busy;
        if (status != null) StatusLabel.Text = status;
    }

    async void OnBackupClicked(object? sender, EventArgs e)
    {
        var password = string.IsNullOrEmpty(PasswordEntry.Text) ? null : PasswordEntry.Text;
        if (password != null && password != RepeatEntry.Text)
        {
            StatusLabel.Text = "The two passwords don't match.";
            return;
        }

        Busy(true, "creating backup…");
        try
        {
            var path = await Task.Run(() => _backup.CreateAsync(password));
            Busy(false, password == null ? "backup created" : "encrypted backup created");
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "DayLogger backup", File = new ShareFile(path) });
        }
        catch (Exception ex)
        {
            Busy(false, $"backup failed: {ex.Message}");
        }
        finally { Busy(false); }
    }

    async void OnRestoreClicked(object? sender, EventArgs e)
    {
        FileResult? picked;
        try { picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a DayLogger backup (.dlbackup)" }); }
        catch (Exception ex) { StatusLabel.Text = $"couldn't open the picker: {ex.Message}"; return; }
        if (picked == null) return;

        Busy(true, "restoring…");
        try
        {
            byte[] bytes;
            await using (var s = await picked.OpenReadAsync())
            using (var ms = new MemoryStream())
            {
                await s.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            var password = string.IsNullOrEmpty(PasswordEntry.Text) ? null : PasswordEntry.Text;
            var r = await Task.Run(() => _backup.RestoreAsync(bytes, password));
            Busy(false, r.Total == 0
                ? "nothing new: everything in that backup is already here"
                : $"restored {r.Meals} meals · {r.Expenses} expenses · {r.Streaks} streaks · {r.TickDays} days of ticks. Everything you already had was kept.");
        }
        catch (PasswordRequiredException ex)
        {
            Busy(false, ex.Message);
            PasswordEntry.Focus();
        }
        catch (BackupException ex) { Busy(false, ex.Message); }
        catch (Exception ex) { Busy(false, $"restore failed: {ex.Message}"); }
        finally { Busy(false); }
    }
}
