using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SecureServerBackupCommon;

namespace SecureServerBackup.Windows
{
    public partial class NextRunTimeEditWindow : Window
    {
        private readonly DateTime currentNextRun;
        private readonly DateTime earliestAllowedRun;
        private readonly DateTime latestAllowedRun;
        private bool isUpdatingSelections;
        private readonly List<int> availableYears;

        public DateTime SelectedNextRun { get; private set; }

        public NextRunTimeEditWindow(BackupJob job, DateTime currentNextRun, DateTime latestAllowedRun)
        {
            InitializeComponent();

            this.currentNextRun = currentNextRun;
            earliestAllowedRun = DateTime.Now.AddMinutes(1);
            this.latestAllowedRun = latestAllowedRun;
            SelectedNextRun = currentNextRun < earliestAllowedRun ? earliestAllowedRun : currentNextRun;
            availableYears = Enumerable.Range(earliestAllowedRun.Year, latestAllowedRun.Year - earliestAllowedRun.Year + 1).ToList();

            txtCurrentRun.Text = $"Current next run: {currentNextRun:yyyy-MM-dd hh:mm tt}";
            txtLatestAllowed.Text = $"Allowed range: {earliestAllowedRun:yyyy-MM-dd hh:mm tt} to {latestAllowedRun:yyyy-MM-dd hh:mm tt}";
            txtOneTimeNotice.Text = "This changes only the upcoming next run. The original schedule time and date settings stay unchanged for future runs.";

            LoadDateTimeOptions();
            ApplyDateTimeSelection(SelectedNextRun);
            UpdateSelectedValueText();
        }

        private void LoadDateTimeOptions()
        {
            isUpdatingSelections = true;

            cmbYear.ItemsSource = availableYears;

            isUpdatingSelections = false;
        }

        private void ApplyDateTimeSelection(DateTime value)
        {
            isUpdatingSelections = true;

            cmbYear.SelectedItem = value.Year;
            RefreshMonthOptions(value.Month);
            RefreshDayOptions(value.Day);
            RefreshTimeOptions(value.Hour >= 12 ? "PM" : "AM", value.Hour % 12 == 0 ? 12 : value.Hour % 12, value.Minute.ToString("00"));

            isUpdatingSelections = false;
        }

        private void RefreshMonthOptions(int? preferredMonth = null)
        {
            if (cmbYear.SelectedItem is not int year)
            {
                return;
            }

            int minMonth = year == earliestAllowedRun.Year ? earliestAllowedRun.Month : 1;
            int maxMonth = year == latestAllowedRun.Year ? latestAllowedRun.Month : 12;
            var months = Enumerable.Range(minMonth, maxMonth - minMonth + 1).ToList();

            cmbMonth.ItemsSource = months;
            cmbMonth.SelectedItem = preferredMonth.HasValue && months.Contains(preferredMonth.Value)
                ? preferredMonth.Value
                : months.FirstOrDefault();
        }

        private void RefreshDayOptions(int? preferredDay = null)
        {
            if (cmbYear.SelectedItem is not int year || cmbMonth.SelectedItem is not int month)
            {
                return;
            }

            int minDay = year == earliestAllowedRun.Year && month == earliestAllowedRun.Month ? earliestAllowedRun.Day : 1;
            int maxDay = year == latestAllowedRun.Year && month == latestAllowedRun.Month
                ? latestAllowedRun.Day
                : DateTime.DaysInMonth(year, month);
            var days = Enumerable.Range(minDay, maxDay - minDay + 1).ToList();

            cmbDay.ItemsSource = days;
            cmbDay.SelectedItem = preferredDay.HasValue && days.Contains(preferredDay.Value)
                ? preferredDay.Value
                : days.FirstOrDefault();
        }

        private void RefreshTimeOptions(string? preferredPeriod = null, int? preferredHour = null, string? preferredMinute = null)
        {
            if (cmbYear.SelectedItem is not int year ||
                cmbMonth.SelectedItem is not int month ||
                cmbDay.SelectedItem is not int day)
            {
                return;
            }

            var periods = GetAvailablePeriods(year, month, day);
            cmbPeriod.ItemsSource = periods;
            cmbPeriod.SelectedItem = !string.IsNullOrWhiteSpace(preferredPeriod) && periods.Contains(preferredPeriod)
                ? preferredPeriod
                : periods.FirstOrDefault();

            if (cmbPeriod.SelectedItem is not string selectedPeriod)
            {
                cmbHour.ItemsSource = Array.Empty<int>();
                cmbMinute.ItemsSource = Array.Empty<string>();
                return;
            }

            var hours = GetAvailableHours(year, month, day, selectedPeriod);
            cmbHour.ItemsSource = hours;
            cmbHour.SelectedItem = preferredHour.HasValue && hours.Contains(preferredHour.Value)
                ? preferredHour.Value
                : hours.FirstOrDefault();

            if (cmbHour.SelectedItem is not int selectedHour)
            {
                cmbMinute.ItemsSource = Array.Empty<string>();
                return;
            }

            var minutes = GetAvailableMinutes(year, month, day, selectedPeriod, selectedHour);
            cmbMinute.ItemsSource = minutes;
            cmbMinute.SelectedItem = !string.IsNullOrWhiteSpace(preferredMinute) && minutes.Contains(preferredMinute)
                ? preferredMinute
                : minutes.FirstOrDefault();
        }

