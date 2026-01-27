// LinuxRestore/restore_tui.cpp
// Terminal UI for Linux restore (using ncurses)

#include <ncurses.h>
#include <menu.h>
#include <string>
#include <vector>
#include <memory>
#include "restore_engine.cpp"

class RestoreTUI {
private:
    WINDOW* mainWin;
    WINDOW* menuWin;
    WINDOW* statusWin;
    std::unique_ptr<RestoreEngine> engine;
    
    std::vector<std::string> disks;
    std::vector<std::string> backups;
    std::string selectedDisk;
    std::string selectedBackup;
    std::string mountPoint = "/mnt/restore";

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
            init_pair(2, COLOR_BLACK, COLOR_CYAN);    // Menu
            init_pair(3, COLOR_YELLOW, COLOR_BLACK);  // Status
            init_pair(4, COLOR_GREEN, COLOR_BLACK);   // Success
            init_pair(5, COLOR_RED, COLOR_BLACK);     // Error
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
        mvwprintw(mainWin, 1, (width - 40) / 2, "  BACKUP & RESTORE - Linux Recovery  ");
        wattroff(mainWin, COLOR_PAIR(1) | A_BOLD);
        mvwprintw(mainWin, 2, (width - 40) / 2, "  Version 4.6.0 - Bootable USB Mode   ");
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

    int ShowMenu(const std::vector<std::string>& items, const std::string& title) {
        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();

        int startY = 4;
        mvwprintw(mainWin, startY, 2, "%s", title.c_str());
        startY += 2;

        int selected = 0;
        int ch;

        while (true) {
            for (size_t i = 0; i < items.size(); i++) {
                if (i == selected) {
                    wattron(mainWin, COLOR_PAIR(2) | A_REVERSE);
                    mvwprintw(mainWin, startY + i, 4, "  %s", items[i].c_str());
                    wattroff(mainWin, COLOR_PAIR(2) | A_REVERSE);
                } else {
                    mvwprintw(mainWin, startY + i, 4, "  %s", items[i].c_str());
                }
            }

            mvwprintw(mainWin, startY + items.size() + 2, 4, "Use UP/DOWN arrows to select, ENTER to confirm, Q to quit");
            wrefresh(mainWin);

            ch = getch();

            switch (ch) {
                case KEY_UP:
                    selected = (selected > 0) ? selected - 1 : items.size() - 1;
                    break;
                case KEY_DOWN:
                    selected = (selected < items.size() - 1) ? selected + 1 : 0;
                    break;
                case 10: // Enter
                case KEY_ENTER:
                    return selected;
                case 'q':
                case 'Q':
                    return -1;
            }
        }
    }

    void ScanDisks() {
        UpdateStatus("Scanning for disks and partitions...");
        disks = engine->ListDisks();
        
        if (disks.empty()) {
            UpdateStatus("No disks found!", true);
        } else {
            UpdateStatus("Found " + std::to_string(disks.size()) + " disk(s)");
        }
    }

    void SelectDisk() {
        if (disks.empty()) {
            ScanDisks();
        }

        if (disks.empty()) {
            UpdateStatus("No disks available", true);
            getch();
            return;
        }

        int selected = ShowMenu(disks, "Select target disk/partition:");
        
        if (selected >= 0 && selected < disks.size()) {
            selectedDisk = disks[selected];
            // Extract device name (e.g., /dev/sda1)
            size_t pos = selectedDisk.find(' ');
            if (pos != std::string::npos) {
                selectedDisk = "/dev/" + selectedDisk.substr(0, pos);
            }
            UpdateStatus("Selected: " + selectedDisk);
        }
    }

    void ScanBackups() {
        UpdateStatus("Scanning for backup files...");
        
        // Scan common locations
        std::vector<std::string> searchPaths = {
            "/media",
            "/mnt",
            "/run/media"
        };

        backups.clear();
        for (const auto& path : searchPaths) {
            auto found = engine->ScanForBackups(path);
            backups.insert(backups.end(), found.begin(), found.end());
        }

        if (backups.empty()) {
            UpdateStatus("No backups found. Please mount backup media first.", true);
        } else {
            UpdateStatus("Found " + std::to_string(backups.size()) + " backup(s)");
        }
    }

