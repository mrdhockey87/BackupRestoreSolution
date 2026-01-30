// LinuxRestore/restore_tui.cpp
// Terminal UI for Linux restore (using ncurses) - Version 4.7.1.0
// Enhanced with backup date selection, tree view, and destination mapping

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
    int currentStep = 1; // 1=Select Date, 2=Select Items, 3=Select Destination

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
        mvwprintw(mainWin, 1, (width - 45) / 2, " BACKUP & RESTORE - Linux Recovery v4.7.1 ");
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

    // Step 3: Select destination
    bool SelectDestination() {
        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();

        int startY = 4;
        int selected = 0;
        
        std::vector<std::string> options = {
            "Restore to original location",
            "Restore to new location"
        };

        while (true) {
            mvwprintw(mainWin, startY, 2, "Restore Destination:");
            startY += 2;

            for (size_t i = 0; i < options.size(); i++) {
                if (i == selected) {
                    wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                }
                
                char bullet = (i == (restoreToOriginal ? 0 : 1)) ? '*' : ' ';
                mvwprintw(mainWin, startY + i, 4, " %c %s", bullet, options[i].c_str());
                
                if (i == selected) {
                    wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
                }
            }

            if (!restoreToOriginal) {
                mvwprintw(mainWin, startY + 4, 4, "Destination path: %s", 
                    restoreDestination.empty() ? "(not set)" : restoreDestination.c_str());
                mvwprintw(mainWin, startY + 5, 4, "Press 'P' to set path");
            }

            int helpY = getmaxy(mainWin) - 4;
            mvwprintw(mainWin, helpY, 2, "UP/DOWN: Navigate | ENTER: Select | R: Start Restore | B: Back | Q: Quit");
            wrefresh(mainWin);

            int ch = getch();

            switch (ch) {
                case KEY_UP:
                    selected = (selected > 0) ? selected - 1 : options.size() - 1;
                    break;
                case KEY_DOWN:
                    selected = (selected < options.size() - 1) ? selected + 1 : 0;
                    break;
                case 10: // Enter
                case KEY_ENTER:
                    restoreToOriginal = (selected == 0);
                    break;
                case 'p':
                case 'P':
                    if (!restoreToOriginal) {
                        restoreDestination = PromptForPath("Enter restore destination path:");
                    }
                    break;
                case 'r':
                case 'R':
                    if (restoreToOriginal || !restoreDestination.empty()) {
                        return ConfirmRestore();
                    } else {
                        UpdateStatus("Please set destination path", true);
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
