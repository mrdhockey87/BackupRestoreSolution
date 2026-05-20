// LinuxRestore/restore_tui.cpp
// Terminal UI for Linux restore (using ncurses) - Version 4.7.3.0
// Enhanced with backup date selection, tree view, destination mapping,
// and restore-target disk tree mirroring Windows RestoreWindowNew:
//   - Boot disk shown inline greyed/dim (not hidden)
//   - Hidden-partition toggle (H key), Refresh (R key), Expand/Collapse (E/C)
//   - Disk and partition nodes presented as a two-level tree
//   - Restore mode selection follows target selection, matching Windows UX

#include <ncurses.h>
#include <menu.h>
#include <string>
#include <vector>
#include <memory>
#include <algorithm>
#include <chrono>
#include "restore_engine.cpp"

using BackupDate  = RestoreEngine::BackupDate;
using RestoreItem = RestoreEngine::RestoreItem;

class RestoreTUI {
private:
    WINDOW* mainWin;
    WINDOW* statusWin;
    std::unique_ptr<RestoreEngine> engine;

    std::vector<BackupDate>  backupDates;
    std::vector<RestoreItem> restoreTree;
    std::string selectedBackupPath;
    std::string restoreDestination;
    std::string restoreLogPath;
    bool restoreToOriginal = true;
    bool restoreToDisk     = false;
    bool restoreAllItems   = true;
    std::vector<RestoreEngine::DiskInfo> targetDisks;
    bool showHiddenPartitions = false;
    int  currentStep = 1;  // 1=Select Date, 2=Select Items, 3=Select Destination

    // -----------------------------------------------------------------------
    // UI helpers
    // -----------------------------------------------------------------------

    void InitializeUI() {
        initscr();
        cbreak();
        noecho();
        keypad(stdscr, TRUE);
        curs_set(0);

        if (has_colors()) {
            start_color();
            init_pair(1, COLOR_WHITE,  COLOR_BLUE);    // Title bar
            init_pair(2, COLOR_BLACK,  COLOR_CYAN);    // Selected row
            init_pair(3, COLOR_YELLOW, COLOR_BLACK);   // Status bar
            init_pair(4, COLOR_GREEN,  COLOR_BLACK);   // Success / normal status
            init_pair(5, COLOR_RED,    COLOR_BLACK);   // Error / boot-disk warning
            init_pair(6, COLOR_CYAN,   COLOR_BLACK);   // Step heading / info
            init_pair(7, COLOR_WHITE,  COLOR_BLACK);   // Normal text
        }

        int height, width;
        getmaxyx(stdscr, height, width);

        mainWin   = newwin(height - 3, width, 0, 0);
        statusWin = newwin(3, width, height - 3, 0);

        box(mainWin,   0, 0);
        box(statusWin, 0, 0);
        wbkgd(statusWin, COLOR_PAIR(3));

        refresh();
        wrefresh(mainWin);
        wrefresh(statusWin);
    }

    void ShowTitle(const char* stepName = nullptr) {
        int width = getmaxx(mainWin);
        wattron(mainWin, COLOR_PAIR(1) | A_BOLD);
        std::string title = " Secure Server Backup — Linux Recovery ";
        mvwprintw(mainWin, 1, (width - (int)title.size()) / 2, "%s", title.c_str());
        wattroff(mainWin, COLOR_PAIR(1) | A_BOLD);

        if (stepName) {
            wattron(mainWin, COLOR_PAIR(6));
            mvwprintw(mainWin, 2, (width - (int)strlen(stepName)) / 2, "%s", stepName);
            wattroff(mainWin, COLOR_PAIR(6));
        }
        wrefresh(mainWin);
    }

    void UpdateStatus(const std::string& message, bool isError = false) {
        wclear(statusWin);
        box(statusWin, 0, 0);
        if (isError) {
            wattron(statusWin, COLOR_PAIR(5) | A_BOLD);
            mvwprintw(statusWin, 1, 2, "ERROR: %s", message.c_str());
            wattroff(statusWin, COLOR_PAIR(5) | A_BOLD);
        } else {
            wattron(statusWin, COLOR_PAIR(4));
            mvwprintw(statusWin, 1, 2, "%s", message.c_str());
            wattroff(statusWin, COLOR_PAIR(4));
        }
        wrefresh(statusWin);
    }

