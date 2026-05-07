// LinuxRestore/restore_tui.cpp
// Terminal UI for Linux restore (using ncurses) - Version 4.7.2.0
// Enhanced with backup date selection, tree view, destination mapping,
// and restore-target disk tree (matching Windows restore page)

#include <ncurses.h>
#include <menu.h>
#include <string>
#include <vector>
#include <memory>
#include <algorithm>
#include "restore_engine.cpp"

// Use types from RestoreEngine
using BackupDate = RestoreEngine::BackupDate;
using RestoreItem = RestoreEngine::RestoreItem;

class RestoreTUI {
private:
    WINDOW* mainWin;
    WINDOW* statusWin;
    std::unique_ptr<RestoreEngine> engine;
    
    std::vector<BackupDate> backupDates;
    std::vector<RestoreItem> restoreTree;
    std::string selectedBackupPath;
    std::string restoreDestination;
    bool restoreToOriginal = true;
    bool restoreToDisk = false;   // true when disk/volume target mode is selected
    std::vector<RestoreEngine::DiskInfo> targetDisks;
    int currentStep = 1; // 1=Select Date, 2=Select Items, 3=Select Destination

    void EnsurePasswordForSelectedBackup() {
        std::ifstream file(selectedBackupPath, std::ios::binary);
        if (!file) {
            return;
        }

        char header[7] = { 0 };
        file.read(header, sizeof(header));
        if (file.gcount() == sizeof(header) && std::strncmp(header, "SSBAES1", sizeof(header)) == 0) {
            std::string password = PromptForPassword("Encrypted backup detected. Enter password:");
            engine->SetBackupPassword(password);
        }
    }

    void InitializeUI() {
        initscr();
        cbreak();
        noecho();
        keypad(stdscr, TRUE);
        curs_set(0);

        // Enable colors
        if (has_colors()) {
            start_color();
            init_pair(1, COLOR_WHITE, COLOR_BLUE);    // Title
            init_pair(2, COLOR_BLACK, COLOR_CYAN);    // Menu selected
            init_pair(3, COLOR_YELLOW, COLOR_BLACK);  // Status
            init_pair(4, COLOR_GREEN, COLOR_BLACK);   // Success
            init_pair(5, COLOR_RED, COLOR_BLACK);     // Error
            init_pair(6, COLOR_CYAN, COLOR_BLACK);    // Info
        }

        int height, width;
        getmaxyx(stdscr, height, width);

        // Create windows
        mainWin = newwin(height - 3, width, 0, 0);
        statusWin = newwin(3, width, height - 3, 0);

        box(mainWin, 0, 0);
        box(statusWin, 0, 0);

        wbkgd(statusWin, COLOR_PAIR(3));
        
        refresh();
        wrefresh(mainWin);
        wrefresh(statusWin);
    }

    void ShowTitle() {
        int width = getmaxx(mainWin);
        wattron(mainWin, COLOR_PAIR(1) | A_BOLD);
        mvwprintw(mainWin, 1, (width - 45) / 2, " BACKUP & RESTORE - Linux Recovery v4.7.2 ");
        wattroff(mainWin, COLOR_PAIR(1) | A_BOLD);
        
        // Show current step
        wattron(mainWin, COLOR_PAIR(6));
        const char* stepName = "";
        switch(currentStep) {
            case 1: stepName = "Step 1: Select Backup & Date"; break;
            case 2: stepName = "Step 2: Select Items to Restore"; break;
            case 3: stepName = "Step 3: Select Destination"; break;
        }
        mvwprintw(mainWin, 2, (width - strlen(stepName)) / 2, "%s", stepName);
        wattroff(mainWin, COLOR_PAIR(6));
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
        
        int width = getmaxx(statusWin) - 4;
        int filled = (width * percentage) / 100;

        mvwprintw(statusWin, 1, 2, "%s", message.c_str());
        
        // Draw progress bar
        mvwprintw(statusWin, 2, 2, "[");
        for (int i = 0; i < width; i++) {
            if (i < filled) {
                waddch(statusWin, '=');
            } else {
                waddch(statusWin, ' ');
            }
        }
        wprintw(statusWin, "] %d%%", percentage);
        
        wrefresh(statusWin);
    }

    // Step 1: Load and select backup date
    bool LoadBackupDates(const std::string& backupPath) {
        UpdateStatus("Scanning backup folder for dates...");
        
        backupDates = engine->EnumerateBackupDates(backupPath);
        
        if (backupDates.empty()) {
            UpdateStatus("No valid backups found in folder", true);
            return false;
        }
        
        UpdateStatus("Found " + std::to_string(backupDates.size()) + " backup point(s)");
        return true;
    }