        private List<string> GetAvailablePeriods(int year, int month, int day)
        {
            return GetCandidateTimesForDate(year, month, day)
                .Select(candidate => candidate.Hour >= 12 ? "PM" : "AM")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static period => period, StringComparer.Ordinal)
                .ToList();
        }

        private List<int> GetAvailableHours(int year, int month, int day, string period)
        {
            return GetCandidateTimesForDate(year, month, day)
                .Where(candidate => string.Equals(candidate.Hour >= 12 ? "PM" : "AM", period, StringComparison.Ordinal))
                .Select(candidate =>
                {
                    int hour = candidate.Hour % 12;
                    return hour == 0 ? 12 : hour;
                })
                .Distinct()
                .OrderBy(static hour => hour)
                .ToList();
        }

        private List<string> GetAvailableMinutes(int year, int month, int day, string period, int hour12)
        {
            return GetCandidateTimesForDate(year, month, day)
                .Where(candidate => string.Equals(candidate.Hour >= 12 ? "PM" : "AM", period, StringComparison.Ordinal))
                .Where(candidate =>
                {
                    int candidateHour = candidate.Hour % 12;
                    candidateHour = candidateHour == 0 ? 12 : candidateHour;
                    return candidateHour == hour12;
                })
                .Select(candidate => candidate.Minute.ToString("00"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static minute => minute, StringComparer.Ordinal)
                .ToList();
        }

        private List<DateTime> GetCandidateTimesForDate(int year, int month, int day)
        {
            DateTime dayStart = new(year, month, day, 0, 0, 0);
            DateTime rangeStart = dayStart == earliestAllowedRun.Date ? earliestAllowedRun : dayStart;
            DateTime rangeEnd = dayStart == latestAllowedRun.Date ? latestAllowedRun : dayStart.AddDays(1).AddMinutes(-1);

            var candidates = new List<DateTime>();
            for (DateTime candidate = rangeStart; candidate <= rangeEnd; candidate = candidate.AddMinutes(1))
            {
                candidates.Add(candidate);
            }

            return candidates;
        }

        private void DateTimePart_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingSelections)
            {
                return;
            }

            if (ReferenceEquals(sender, cmbYear))
            {
                isUpdatingSelections = true;
                RefreshMonthOptions();
                RefreshDayOptions();
                RefreshTimeOptions();
                isUpdatingSelections = false;
            }
            else if (ReferenceEquals(sender, cmbMonth))
            {
                isUpdatingSelections = true;
                RefreshDayOptions();
                RefreshTimeOptions();
                isUpdatingSelections = false;
            }
            else if (ReferenceEquals(sender, cmbDay))
            {
                isUpdatingSelections = true;
                RefreshTimeOptions();
                isUpdatingSelections = false;
            }
            else if (ReferenceEquals(sender, cmbPeriod))
            {
                isUpdatingSelections = true;
                RefreshTimeOptions(cmbPeriod.SelectedItem as string);
                isUpdatingSelections = false;
            }
            else if (ReferenceEquals(sender, cmbHour))
            {
                isUpdatingSelections = true;
                RefreshTimeOptions(cmbPeriod.SelectedItem as string, cmbHour.SelectedItem as int?);
                isUpdatingSelections = false;
            }

            if (TryGetSelectedDateTime(out var selectedValue))
            {
                SelectedNextRun = selectedValue;
                UpdateSelectedValueText();
            }
        }

        private bool TryGetSelectedDateTime(out DateTime selectedValue)
        {
            selectedValue = SelectedNextRun;

            if (cmbYear.SelectedItem is not int year ||
                cmbMonth.SelectedItem is not int month ||
                cmbDay.SelectedItem is not int day ||
                cmbHour.SelectedItem is not int hour ||
                cmbMinute.SelectedItem is not string minuteText ||
                cmbPeriod.SelectedItem is not string period ||
                !int.TryParse(minuteText, out int minute))
            {
                return false;
            }

            int hour24 = hour % 12;
            if (string.Equals(period, "PM", StringComparison.OrdinalIgnoreCase))
            {
                hour24 += 12;
            }

            selectedValue = new DateTime(year, month, day, hour24, minute, 0);
            return true;
        }

        private void UpdateSelectedValueText()
        {
            txtSelectedValue.Text = $"Selected next run: {SelectedNextRun:yyyy-MM-dd hh:mm tt}";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedDateTime(out var selectedValue))
            {
                CustomDialogService.ShowWarning(this, "Please select a valid date and time.", "Invalid Selection");
                return;
            }

            if (selectedValue < earliestAllowedRun || selectedValue > latestAllowedRun)
            {
                CustomDialogService.ShowWarning(
                    this,
                    $"The selected next run must be between {earliestAllowedRun:yyyy-MM-dd hh:mm tt} and {latestAllowedRun:yyyy-MM-dd hh:mm tt}.",
                    "Invalid Selection");
                return;
            }

            SelectedNextRun = selectedValue;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}