    void SelectBackup() {
        if (backups.empty()) {
            ScanBackups();
        }

        if (backups.empty()) {
            getch();
            return;
        }

        // Show simplified paths
        std::vector<std::string> displayPaths;
        for (const auto& backup : backups) {
            displayPaths.push_back(backup);
        }

        int selected = ShowMenu(displayPaths, "Select backup to restore:");
        
        if (selected >= 0 && selected < backups.size()) {
            selectedBackup = backups[selected];
            UpdateStatus("Selected: " + selectedBackup);
        }
    }

    void PerformRestore() {
        if (selectedDisk.empty()) {
            UpdateStatus("Please select a target disk first", true);
            getch();
            return;
        }

        if (selectedBackup.empty()) {
            UpdateStatus("Please select a backup first", true);
            getch();
            return;
        }

        // Confirm
        wclear(mainWin);
        box(mainWin, 0, 0);
        ShowTitle();

        mvwprintw(mainWin, 5, 4, "Ready to restore:");
        mvwprintw(mainWin, 7, 6, "From: %s", selectedBackup.c_str());
        mvwprintw(mainWin, 8, 6, "To:   %s", selectedDisk.c_str());
        mvwprintw(mainWin, 10, 4, "WARNING: This will OVERWRITE data on the target disk!");
        mvwprintw(mainWin, 12, 4, "Press Y to continue, any other key to cancel...");
        wrefresh(mainWin);

        int ch = getch();
        if (ch != 'y' && ch != 'Y') {
            UpdateStatus("Restore cancelled");
            return;
        }

        // Mount NTFS partition
        UpdateStatus("Mounting target partition...");
        if (engine->MountNTFSPartition(selectedDisk, mountPoint) != 0) {
            UpdateStatus("Failed to mount partition: " + engine->GetLastError(), true);
            getch();
            return;
        }

        // Perform restore
        UpdateStatus("Starting restore...");
        sleep(1);

        // Set progress callback
        auto progressCallback = [](int percentage, const char* message) {
            // This will be called from the engine
            // We'll handle it in the main loop
        };

        int result = engine->RestoreFiles(selectedBackup, mountPoint, true);

        if (result == 0) {
            ShowProgress(100, "Restore completed successfully!");
        } else {
            UpdateStatus("Restore failed: " + engine->GetLastError(), true);
        }

        // Unmount
        engine->UnmountPartition(mountPoint);

        getch();
    }

public:
    RestoreTUI() : engine(std::make_unique<RestoreEngine>()) {
        InitializeUI();
    }

    ~RestoreTUI() {
        if (mainWin) delwin(mainWin);
        if (menuWin) delwin(menuWin);
        if (statusWin) delwin(statusWin);
        endwin();
    }

    void Run() {
        std::vector<std::string> mainMenu = {
            "1. Scan for disks and partitions",
            "2. Select target disk/partition",
            "3. Scan for backups",
            "4. Select backup to restore",
            "5. Perform restore",
            "6. Exit"
        };

        while (true) {
            int choice = ShowMenu(mainMenu, "Main Menu - Select an option:");

            switch (choice) {
                case 0:
                    ScanDisks();
                    getch();
                    break;
                case 1:
                    SelectDisk();
                    getch();
                    break;
                case 2:
                    ScanBackups();
                    getch();
                    break;
                case 3:
                    SelectBackup();
                    getch();
                    break;
                case 4:
                    PerformRestore();
                    break;
                case 5:
                case -1:
                    return;
            }
        }
    }
};

int main(int argc, char* argv[]) {
    // Check if running as root
    if (geteuid() != 0) {
        std::cerr << "This program must be run as root (use sudo)" << std::endl;
        return 1;
    }

    RestoreTUI tui;
    tui.Run();

    return 0;
}