    int SelectBackupDate() {
        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();

        int startY = 4;
        mvwprintw(mainWin, startY, 2, "Available Backup Dates:");
        mvwprintw(mainWin, startY + 1, 2, "Date                  Type          Size");
        mvwprintw(mainWin, startY + 2, 2, "----------------------------------------------------");
        startY += 3;

        int selected = backupDates.size() - 1; // Default to most recent
        int ch;

        while (true) {
            for (size_t i = 0; i < backupDates.size(); i++) {
                if (i == selected) {
                    wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                }
                
                mvwprintw(mainWin, startY + i, 2, "%-20s %-12s %s",
                    backupDates[i].date.c_str(),
                    backupDates[i].type.c_str(),
                    backupDates[i].size.c_str());
                
                if (i == selected) {
                    wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
                }
            }

            int helpY = startY + backupDates.size() + 2;
            mvwprintw(mainWin, helpY, 2, "UP/DOWN: Navigate | ENTER: Select | Q: Quit");
            wrefresh(mainWin);

            ch = getch();

            switch (ch) {
                case KEY_UP:
                    selected = (selected > 0) ? selected - 1 : backupDates.size() - 1;
                    break;
                case KEY_DOWN:
                    selected = (selected < backupDates.size() - 1) ? selected + 1 : 0;
                    break;
                case 10: // Enter
                case KEY_ENTER:
                    selectedBackupPath = backupDates[selected].path;
                    return selected;
                case 'q':
                case 'Q':
                    return -1;
            }
        }
    }

    // Step 2: Build and select from restore tree
    void LoadBackupContents() {
        UpdateStatus("Loading backup contents...");
        restoreTree = engine->BuildRestoreTree(selectedBackupPath);
        
        if (restoreTree.empty()) {
            UpdateStatus("Failed to load backup contents", true);
        } else {
            UpdateStatus("Backup contents loaded successfully");
        }
    }

    void DrawTree(const std::vector<RestoreItem>& items, int& currentY, int indent, int& selected, int& currentIndex) {
        for (const auto& item : items) {
            if (currentY >= getmaxy(mainWin) - 4) break; // Don't overflow window
            
            bool isCurrent = (currentIndex == selected);
            
            if (isCurrent) {
                wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
            }
            
            // Draw checkbox and item
            std::string prefix(indent * 2, ' ');
            char checkbox = item.checked ? 'X' : ' ';
            mvwprintw(mainWin, currentY, 2, "%s[%c] %s", 
                prefix.c_str(), checkbox, item.name.c_str());
            
            if (isCurrent) {
                wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
            }
            
            currentY++;
            currentIndex++;
            
            // Draw children if not empty
            if (!item.children.empty()) {
                DrawTree(item.children, currentY, indent + 1, selected, currentIndex);
            }
        }
    }

    int CountTreeItems(const std::vector<RestoreItem>& items) {
        int count = items.size();
        for (const auto& item : items) {
            count += CountTreeItems(item.children);
        }
        return count;
    }

    void ToggleItem(std::vector<RestoreItem>& items, int targetIndex, int& currentIndex) {
        for (auto& item : items) {
            if (currentIndex == targetIndex) {
                item.checked = !item.checked;
                // Toggle all children
                SetAllChildren(item.children, item.checked);
                return;
            }
            currentIndex++;
            
            if (!item.children.empty()) {
                ToggleItem(item.children, targetIndex, currentIndex);
            }
        }
    }

    void SetAllChildren(std::vector<RestoreItem>& items, bool checked) {
        for (auto& item : items) {
            item.checked = checked;
            if (!item.children.empty()) {
                SetAllChildren(item.children, checked);
            }
        }
    }