    void ShowProgress(int percentage, const std::string& message) {
        wclear(statusWin);
        box(statusWin, 0, 0);
        int width  = getmaxx(statusWin) - 4;
        int filled = (width * percentage) / 100;
        mvwprintw(statusWin, 1, 2, "%s", message.c_str());
        mvwprintw(statusWin, 2, 2, "[");
        for (int i = 0; i < width; i++) waddch(statusWin, i < filled ? '=' : ' ');
        wprintw(statusWin, "] %d%%", percentage);
        wrefresh(statusWin);
    }

    // -----------------------------------------------------------------------
    // Step 1: select backup date
    // -----------------------------------------------------------------------

    bool LoadBackupDates(const std::string& backupPath) {
        UpdateStatus("Scanning backup folder...");
        backupDates = engine->EnumerateBackupDates(backupPath);
        if (backupDates.empty()) {
            UpdateStatus("No valid backups found in folder", true);
            return false;
        }
        UpdateStatus("Found " + std::to_string(backupDates.size()) + " backup point(s)");
        return true;
    }

    int SelectBackupDate() {
        wclear(mainWin); box(mainWin, 0, 0);
        ShowTitle("Step 1: Select Backup & Date");

        int startY = 4;
        mvwprintw(mainWin, startY, 2, "Available Backup Points:");
        mvwprintw(mainWin, startY + 1, 2, "%-22s %-14s %s", "Date", "Type", "Size");
        mvwprintw(mainWin, startY + 2, 2, "-----------------------------------------------------");
        startY += 3;

        int selected = (int)backupDates.size() - 1;
        while (true) {
            for (int i = 0; i < (int)backupDates.size(); i++) {
                if (i == selected) wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                mvwprintw(mainWin, startY + i, 2, "%-22s %-14s %s",
                    backupDates[i].date.c_str(),
                    backupDates[i].type.c_str(),
                    backupDates[i].size.c_str());
                if (i == selected) wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
            }
            int helpY = startY + (int)backupDates.size() + 2;
            mvwprintw(mainWin, helpY, 2, "UP/DOWN: Navigate | ENTER: Select | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();
            switch (ch) {
                case KEY_UP:   selected = selected > 0 ? selected - 1 : (int)backupDates.size() - 1; break;
                case KEY_DOWN: selected = selected < (int)backupDates.size() - 1 ? selected + 1 : 0; break;
                case 10: case KEY_ENTER:
                    selectedBackupPath = backupDates[selected].path;
                    return selected;
                case 'q': case 'Q': return -1;
            }
        }
    }

    void EnsurePasswordForSelectedBackup() {
        std::ifstream file(selectedBackupPath, std::ios::binary);
        if (!file) return;
        char header[7] = { 0 };
        file.read(header, sizeof(header));
        if (file.gcount() == sizeof(header) &&
            std::strncmp(header, "SSBAES1", sizeof(header)) == 0) {
            std::string password = PromptForPassword(
                "Encrypted backup — enter password:");
            engine->SetBackupPassword(password);
        }
    }

    // -----------------------------------------------------------------------
    // Step 2: select items to restore
    // -----------------------------------------------------------------------

    void LoadBackupContents() {
        UpdateStatus("Loading backup contents...");
        restoreTree = engine->BuildRestoreTree(selectedBackupPath);
        UpdateStatus(restoreTree.empty()
            ? "Failed to load backup contents" : "Backup contents loaded");
    }

    void DrawTreeItems(const std::vector<RestoreItem>& items, int& y, int indent,
                       int& selected, int& idx) {
        for (const auto& item : items) {
            if (y >= getmaxy(mainWin) - 4) break;
            bool isCurrent = (idx == selected);
            if (isCurrent) wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
            std::string prefix(indent * 2, ' ');
            char cb = item.checked ? 'X' : ' ';
            mvwprintw(mainWin, y, 2, "%s[%c] %s", prefix.c_str(), cb, item.name.c_str());
            if (isCurrent) wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
            y++; idx++;
            if (!item.children.empty()) DrawTreeItems(item.children, y, indent + 1, selected, idx);
        }
    }

    int CountItems(const std::vector<RestoreItem>& items) {
        int count = (int)items.size();
        for (const auto& item : items) count += CountItems(item.children);
        return count;
    }

    void ToggleItem(std::vector<RestoreItem>& items, int target, int& idx) {
        for (auto& item : items) {
            if (idx == target) {
                item.checked = !item.checked;
                SetAllChildren(item.children, item.checked);
                return;
            }
            idx++;
            if (!item.children.empty()) ToggleItem(item.children, target, idx);
        }
    }

    void SetAllChildren(std::vector<RestoreItem>& items, bool checked) {
        for (auto& item : items) {
            item.checked = checked;
            if (!item.children.empty()) SetAllChildren(item.children, checked);
        }
    }

    bool HasCheckedItem(const std::vector<RestoreItem>& items) {
        for (const auto& item : items) {
            if (item.checked) return true;
            if (HasCheckedItem(item.children)) return true;
        }
        return false;
    }

    void SetTopLevelItemsChecked(bool checked) {
        for (auto& item : restoreTree) {
            item.checked = checked;
            SetAllChildren(item.children, checked);
        }
    }

    bool ChooseRestoreScope() {
        int selected = restoreAllItems ? 0 : 1;

        while (true) {
            wclear(mainWin); box(mainWin, 0, 0);
            ShowTitle("Step 2: Choose Restore Scope");

            int startY = 4;
            mvwprintw(mainWin, startY, 2, "Restore point: %s", selectedBackupPath.c_str());
            mvwprintw(mainWin, startY + 2, 2, "Choose what to restore before selecting the destination:");

            const char* options[] = {
                "Restore all files, folders, or volumes from this restore point",
                "Select specific files, folders, or volumes before restoring"
            };

            for (int i = 0; i < 2; ++i) {
                bool isCurrent = (i == selected);
                if (isCurrent) wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                mvwprintw(mainWin, startY + 5 + i, 4, "(%c) %s", isCurrent ? '*' : ' ', options[i]);
                if (isCurrent) wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
            }

            mvwprintw(mainWin, getmaxy(mainWin) - 4, 2,
                "UP/DOWN: Navigate | ENTER: Continue | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();
            switch (ch) {
                case KEY_UP:
                case KEY_DOWN:
                    selected = 1 - selected;
                    break;
                case 10:
                case KEY_ENTER:
                    restoreAllItems = (selected == 0);
                    if (restoreAllItems) {
                        SetTopLevelItemsChecked(true);
                    } else {
                        SetTopLevelItemsChecked(false);
                    }
                    return true;
                case 'b':
                case 'B':
                    currentStep = 1;
                    return false;
                case 'q':
                case 'Q':
                    return false;
            }
        }
    }

    bool SelectRestoreItems() {
        int selected   = 0;
        int totalItems = CountItems(restoreTree);
        while (true) {
            wclear(mainWin); box(mainWin, 0, 0);
            ShowTitle("Step 2: Select Items to Restore");
            int y = 4;
            mvwprintw(mainWin, y, 2, "Select items to restore (SPACE to toggle, A = all, N = none):");
            y += 2;
            int idx = 0;
            DrawTreeItems(restoreTree, y, 0, selected, idx);
            int helpY = getmaxy(mainWin) - 4;
            mvwprintw(mainWin, helpY, 2,
                "UP/DOWN: Navigate | SPACE: Toggle | N: Next | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();
            switch (ch) {
                case KEY_UP:   selected = selected > 0 ? selected - 1 : totalItems - 1; break;
                case KEY_DOWN: selected = selected < totalItems - 1 ? selected + 1 : 0; break;
                case ' ': { int i = 0; ToggleItem(restoreTree, selected, i); } break;
                case 'a': case 'A': SetAllChildren(restoreTree, true);  break;
                case 'z': case 'Z': SetAllChildren(restoreTree, false); break;
                case 'n': case 'N':
                    if (HasCheckedItem(restoreTree)) return true;
                    UpdateStatus("Please select at least one item", true); getch();
                    break;
                case 'b': case 'B': currentStep = 1; return false;
                case 'q': case 'Q': return false;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Step 3: select restore destination
    // Disk target tree is always shown first (mirrors Windows page), then mode.
    // -----------------------------------------------------------------------

    // Build a flat list of displayable tree rows from loaded targetDisks.
    // Boot disks/partitions are included but flagged so we can dim them.
    struct TargetRow {
        std::string label;
        std::string device;     // /dev/xxx path
        bool        isBootDisk;
        bool        isHidden;
        bool        isDisk;     // true = disk level, false = partition level
    };

    std::vector<TargetRow> buildTargetRows() const {
        std::vector<TargetRow> rows;
        for (const auto& d : targetDisks) {
            // Disk row — always shown
            std::string label = "/dev/" + d.device + "  [Disk]  " + d.size;
            if (d.isBootDisk) label += "  *** BOOT/SYSTEM — cannot restore ***";
            rows.push_back({ label, "/dev/" + d.device, d.isBootDisk, false, true });

            for (const auto& p : d.partitions) {
                if (!showHiddenPartitions && p.isHiddenPartition) continue;
                std::string pl = "  +-- /dev/" + p.device + "  " + p.size;
                if (!p.fsType.empty())     pl += "  [" + p.fsType + "]";
                if (!p.mountPoint.empty()) pl += "  " + p.mountPoint;
                if (d.isBootDisk)          pl += "  (boot)";
                if (p.isHiddenPartition)   pl += "  [hidden]";
                rows.push_back({ pl, "/dev/" + p.device, d.isBootDisk, p.isHiddenPartition, false });
            }
        }
        return rows;
    }

    // Show the disk/partition target tree and return the selected device path,
    // or empty string if the user went back or quit.
    // R = refresh, H = toggle hidden, E = expand (all already shown), B = back.
    bool SelectTargetDisk() {
        targetDisks = engine->ListTargetDisks();

        int selected = 0;
        while (true) {
            auto rows = buildTargetRows();
            // Clamp selection
            if (selected >= (int)rows.size()) selected = 0;

            wclear(mainWin); box(mainWin, 0, 0);
            ShowTitle("Step 3A: Select Restore Target Disk or Partition");

            int width = getmaxx(mainWin);
            int startY = 4;

            wattron(mainWin, COLOR_PAIR(6));
            mvwprintw(mainWin, startY, 2,
                "Select a disk or partition to restore to (boot disk is shown greyed).");
            mvwprintw(mainWin, startY + 1, 2,
                "Hidden partitions: %s", showHiddenPartitions ? "SHOWN" : "hidden (H to toggle)");
            wattroff(mainWin, COLOR_PAIR(6));
            startY += 3;

            for (int i = 0; i < (int)rows.size(); i++) {
                if (startY + i >= getmaxy(mainWin) - 4) break;
                bool isCurrent = (i == selected);

                if (rows[i].isBootDisk) {
                    // Boot disk: dim + different marker
                    if (isCurrent) wattron(mainWin, A_REVERSE | A_DIM);
                    else wattron(mainWin, A_DIM);
                    mvwprintw(mainWin, startY + i, 2, "  %s", rows[i].label.c_str());
                    if (isCurrent) wattroff(mainWin, A_REVERSE | A_DIM);
                    else wattroff(mainWin, A_DIM);
                } else {
                    if (isCurrent) wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                    char marker = isCurrent ? '>' : ' ';
                    mvwprintw(mainWin, startY + i, 2, "%c %s", marker, rows[i].label.c_str());
                    if (isCurrent) wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
                }
            }

            int helpY = getmaxy(mainWin) - 4;
            mvwprintw(mainWin, helpY, 2,
                "UP/DOWN: Navigate | ENTER: Select | R: Refresh | H: Toggle Hidden | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();
            switch (ch) {
                case KEY_UP:
                    selected = selected > 0 ? selected - 1 : (int)rows.size() - 1;
                    break;
                case KEY_DOWN:
                    selected = selected < (int)rows.size() - 1 ? selected + 1 : 0;
                    break;
                case 10: case KEY_ENTER:
                    if (!rows.empty()) {
                        if (rows[selected].isBootDisk) {
                            UpdateStatus(
                                "Boot/system disk cannot be a restore target. "
                                "Select a different disk or partition.", true);
                            getch();
                        } else {
                            restoreDestination = rows[selected].device;
                            return true;
                        }
                    }
                    break;
                case 'r': case 'R':
                    targetDisks = engine->ListTargetDisks();
                    UpdateStatus("Target disks refreshed");
                    break;
                case 'h': case 'H':
                    showHiddenPartitions = !showHiddenPartitions;
                    break;
                case 'b': case 'B':
                    currentStep = 2;
                    return false;
                case 'q': case 'Q':
                    return false;
            }
        }
    }

    bool SelectDestination() {
        // Always show the target disk tree first (Step 3A), then mode options.
        if (!SelectTargetDisk()) return false;

        // Step 3B: choose the restore mode given the selected target.
        static const char* optionLabels[] = {
            "Restore to original location",
            "Restore to selected target (overwrite disk / alternate folder)",
            "Metadata-driven disk reconstruction (rebuild partition layout)"
        };
        const int optCount = 3;
        int selected = 1; // default: restore to selected target

        while (true) {
            wclear(mainWin); box(mainWin, 0, 0);
            ShowTitle("Step 3B: Choose Restore Mode");

            int startY = 4;
            wattron(mainWin, COLOR_PAIR(6));
            mvwprintw(mainWin, startY, 2, "Target: %s",
                restoreDestination.empty() ? "(none)" : restoreDestination.c_str());
            wattroff(mainWin, COLOR_PAIR(6));
            startY += 2;

            mvwprintw(mainWin, startY, 2, "Select restore mode:");
            startY += 2;

            for (int i = 0; i < optCount; i++) {
                bool isCurrent = (i == selected);
                if (isCurrent) wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                // Radio-style indicator
                mvwprintw(mainWin, startY + i, 4, "(%c) %s",
                    isCurrent ? '*' : ' ', optionLabels[i]);
                if (isCurrent) wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
            }

            int infoY = startY + optCount + 1;
            wattron(mainWin, COLOR_PAIR(6));
            switch (selected) {
                case 0:
                    mvwprintw(mainWin, infoY, 4, "Files restored to their original paths.");
                    break;
                case 1:
                    mvwprintw(mainWin, infoY, 4, "Files/volumes written to: %s",
                        restoreDestination.c_str());
                    mvwprintw(mainWin, infoY + 1, 4, "Press T to re-select target disk.");
                    break;
                case 2:
                    mvwprintw(mainWin, infoY, 4,
                        "Partition layout reconstructed from backup metadata.");
                    mvwprintw(mainWin, infoY + 1, 4,
                        "Target: %s  (all data on target will be overwritten)",
                        restoreDestination.c_str());
                    break;
            }
            mvwprintw(mainWin, infoY + 3, 4, "Restore log: %s",
                restoreLogPath.empty() ? "Not saved" : restoreLogPath.c_str());
            mvwprintw(mainWin, infoY + 4, 4, "Press L to set or clear the plain-text restore log file.");
            wattroff(mainWin, COLOR_PAIR(6));

            int helpY = getmaxy(mainWin) - 4;
            mvwprintw(mainWin, helpY, 2,
                "UP/DOWN: Navigate | ENTER/R: Start Restore | T: Target | L: Log File | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();
            switch (ch) {
                case KEY_UP:   selected = selected > 0 ? selected - 1 : optCount - 1; break;
                case KEY_DOWN: selected = selected < optCount - 1 ? selected + 1 : 0; break;
                case 10: case KEY_ENTER:
                    restoreToOriginal = (selected == 0);
                    restoreToDisk     = (selected != 0);
                    if (selected == 0) restoreDestination.clear();
                    if (restoreToOriginal || !restoreDestination.empty())
                        return ConfirmRestore();
                    break;
                case 'r': case 'R':
                    restoreToOriginal = (selected == 0);
                    restoreToDisk     = (selected != 0);
                    if (selected == 0) restoreDestination.clear();
                    if (restoreToOriginal || !restoreDestination.empty())
                        return ConfirmRestore();
                    break;
                case 't': case 'T':
                    if (SelectTargetDisk()) {
                        restoreToDisk     = true;
                        restoreToOriginal = false;
                        selected = 1;
                    }
                    break;
                case 'l': case 'L': {
                    std::string logPath = PromptForPath("Enter plain-text restore log file path (blank to disable):");
                    restoreLogPath = logPath;
                    break;
                }
                case 'b': case 'B':
                    // Re-show the target tree
                    if (!SelectTargetDisk()) return false;
                    break;
                case 'q': case 'Q':
                    return false;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Prompt helpers
    // -----------------------------------------------------------------------

    std::string PromptForPath(const std::string& prompt) {
        echo(); curs_set(1);
        wclear(mainWin); box(mainWin, 0, 0);
        ShowTitle();
        mvwprintw(mainWin, 4, 2, "%s", prompt.c_str());
        mvwprintw(mainWin, 6, 2, "Path: ");
        wrefresh(mainWin);
        char buf[512]; wgetnstr(mainWin, buf, sizeof(buf) - 1);
        noecho(); curs_set(0);
        return std::string(buf);
    }

    std::string PromptForPassword(const std::string& prompt) {
        // Use noecho to mask input
        noecho(); curs_set(1);
        wclear(mainWin); box(mainWin, 0, 0);
        ShowTitle();
        mvwprintw(mainWin, 4, 2, "%s", prompt.c_str());
        mvwprintw(mainWin, 6, 2, "Password: ");
        wrefresh(mainWin);
        char buf[256]; wgetnstr(mainWin, buf, sizeof(buf) - 1);
        noecho(); curs_set(0);
        return std::string(buf);
    }

    bool ConfirmRestore() {
        wclear(mainWin); box(mainWin, 0, 0);
        ShowTitle("Confirm Restore");
        wattron(mainWin, COLOR_PAIR(5) | A_BOLD);
        mvwprintw(mainWin, 5, 4, "WARNING: This will overwrite data at the destination.");
        wattroff(mainWin, COLOR_PAIR(5) | A_BOLD);
        mvwprintw(mainWin, 7, 4, "Backup:      %s", selectedBackupPath.c_str());
        mvwprintw(mainWin, 8, 4, "Destination: %s",
            restoreToOriginal ? "Original locations" : restoreDestination.c_str());
        mvwprintw(mainWin, 9, 4, "Log file:    %s",
            restoreLogPath.empty() ? "Not saved" : restoreLogPath.c_str());
        mvwprintw(mainWin, 11, 4, "Proceed? (Y/N): ");
        wrefresh(mainWin);
        int ch = getch();
        return (ch == 'y' || ch == 'Y');
    }

    void CollectSelectedPaths(const std::vector<RestoreItem>& items,
                              std::vector<std::string>& paths) {
        for (const auto& item : items) {
            if (item.checked) paths.push_back(item.path);
            else if (!item.children.empty()) CollectSelectedPaths(item.children, paths);
        }
    }

    void CollectAllTopLevelPaths(std::vector<std::string>& paths) {
        paths.clear();
        for (const auto& item : restoreTree) {
            paths.push_back(item.path);
        }
    }

    // -----------------------------------------------------------------------
    // Restore execution
    // -----------------------------------------------------------------------

    void PerformRestore() {
        std::vector<std::string> selectedPaths;
        if (restoreAllItems) {
            CollectAllTopLevelPaths(selectedPaths);
        } else {
            CollectSelectedPaths(restoreTree, selectedPaths);
        }
        if (selectedPaths.empty()) {
            UpdateStatus("No items selected for restore", true); getch(); return;
        }

        std::string dest = restoreToOriginal ? "" : restoreDestination;
        std::string latestPhaseMessage = "Starting restore...";
        std::string latestItemMessage;
        auto lastUiUpdate = std::chrono::steady_clock::time_point{};
        int lastUiPercent = -1;

        engine->SetLogOperationName("Linux Restore");
        engine->SetLogFilePath(restoreLogPath);

        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle("Restore Progress");
        mvwprintw(mainWin, 4, 2, "Restore in progress...");
        mvwprintw(mainWin, 6, 2, "Phase: %s", latestPhaseMessage.c_str());
        mvwprintw(mainWin, 7, 2, "Item: ");
        wrefresh(mainWin);

        auto cb = [this, &latestPhaseMessage, &latestItemMessage, &lastUiUpdate, &lastUiPercent](int pct, const std::string& msg) {
            auto now = std::chrono::steady_clock::now();
            bool isItemMessage = msg.rfind("Restoring:", 0) == 0 || msg.rfind("Processing:", 0) == 0;
            bool percentChanged = pct != lastUiPercent;
            bool itemChanged = isItemMessage && msg != latestItemMessage;
            bool shouldRefresh = lastUiUpdate == std::chrono::steady_clock::time_point{} ||
                                 percentChanged ||
                                 (!isItemMessage) ||
                                 (itemChanged && now - lastUiUpdate >= std::chrono::milliseconds(150));
            if (!shouldRefresh) {
                return;
            }

            lastUiUpdate = now;
            lastUiPercent = pct;

            if (isItemMessage) {
                latestItemMessage = msg;
            } else {
                latestPhaseMessage = msg;
            }

            ShowProgress(pct, latestPhaseMessage);

            const int itemRow = 8;
            const int width = std::max(1, getmaxx(mainWin) - 10);
            std::string displayedItem = latestItemMessage;
            if ((int)displayedItem.size() > width) {
                displayedItem = displayedItem.substr(0, width - 3) + "...";
            }

            mvwprintw(mainWin, itemRow, 2, "%*s", getmaxx(mainWin) - 4, "");
            mvwprintw(mainWin, itemRow, 2, "Item: %s", displayedItem.c_str());
            wrefresh(mainWin);
        };
        bool success = engine->RestoreWithManifest(
            selectedBackupPath, dest, selectedPaths, true, cb);

        if (success) UpdateStatus("Restore completed successfully!", false);
        else UpdateStatus("Restore failed: " + engine->GetLastError(), true);

        mvwprintw(mainWin, getmaxy(mainWin) - 2, 2, "Press any key to continue...");
        wrefresh(mainWin);
        getch();
    }

public:
    RestoreTUI() : engine(std::make_unique<RestoreEngine>()) {
        InitializeUI();
    }

    ~RestoreTUI() {
        if (mainWin)   delwin(mainWin);
        if (statusWin) delwin(statusWin);
        endwin();
    }

    void Run() {
        while (true) {
            switch (currentStep) {
                case 1: {
                    std::string backupPath = PromptForPath("Enter backup folder path:");
                    if (backupPath.empty()) return;
                    if (LoadBackupDates(backupPath)) {
                        int sel = SelectBackupDate();
                        if (sel >= 0) {
                            EnsurePasswordForSelectedBackup();
                            currentStep = 2;
                        } else return;
                    } else getch();
                    break;
                }
                case 2: {
                    LoadBackupContents();
                    if (restoreTree.empty()) {
                        UpdateStatus("Failed to load backup contents", true);
                        getch();
                        currentStep = 1;
                        continue;
                    }

                    if (!ChooseRestoreScope()) {
                        if (currentStep == 1) {
                            continue;
                        }

                        return;
                    }

                    if (restoreAllItems) {
                        currentStep = 3;
                        continue;
                    }

                    if (SelectRestoreItems()) currentStep = 3;
                    else if (currentStep == 1) continue;
                    else return;
                    break;
                }
                case 3: {
                    bool confirmed = SelectDestination();
                    if (confirmed) { PerformRestore(); return; }
                    else if (currentStep == 2) continue;
                    else return;
                    break;
                }
            }
        }
    }
};

int main() {
    try {
        RestoreTUI tui;
        tui.Run();
        return 0;
    } catch (const std::exception& e) {
        endwin();
        fprintf(stderr, "Error: %s\n", e.what());
        return 1;
    }
}