    bool SelectRestoreItems() {
        int selected = 0;
        int totalItems = CountTreeItems(restoreTree);
        
        while (true) {
            wclear(mainWin);
            box(mainWin, 0, 0);
            ShowTitle();

            int startY = 4;
            mvwprintw(mainWin, startY, 2, "Select items to restore (SPACE to toggle):");
            startY += 2;

            int currentY = startY;
            int currentIndex = 0;
            DrawTree(restoreTree, currentY, 0, selected, currentIndex);

            int helpY = getmaxy(mainWin) - 4;
            mvwprintw(mainWin, helpY, 2, "UP/DOWN: Navigate | SPACE: Toggle | N: Next | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();

            switch (ch) {
                case KEY_UP:
                    selected = (selected > 0) ? selected - 1 : totalItems - 1;
                    break;
                case KEY_DOWN:
                    selected = (selected < totalItems - 1) ? selected + 1 : 0;
                    break;
                case ' ': // Space - toggle
                    {
                        int idx = 0;
                        ToggleItem(restoreTree, selected, idx);
                    }
                    break;
                case 'n':
                case 'N':
                    // Check if at least one item is checked
                    if (HasCheckedItem(restoreTree)) {
                        return true;
                    } else {
                        UpdateStatus("Please select at least one item to restore", true);
                        getch();
                    }
                    break;
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

    bool HasCheckedItem(const std::vector<RestoreItem>& items) {
        for (const auto& item : items) {
            if (item.checked) return true;
            if (HasCheckedItem(item.children)) return true;
        }
        return false;
    }

    // Step 3: Select destination — shows the restore target tree first, then
    // destination mode options, mirroring the Windows restore page.
    bool SelectDestination() {
        // Always show the target disk tree first so the user can pick where to restore.
        // This is the primary selection; the mode options below refine the behavior.
        if (!SelectTargetDisk()) {
            // User went back or quit
            if (currentStep == 2) return false;
            return false;
        }

        // After a target is chosen, let the user pick the restore mode.
        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();

        int startY = 4;
        int selected = 0;

        std::vector<std::string> options = {
            "Restore to original location",
            "Restore to selected target (alternate location / overwrite disk)",
            "Metadata-driven disk reconstruction restore"
        };

        // Default to "restore to selected target" since the user just picked one.
        selected = 1;

        while (true) {
            wclear(mainWin);
            box(mainWin, 0, 0);
            ShowTitle();
            startY = 4;

            std::string targetLabel = restoreDestination.empty() ? "(none selected)" : restoreDestination;
            mvwprintw(mainWin, startY, 2, "Target selected: %s", targetLabel.c_str());
            startY += 2;

            mvwprintw(mainWin, startY, 2, "Restore Mode:");
            startY += 2;

            for (size_t i = 0; i < options.size(); i++) {
                if ((int)i == selected) {
                    wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                }
                mvwprintw(mainWin, startY + (int)i, 4, "  %s", options[i].c_str());
                if ((int)i == selected) {
                    wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
                }
            }

            int infoY = startY + (int)options.size() + 1;
            if (selected == 0) {
                mvwprintw(mainWin, infoY, 4, "Files will be restored to their original paths.");
            } else if (selected == 1) {
                mvwprintw(mainWin, infoY, 4, "Files restore to target; disk restores overwrite target.");
                mvwprintw(mainWin, infoY + 1, 4, "Press 'T' to re-select target disk");
            } else if (selected == 2) {
                mvwprintw(mainWin, infoY, 4, "Partition layout rebuilt from backup metadata.");
                mvwprintw(mainWin, infoY + 1, 4, "Press 'D' to enter target disk device manually");
            }

            int helpY = getmaxy(mainWin) - 4;
            mvwprintw(mainWin, helpY, 2, "UP/DOWN: Navigate | ENTER/R: Start Restore | T: Re-select target | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();
            switch (ch) {
                case KEY_UP:
                    selected = (selected > 0) ? selected - 1 : (int)options.size() - 1;
                    break;
                case KEY_DOWN:
                    selected = (selected < (int)options.size() - 1) ? selected + 1 : 0;
                    break;
                case 10:
                case KEY_ENTER:
                    if (selected == 0) {
                        restoreToOriginal = true;
                        restoreToDisk = false;
                        restoreDestination.clear();
                    } else if (selected == 1) {
                        restoreToOriginal = false;
                        restoreToDisk = true;
                    } else if (selected == 2) {
                        restoreToOriginal = false;
                        restoreToDisk = true;
                    }
                    if (restoreToOriginal || !restoreDestination.empty()) {
                        return ConfirmRestore();
                    }
                    break;
                case 't':
                case 'T':
                    if (SelectTargetDisk()) {
                        restoreToDisk = true;
                        restoreToOriginal = false;
                        selected = 1;
                    }
                    break;
                case 'd':
                case 'D':
                    if (selected == 2) {
                        restoreDestination = PromptForPath("Enter target disk device (e.g. /dev/sda):");
                        restoreToDisk = true;
                    }
                    break;
                case 'r':
                case 'R':
                    if (restoreToOriginal || !restoreDestination.empty()) {
                        return ConfirmRestore();
                    } else {
                        UpdateStatus("Please select a target disk first", true);
                        getch();
                    }
                    break;
                case 'b':
                case 'B':
                    currentStep = 2;
                    return false;
                case 'q':
                case 'Q':
                    return false;
            }
        }
    }

    // Disk target tree — primary selection screen for Step 3.
    // Shows all disks/partitions; boot disk is greyed and unselectable.
    bool SelectTargetDisk() {
        // Reload target disks each time so the list is fresh
        targetDisks = engine->ListTargetDisks();

        // Build a flat list of selectable (non-boot) entries
        struct TargetEntry {
            std::string label;
            std::string device;   // full /dev/xxx path
            bool isDisk;          // disk-level vs partition-level
        };
        std::vector<TargetEntry> entries;
        for (const auto& d : targetDisks) {
            if (d.isBootDisk) continue;
            entries.push_back({ "/dev/" + d.device + "  [disk]  " + d.size, "/dev/" + d.device, true });
            for (const auto& p : d.partitions) {
                std::string label = "  /dev/" + p.device + "  " + p.size;
                if (!p.fsType.empty())     label += "  " + p.fsType;
                if (!p.mountPoint.empty()) label += "  " + p.mountPoint;
                entries.push_back({ label, "/dev/" + p.device, false });
            }
        }

        int selected = 0;
        while (true) {
            wclear(mainWin);
            box(mainWin, 0, 0);

            int width = getmaxx(mainWin);
            wattron(mainWin, COLOR_PAIR(1) | A_BOLD);
            mvwprintw(mainWin, 1, (width - 45) / 2, " BACKUP & RESTORE - Linux Recovery ");
            wattroff(mainWin, COLOR_PAIR(1) | A_BOLD);
            wattron(mainWin, COLOR_PAIR(6));
            const char* heading = "Step 3: Select Restore Target Disk or Partition";
            mvwprintw(mainWin, 2, (width - (int)strlen(heading)) / 2, "%s", heading);
            wattroff(mainWin, COLOR_PAIR(6));

            int startY = 4;
            mvwprintw(mainWin, startY, 2, "Click any non-boot disk or partition to restore to it.");
            mvwprintw(mainWin, startY + 1, 2, "Boot/system disk is shown greyed and cannot be selected.");
            startY += 3;

            // Show boot disks greyed-out
            for (const auto& d : targetDisks) {
                if (!d.isBootDisk) continue;
                wattron(mainWin, A_DIM);
                mvwprintw(mainWin, startY++, 2, "[BOOT - cannot restore] /dev/%s  %s",
                          d.device.c_str(), d.size.c_str());
                for (const auto& p : d.partitions) {
                    std::string info = "  /dev/" + p.device + "  " + p.size;
                    if (!p.fsType.empty())     info += "  " + p.fsType;
                    if (!p.mountPoint.empty()) info += "  " + p.mountPoint;
                    mvwprintw(mainWin, startY++, 4, "[boot] %s", info.c_str());
                }
                wattroff(mainWin, A_DIM);
            }
            if (!targetDisks.empty()) startY++;

            if (entries.empty()) {
                wattron(mainWin, COLOR_PAIR(5));
                mvwprintw(mainWin, startY, 2, "No non-boot disks available as restore targets.");
                wattroff(mainWin, COLOR_PAIR(5));
                mvwprintw(mainWin, startY + 2, 2, "Press any key to go back...");
                wrefresh(mainWin);
                getch();
                currentStep = 2;
                return false;
            }

            for (size_t i = 0; i < entries.size(); i++) {
                bool isCurrent = ((int)i == selected);
                if (isCurrent) wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                mvwprintw(mainWin, startY + (int)i, 2, "( ) %s", entries[i].label.c_str());
                if (isCurrent) wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
            }

            int helpY = getmaxy(mainWin) - 4;
            mvwprintw(mainWin, helpY, 2, "UP/DOWN: Navigate | ENTER: Select target | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();
            switch (ch) {
                case KEY_UP:
                    selected = (selected > 0) ? selected - 1 : (int)entries.size() - 1;
                    break;
                case KEY_DOWN:
                    selected = (selected < (int)entries.size() - 1) ? selected + 1 : 0;
                    break;
                case 10:
                case KEY_ENTER:
                    restoreDestination = entries[selected].device;
                    return true;
                case 'b':
                case 'B':
                    currentStep = 2;
                    return false;
                case 'q':
                case 'Q':
                    return false;
            }
        }
    }

    std::string PromptForPath(const std::string& prompt) {
        echo();
        curs_set(1);
        
        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();
        
        mvwprintw(mainWin, 4, 2, "%s", prompt.c_str());
        mvwprintw(mainWin, 6, 2, "Path: ");
        wrefresh(mainWin);
        
        char path[256];
        wgetnstr(mainWin, path, sizeof(path) - 1);
        
        noecho();
        curs_set(0);
        
        return std::string(path);
    }

    std::string PromptForPassword(const std::string& prompt) {
        echo();
        curs_set(1);

        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();

        mvwprintw(mainWin, 4, 2, "%s", prompt.c_str());
        mvwprintw(mainWin, 6, 2, "Password: ");
        wrefresh(mainWin);

        char password[256];
        wgetnstr(mainWin, password, sizeof(password) - 1);

        noecho();
        curs_set(0);

        return std::string(password);
    }

    bool ConfirmRestore() {
        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();

        wattron(mainWin, COLOR_PAIR(5) | A_BOLD);
        mvwprintw(mainWin, 5, 4, "WARNING: This will restore the selected items");
        wattroff(mainWin, COLOR_PAIR(5) | A_BOLD);
        
        mvwprintw(mainWin, 7, 4, "Backup:      %s", selectedBackupPath.c_str());
        mvwprintw(mainWin, 8, 4, "Destination: %s", 
            restoreToOriginal ? "Original locations" : restoreDestination.c_str());
        
        mvwprintw(mainWin, 10, 4, "Continue? (Y/N): ");
        wrefresh(mainWin);

        int ch = getch();
        return (ch == 'y' || ch == 'Y');
    }

    void CollectSelectedPaths(const std::vector<RestoreItem>& items, std::vector<std::string>& paths) {
        for (const auto& item : items) {
            if (item.checked) {
                paths.push_back(item.path);
            } else if (!item.children.empty()) {
                CollectSelectedPaths(item.children, paths);
            }
        }
    }

    void PerformRestore() {
        // Collect selected paths
        std::vector<std::string> selectedPaths;
        CollectSelectedPaths(restoreTree, selectedPaths);
        
        if (selectedPaths.empty()) {
            UpdateStatus("No items selected for restore", true);
            getch();
            return;
        }

        // Execute restore
        std::string dest = restoreToOriginal ? "" : restoreDestination;
        
        auto progressCallback = [this](int percent, const std::string& msg) {
            ShowProgress(percent, msg);
        };

        bool success = engine->RestoreWithManifest(
            selectedBackupPath,
            dest,
            selectedPaths,
            true, // overwrite
            progressCallback
        );

        if (success) {
            UpdateStatus("Restore completed successfully!", false);
        } else {
            UpdateStatus("Restore failed: " + engine->GetLastError(), true);
        }
        
        mvwprintw(mainWin, getmaxy(mainWin) - 2, 2, "Press any key to continue...");
        wrefresh(mainWin);
        getch();
    }

public:
    RestoreTUI() : engine(std::make_unique<RestoreEngine>()) {
        InitializeUI();
    }

    ~RestoreTUI() {
        if (mainWin) delwin(mainWin);
        if (statusWin) delwin(statusWin);
        endwin();
    }

    void Run() {
        while (true) {
            switch (currentStep) {
                case 1: {
                    // Step 1: Select backup and date
                    std::string backupPath = PromptForPath("Enter backup folder path:");
                    if (backupPath.empty()) {
                        return;
                    }
                    
                    if (LoadBackupDates(backupPath)) {
                        int selected = SelectBackupDate();
                        if (selected >= 0) {
                            currentStep = 2;
                        } else {
                            return; // User quit
                        }
                    } else {
                        getch();
                    }
                    break;
                }
                
                case 2: {
                    // Step 2: Select items to restore
                    LoadBackupContents();
                    if (SelectRestoreItems()) {
                        currentStep = 3;
                    } else if (currentStep == 1) {
                        // User went back
                        continue;
                    } else {
                        return; // User quit
                    }
                    break;
                }
                
                case 3: {
                    // Step 3: Select destination and confirm
                    bool confirmed = SelectDestination();
                    if (confirmed) {
                        PerformRestore();
                        return; // Exit after restore
                    } else if (currentStep == 2) {
                        // User went back
                        continue;
                    } else {
                        return; // User quit
                    }
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
    }
    catch (const std::exception& e) {
        endwin();
        fprintf(stderr, "Error: %s\n", e.what());
        return 1;
    }
}
